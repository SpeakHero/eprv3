/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：memberAdmin 适配器单元测试——分页筛选映射 / 邀请 / 重发 / 导入结果映射 / 登录日志页大小收敛
 */

import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AxiosRequestConfig, AxiosResponse } from 'axios'

vi.mock('@/core/http', () => ({
  http: { request: vi.fn() },
}))

import { http } from '@/core/http'
import { createTenantMemberAdminAdapter } from '../memberAdmin'

const requestMock = vi.mocked(http.request)

function ok<T>(data: T): AxiosResponse {
  return { data: { success: true, data } } as AxiosResponse
}

function lastConfig(): AxiosRequestConfig {
  return requestMock.mock.calls.at(-1)![0] as AxiosRequestConfig
}

const memberRow = {
  tenantMemberId: 'm1', portalUserId: 'u1', loginAccount: '13800000000', displayName: '张三',
  phone: null, email: 'a@b.c', orgId: 'o1', roleCodes: ['admin'],
  memberRole: 1, memberStatus: 1, joinAt: '2026-10-23', lastLoginAt: null,
}

beforeEach(() => {
  requestMock.mockReset()
})

describe('createTenantMemberAdminAdapter', () => {
  it('fetchMembers 枚举筛选转 int 且行映射归一可空字段', async () => {
    requestMock.mockResolvedValueOnce(ok({
      items: [memberRow], totalCount: 1, page: 1, pageSize: 20, totalPages: 1, isLastPage: true,
    }))
    const res = await createTenantMemberAdminAdapter().fetchMembers({
      page: 1, pageSize: 20, orgId: 'o1', memberRole: 'TenantAdmin', memberStatus: 'Disabled',
    })
    expect(lastConfig().params).toEqual({ page: 1, pageSize: 20, orgId: 'o1', memberRole: 1, memberStatus: 2 })
    expect(res.items[0]).toMatchObject({
      id: 'm1', portalUserId: 'u1', displayName: '张三', phone: '',
      memberRole: 'TenantAdmin', memberStatus: 'Enabled',
    })
  })

  it('fetchMemberDetail 走 GET {id}', async () => {
    requestMock.mockResolvedValueOnce(ok(memberRow))
    const detail = await createTenantMemberAdminAdapter().fetchMemberDetail('m1')
    expect(lastConfig().url).toBe('/api/tenant/members/m1')
    expect(detail.id).toBe('m1')
  })

  it('inviteMember 走 POST invite 并透传结果', async () => {
    requestMock.mockResolvedValueOnce(ok({ tenantMemberId: 'm9', portalUserId: 'u9' }))
    const result = await createTenantMemberAdminAdapter().inviteMember({
      account: '13900000000', displayName: '李四', orgId: 'o1', roleCodes: ['admin'],
    })
    expect(lastConfig()).toMatchObject({ method: 'POST', url: '/api/tenant/members/invite' })
    expect(result).toEqual({ tenantMemberId: 'm9', portalUserId: 'u9' })
  })

  it('resendInvite 走 POST {id}/resend-invite（B-6 新端点）', async () => {
    requestMock.mockResolvedValueOnce(ok(undefined))
    await createTenantMemberAdminAdapter().resendInvite('m1')
    expect(lastConfig()).toMatchObject({ method: 'POST', url: '/api/tenant/members/m1/resend-invite' })
  })

  it('importMembers 顶层行数组提交并映射 { invited, failed }（B-7 新端点）', async () => {
    requestMock.mockResolvedValueOnce(ok({
      invited: 2,
      failed: [{ line: 3, reason: '组织不存在' }, { line: 5, reason: '账号格式非法' }],
    }))
    const rows = [
      { line: 1, displayName: '甲', account: 'a', orgName: '总部', roleName: '管理员' },
      { line: 3, displayName: '乙', account: 'b', orgName: '不存在', roleName: '管理员' },
    ]
    const result = await createTenantMemberAdminAdapter().importMembers(rows)
    const cfg = lastConfig()
    expect(cfg).toMatchObject({ method: 'POST', url: '/api/tenant/members/import' })
    expect(cfg.data).toEqual(rows)
    expect(result.invited).toBe(2)
    expect(result.failed).toEqual([
      { line: 3, reason: '组织不存在' },
      { line: 5, reason: '账号格式非法' },
    ])
  })

  it('toggle/reset/remove 端点形态正确', async () => {
    const adapter = createTenantMemberAdminAdapter()
    requestMock.mockResolvedValue(ok(undefined))

    await adapter.toggleMember('m1')
    expect(lastConfig()).toMatchObject({ method: 'POST', url: '/api/tenant/members/m1/toggle' })

    await adapter.resetMemberPassword('m1', 'Ab@12345')
    expect(lastConfig()).toMatchObject({ method: 'POST', url: '/api/tenant/members/m1/reset' })
    expect(lastConfig().data).toEqual({ newPassword: 'Ab@12345' })

    await adapter.removeMember('m1')
    expect(lastConfig()).toMatchObject({ method: 'DELETE', url: '/api/tenant/members/m1' })
  })

  it('fetchLoginLogs 页大小收敛至 20 并映射日志行（B-8 新端点）', async () => {
    requestMock.mockResolvedValueOnce(ok({
      items: [
        { eventId: '1234567890123456789', loginAt: '2026-10-23T10:00:00Z', ipAddress: '1.2.3.4', isSuccess: true, failureReason: null },
        { eventId: 987, loginAt: '2026-10-23T09:00:00Z', ipAddress: null, isSuccess: false, failureReason: '密码错误' },
      ],
      totalCount: 2, page: 1, pageSize: 20, totalPages: 1, isLastPage: true,
    }))
    const res = await createTenantMemberAdminAdapter().fetchLoginLogs('m1', 1, 50)
    expect(lastConfig().url).toBe('/api/tenant/members/m1/login-logs')
    expect(lastConfig().params).toEqual({ page: 1, pageSize: 20 })
    expect(res.items[0]).toMatchObject({ id: '1234567890123456789', result: 'Success', ipAddress: '1.2.3.4' })
    expect(res.items[1]).toMatchObject({ id: '987', result: 'Failure', failureReason: '密码错误', ipAddress: '' })
  })

  it('success=false 时透传后端 message（如状态机拒绝）', async () => {
    requestMock.mockResolvedValueOnce({
      data: { success: false, code: 'MEMBER_NOT_PENDING_INVITE', message: '仅待激活成员可重发邀请' },
    } as AxiosResponse)
    await expect(createTenantMemberAdminAdapter().resendInvite('m1')).rejects.toThrow('仅待激活成员可重发邀请')
  })

  it('HTTP 错误响应透传后端 message', async () => {
    requestMock.mockRejectedValueOnce(Object.assign(new Error('conflict'), {
      isAxiosError: true,
      response: { data: { success: false, code: 'MEMBER_ALREADY_EXISTS', message: '该账号已是本企业成员' } },
    }))
    await expect(createTenantMemberAdminAdapter().inviteMember({
      account: 'a', displayName: 'b', orgId: 'o1', roleCodes: [],
    })).rejects.toThrow('该账号已是本企业成员')
  })
})
