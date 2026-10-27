/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：全局统一请求层单元测试——ApiResponse 解包 / ApiError 元数据保留 / 超时断网归一 / Blob 下载错误解包
 * 场景对齐后端 ApiResponseWrapperMiddleware 契约：
 *   成功 { success:true, data }；失败 { success:false, code, message, errors? }（400/422 带 errors）；
 *   401/403/404/409/422/500 均带统一信封；超时（ECONNABORTED）与断网（无 response）归一可读文案。
 */

import { AxiosError, type AxiosResponse } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'

// mock core/http 共享实例（统一请求层经该实例发请求）
vi.mock('@/core/http', () => ({
  http: { request: vi.fn() },
}))

import { http } from '@/core/http'
import {
  ApiError,
  apiRequest,
  apiRequestBlob,
  parseBlobApiResponse,
  toApiError,
  unwrapApiResponse,
  type ApiResponse,
} from '../request'

const requestMock = vi.mocked(http.request)

/** 构造成功 Axios 响应（信封 + data）。 */
function ok<T>(data: T): AxiosResponse {
  return { data: { success: true, data, timestamp: '2026-10-27T00:00:00Z' } } as AxiosResponse
}

/** 构造带统一错误信封的 AxiosError（对齐后端中间件输出）。 */
function apiError(status: number, body: Partial<ApiResponse<unknown>>): AxiosError {
  return new AxiosError(
    `Request failed with status code ${status}`,
    'ERR_BAD_REQUEST',
    { headers: {} } as never,
    {},
    { status, statusText: '', data: { success: false, timestamp: '2026-10-27T00:00:00Z', ...body }, headers: {}, config: {} as never },
  )
}

beforeEach(() => {
  requestMock.mockReset()
})

// ================ apiRequest：正常响应 ================

describe('apiRequest 正常响应', () => {
  it('解包 success=true 信封并返回 data', async () => {
    requestMock.mockResolvedValueOnce(ok({ id: 'a1', name: '测试' }))
    const result = await apiRequest<{ id: string; name: string }>({ method: 'get', url: '/api/x' })
    expect(result).toEqual({ id: 'a1', name: '测试' })
    expect(requestMock).toHaveBeenCalledWith({ method: 'get', url: '/api/x' })
  })

  it('data 为数组 / null / 空对象时原样返回', async () => {
    requestMock.mockResolvedValueOnce(ok([1, 2, 3]))
    await expect(apiRequest<number[]>({ url: '/a' })).resolves.toEqual([1, 2, 3])

    requestMock.mockResolvedValueOnce(ok(null))
    await expect(apiRequest<null>({ url: '/b' })).resolves.toBeNull()
  })

  it('200 但 success=false（漏 HTTP 状态对齐的边界）：抛 ApiError 保留 code/message', async () => {
    requestMock.mockResolvedValueOnce({
      data: { success: false, code: 'BIZ_LOCKED', message: '单据已锁定' },
    } as AxiosResponse)
    const err = await apiRequest({ url: '/a' }).catch((e: unknown) => e)
    expect(err).toBeInstanceOf(ApiError)
    expect((err as ApiError).message).toBe('单据已锁定')
    expect((err as ApiError).code).toBe('BIZ_LOCKED')
  })
})

// ================ apiRequest：业务错误（统一信封） ================

