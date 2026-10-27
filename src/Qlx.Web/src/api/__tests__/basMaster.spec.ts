/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：basMaster API 封装单元测试——端点路径 / 查询参数组装 / ApiResponse 解包 / 错误透传 / 导入导出
 */

import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AxiosRequestConfig, AxiosResponse } from 'axios'

vi.mock('@/core/http', () => ({
  http: { request: vi.fn() },
}))

import { http } from '@/core/http'
import {
  changeLogApi,
  codeRuleApi,
  customerApi,
  dictApi,
  importExportApi,
  IMPORT_TEMPLATE_CODES,
  orgMasterApi,
  personApi,
  positionApi,
  supplierApi,
} from '../basMaster'

const requestMock = vi.mocked(http.request)

function ok<T>(data: T): AxiosResponse {
  return { data: { success: true, data } } as AxiosResponse
}

function lastConfig(): AxiosRequestConfig {
  return requestMock.mock.calls.at(-1)![0] as AxiosRequestConfig
}

beforeEach(() => {
  requestMock.mockReset()
})

describe('组织 / 岗位端点', () => {
  it('orgMasterApi.list 无参数时不上送 asOfDate，回看版本时透传', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await orgMasterApi.list()
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/bas/orgs' })
    expect(lastConfig().params).toEqual({})

    requestMock.mockResolvedValueOnce(ok([]))
    await orgMasterApi.list({ asOfDate: '2026-10-25' })
    expect(lastConfig().params).toEqual({ asOfDate: '2026-10-25' })
  })

  it('orgMasterApi.history 走 GET /orgs/{id}/history', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await orgMasterApi.history('o 1')
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/bas/orgs/o%201/history' })
  })

  it('positionApi.list 组装分页与筛选参数（空值不上送）', async () => {
    requestMock.mockResolvedValueOnce(
      ok({ items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0, isLastPage: true }),
    )
    await positionApi.list({ page: 1, pageSize: 20, keyword: '会计' })
    expect(lastConfig().params).toEqual({ page: 1, pageSize: 20, keyword: '会计' })
  })
})

describe('客户 / 供应商 / 人员端点', () => {
  it('customerApi.submitCreditAdjust 自动补齐 customerId 并走审批申请端点', async () => {
    requestMock.mockResolvedValueOnce(ok({ id: 'a1' }))
    await customerApi.submitCreditAdjust('c1', { newLimit: 100000, newCreditGrade: 2, reason: '提额' })
    expect(lastConfig()).toMatchObject({
      method: 'post',
      url: '/api/bas/customers/c1/credit-adjust-requests',
    })
    expect(lastConfig().data).toEqual({
      customerId: 'c1',
      newLimit: 100000,
      newCreditGrade: 2,
      reason: '提额',
    })
  })

  it('supplierApi.changeGrade 走 POST /suppliers/{id}/grade', async () => {
    requestMock.mockResolvedValueOnce(ok({ id: 's1' }))
    await supplierApi.changeGrade('s1', { grade: 3, reason: null })
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/bas/suppliers/s1/grade' })
  })

  it('personApi.plaintext 走 GET /persons/{id}/plaintext', async () => {
    requestMock.mockResolvedValueOnce(ok({ id: 'p1' }))
    await personApi.plaintext('p1')
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/bas/persons/p1/plaintext' })
  })
})

describe('字典 / 编码规则 / 变更日志端点', () => {
  it('dictApi.listItems 以类型编码为路径参数并透传状态筛选', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await dictApi.listItems('sup.grade', { status: '1' })
    expect(lastConfig()).toMatchObject({
      method: 'get',
      url: '/api/bas/dictionaries/sup.grade/items',
    })
    expect(lastConfig().params).toEqual({ status: '1' })
  })

  it('codeRuleApi.disable 以 bizType 为路径参数', async () => {
    requestMock.mockResolvedValueOnce(ok({ bizType: 'org' }))
    await codeRuleApi.disable('org')
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/bas/code-rules/org/disable' })
  })

  it('changeLogApi.list 组装筛选条件', async () => {
    requestMock.mockResolvedValueOnce(
      ok({ items: [], totalCount: 0, page: 2, pageSize: 20, totalPages: 0, isLastPage: true }),
    )
    await changeLogApi.list({
      page: 2,
      pageSize: 20,
      entityType: 'Person',
      changedBy: 'u1',
      from: '2026-10-01',
      to: '2026-10-25',
    })
    expect(lastConfig().params).toEqual({
      page: 2,
      pageSize: 20,
      entityType: 'Person',
      changedBy: 'u1',
      from: '2026-10-01',
      to: '2026-10-25',
    })
  })
})

describe('导入导出', () => {
  it('importData 以 multipart 提交模板编码与文件', async () => {
    requestMock.mockResolvedValueOnce(ok({ totalRows: 0, successRows: 0, failedRows: 0, errors: [], duration: '0s' }))
    const file = new File(['x'], 'supplier.xlsx')
    await importExportApi.importData(IMPORT_TEMPLATE_CODES.supplier, file)
    const config = lastConfig()
    expect(config).toMatchObject({ method: 'post', url: '/api/bas/import-export/import' })
    const form = config.data as FormData
    expect(form.get('templateCode')).toBe('bas.supplier.import')
    expect(form.get('file')).toBeInstanceOf(File)
  })

  it('resolveImportTemplateCode 清单命中时返回编码，未命中时抛错并列出可用模板', async () => {
    requestMock.mockResolvedValueOnce(
      ok([
        { templateCode: 'bas.person.import', name: '人员导入', moduleCode: 'bas', columns: [] },
      ]),
    )
    await expect(importExportApi.resolveImportTemplateCode('bas.person.import')).resolves.toBe(
      'bas.person.import',
    )

    requestMock.mockResolvedValueOnce(
      ok([{ templateCode: 'bas.person.import', name: '人员导入', moduleCode: 'bas', columns: [] }]),
    )
    await expect(importExportApi.resolveImportTemplateCode('bas.unknown.import')).rejects.toThrow(
      '未找到导入模板：bas.unknown.import（可用：bas.person.import）',
    )
  })

  it('resolveExportTemplateCode 由导入模板推导导出模板', () => {
    expect(importExportApi.resolveExportTemplateCode('bas.supplier.import')).toBe(
      'bas.supplier.export',
    )
    expect(importExportApi.resolveExportTemplateCode('bas.supplier')).toBe(
      'bas.supplier.export',
    )
  })

  it('downloadTemplate 从 Content-Disposition 解析中文文件名', async () => {
    requestMock.mockResolvedValueOnce({
      data: new Blob(['x']),
      headers: {
        'content-disposition': "attachment; filename*=UTF-8''%E4%BE%9B%E5%BA%94%E5%95%86.xlsx",
      },
    } as unknown as AxiosResponse)
    const result = await importExportApi.downloadTemplate('bas.supplier.import')
    expect(result.fileName).toBe('供应商.xlsx')
    expect(result.blob).toBeInstanceOf(Blob)
  })
})

describe('错误透传', () => {
  it('success=false 时抛出后端 message（403 / 409 / 422 可见）', async () => {
    requestMock.mockResolvedValueOnce({
      data: { success: false, code: 'FORBIDDEN', message: '缺少字段级权限 bas.person.field.plaintext' },
    } as AxiosResponse)
    await expect(personApi.plaintext('p1')).rejects.toThrow(
      '缺少字段级权限 bas.person.field.plaintext',
    )
  })
})