describe('apiRequest 业务错误（4xx/5xx 统一信封）', () => {
  it('400 VALIDATION_ERROR：保留 code + status + fieldErrors', async () => {
    requestMock.mockRejectedValueOnce(apiError(400, {
      code: 'VALIDATION_ERROR',
      message: '请求数据校验失败',
      errors: [
        { fieldName: 'tenantName', errorMessage: '租户名称不能为空' },
        { fieldName: 'creditCode', errorMessage: '统一社会信用代码格式错误' },
      ],
    }))
    const err = await apiRequest({ url: '/a' }).catch((e: unknown) => e)
    expect(err).toBeInstanceOf(ApiError)
    const apiErr = err as ApiError
    expect(apiErr.message).toBe('请求数据校验失败')
    expect(apiErr.code).toBe('VALIDATION_ERROR')
    expect(apiErr.status).toBe(400)
    expect(apiErr.fieldErrors).toEqual([
      { fieldName: 'tenantName', errorMessage: '租户名称不能为空' },
      { fieldName: 'creditCode', errorMessage: '统一社会信用代码格式错误' },
    ])
  })

  it('422 业务校验失败（BusinessException）：透传后端 message 与 fieldErrors', async () => {
    requestMock.mockRejectedValueOnce(apiError(422, {
      code: 'VALIDATION_ERROR',
      message: '角色编码已存在',
      errors: [{ fieldName: 'roleName', errorMessage: '同名角色已存在' }],
    }))
    const err = await apiRequest({ url: '/a' }).catch((e: unknown) => e) as ApiError
    expect(err.message).toBe('角色编码已存在')
    expect(err.status).toBe(422)
    expect(err.fieldErrors?.[0]).toEqual({ fieldName: 'roleName', errorMessage: '同名角色已存在' })
  })

  it('409 业务冲突（自定义 code）：code 原样保留供页面分支处理', async () => {
    requestMock.mockRejectedValueOnce(apiError(409, { code: 'TENANT_DATABASE_BUSY', message: '租户数据库初始化中' }))
    const err = await apiRequest({ url: '/a' }).catch((e: unknown) => e) as ApiError
    expect(err.code).toBe('TENANT_DATABASE_BUSY')
    expect(err.status).toBe(409)
    expect(err.message).toBe('租户数据库初始化中')
  })

  it('403 FORBIDDEN / 404 NOT_FOUND / 500 INTERNAL_ERROR：message + status 全量保留', async () => {
    for (const [status, code, message] of [
      [403, 'FORBIDDEN', '无权访问'],
      [404, 'NOT_FOUND', '资源不存在'],
      [500, 'INTERNAL_ERROR', '服务内部错误'],
    ] as const) {
      requestMock.mockRejectedValueOnce(apiError(status, { code, message }))
      const err = await apiRequest({ url: '/a' }).catch((e: unknown) => e) as ApiError
      expect(err).toBeInstanceOf(ApiError)
      expect(err.status).toBe(status)
      expect(err.code).toBe(code)
      expect(err.message).toBe(message)
    }
  })

  it('信封缺 message 时回退 code；code 也缺时回退默认文案', async () => {
    requestMock.mockRejectedValueOnce(apiError(409, { code: 'CONFLICT' }))
    await expect(apiRequest({ url: '/a' })).rejects.toThrow('CONFLICT')

    requestMock.mockRejectedValueOnce(apiError(500, {}))
    await expect(apiRequest({ url: '/a' })).rejects.toThrow('请求失败：HTTP 500')
  })
})

// ================ apiRequest：网络与异常边界 ================

describe('apiRequest 网络异常边界', () => {
  it('超时（ECONNABORTED）归一为 TIMEOUT 可读文案', async () => {
    requestMock.mockRejectedValueOnce(new AxiosError('timeout', 'ECONNABORTED', {} as never))
    const err = await apiRequest({ url: '/a' }).catch((e: unknown) => e) as ApiError
    expect(err).toBeInstanceOf(ApiError)
    expect(err.message).toBe('请求超时，请稍后重试')
    expect(err.code).toBe('TIMEOUT')
  })

  it('断网（无 response）归一为 NETWORK_ERROR 可读文案', async () => {
    requestMock.mockRejectedValueOnce(new AxiosError('Network Error', 'ERR_NETWORK', {} as never))
    const err = await apiRequest({ url: '/a' }).catch((e: unknown) => e) as ApiError
    expect(err.message).toBe('无法连接服务，请确认后端服务已启动')
    expect(err.code).toBe('NETWORK_ERROR')
  })

  it('非 JSON 错误体（网关 HTML 等）：回退 HTTP 状态文案且保留 status', async () => {
    requestMock.mockRejectedValueOnce(new AxiosError(
      'Bad Gateway', 'ERR_BAD_RESPONSE', {} as never, {},
      { status: 502, statusText: '', data: '<html>Bad Gateway</html>', headers: {}, config: {} as never },
    ))
    const err = await apiRequest({ url: '/a' }).catch((e: unknown) => e) as ApiError
    expect(err.message).toBe('请求失败：HTTP 502')
    expect(err.status).toBe(502)
    expect(err.code).toBe('HTTP_502')
  })

  it('非 Axios 异常透传 message 并包装为 ApiError', async () => {
    requestMock.mockRejectedValueOnce(new TypeError('boom'))
    const err = await apiRequest({ url: '/a' }).catch((e: unknown) => e) as ApiError
    expect(err).toBeInstanceOf(ApiError)
    expect(err.message).toBe('boom')
  })
})

// ================ unwrapApiResponse / toApiError 纯函数 ================

describe('unwrapApiResponse / toApiError', () => {
  it('unwrapApiResponse：success 返回 data，fail 抛 ApiError（无 HTTP status）', () => {
    expect(unwrapApiResponse({ success: true, data: 'v' })).toBe('v')
    const err = (() => { try { return unwrapApiResponse({ success: false, code: 'X', message: '失败' }) } catch (e) { return e } })() as ApiError
    expect(err).toBeInstanceOf(ApiError)
    expect(err.code).toBe('X')
    expect(err.status).toBeUndefined()
  })

  it('toApiError：ApiError 实例直通不二次包装', () => {
    const origin = new ApiError('原始', 'C1', 409, [{ fieldName: 'f', errorMessage: 'm' }])
    expect(toApiError(origin)).toBe(origin)
  })
})

// ================ apiRequestBlob：下载场景 ================

describe('apiRequestBlob 下载', () => {
  it('成功：返回 Blob 并解析 Content-Disposition UTF-8 文件名', async () => {
    const blob = new Blob(['xlsx-binary'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
    requestMock.mockResolvedValueOnce({
      data: blob,
      headers: { 'content-disposition': `attachment; filename*=UTF-8''${encodeURIComponent('导入模板.xlsx')}` },
    } as unknown as AxiosResponse)
    const result = await apiRequestBlob({ method: 'get', url: '/api/bas/import-export/templates/t1' })
    expect(result.blob).toBe(blob)
    expect(result.fileName).toBe('导入模板.xlsx')
    expect(requestMock).toHaveBeenCalledWith(expect.objectContaining({ responseType: 'blob' }))
  })

  it('成功：无 Content-Disposition 时回退默认文件名', async () => {
    requestMock.mockResolvedValueOnce({ data: new Blob(['x']), headers: {} } as unknown as AxiosResponse)
    const result = await apiRequestBlob({ method: 'get', url: '/d' })
    expect(result.fileName).toBe('download.xlsx')
  })

  it('失败：Blob 中的 ApiResponse JSON 解出 message/code/status', async () => {
    const errBody = new Blob([JSON.stringify({ success: false, code: 'FORBIDDEN', message: '无权导出该数据' })], { type: 'application/json' })
    requestMock.mockRejectedValueOnce(new AxiosError(
      'Forbidden', 'ERR_BAD_REQUEST', {} as never, {},
      { status: 403, statusText: '', data: errBody, headers: {}, config: {} as never },
    ))
    const err = await apiRequestBlob({ method: 'get', url: '/d' }).catch((e: unknown) => e) as ApiError
    expect(err).toBeInstanceOf(ApiError)
    expect(err.message).toBe('无权导出该数据')
    expect(err.code).toBe('FORBIDDEN')
    expect(err.status).toBe(403)
  })

  it('失败：错误体非 JSON Blob（网络截断等）走通用归一文案', async () => {
    requestMock.mockRejectedValueOnce(new AxiosError(
      'Server Error', 'ERR_BAD_RESPONSE', {} as never, {},
      { status: 502, statusText: '', data: new Blob(['<html>bad gateway</html>'], { type: 'text/html' }), headers: {}, config: {} as never },
    ))
    const err = await apiRequestBlob({ method: 'get', url: '/d' }).catch((e: unknown) => e) as ApiError
    expect(err.message).toBe('请求失败：HTTP 502')
    expect(err.status).toBe(502)
  })
})

// ================ parseBlobApiResponse ================

describe('parseBlobApiResponse', () => {
  it('非 Blob 返回 null；非法 JSON 返回 null；合法信封原样返回', async () => {
    await expect(parseBlobApiResponse({ success: false })).resolves.toBeNull()
    await expect(parseBlobApiResponse(new Blob(['not-json']))).resolves.toBeNull()
    await expect(parseBlobApiResponse(new Blob([JSON.stringify({ foo: 1 })]))).resolves.toBeNull()
    const body = await parseBlobApiResponse(new Blob([JSON.stringify({ success: false, code: 'X', message: 'm' })]))
    expect(body).toMatchObject({ success: false, code: 'X', message: 'm' })
  })
})
