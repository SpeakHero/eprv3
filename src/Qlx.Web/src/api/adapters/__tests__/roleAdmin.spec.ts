/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：roleAdmin 适配器单元测试——响应解包 / 错误透传 / 枚举映射 / 混淆串直通 / caps 差异
 */

import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AxiosRequestConfig, AxiosResponse } from 'axios'

// mock core/http 共享实例（适配层统一经 apiRequest 走该实例）
vi.mock('@/core/http', () => ({
  http: { request: vi.fn() },
}))

import { http } from '@/core/http'
import {
  createPortalRoleAdminAdapter,
  createTenantRoleAdminAdapter,
} from '../roleAdmin'

const requestMock = vi.mocked(http.request)

/** 构造成功响应。 */
function ok<T>(data: T): AxiosResponse {
  return { data: { success: true, data } } as AxiosResponse
}

/** 取最近一次调用的请求配置。 */
function lastConfig(): AxiosRequestConfig {
  return requestMock.mock.calls.at(-1)![0] as AxiosRequestConfig
}

beforeEach(() => {
  requestMock.mockReset()
})

// ================ tenant 实现 ================

describe('createTenantRoleAdminAdapter', () => {
  it('caps 表达租户场景差异矩阵（含 Custom 数据范围）', () => {
    const caps = createTenantRoleAdminAdapter().caps
    expect(caps.isPortal).toBe(false)
    expect(caps.dataScope).toBe(true)
    expect(caps.scopeOptions).toEqual(['Self', 'Dept', 'DeptAndChild', 'Custom', 'All'])
    expect(caps.builtinLock).toBe('permissions')
    expect(caps.canEditBuiltin).toBe(false)
  })

  it('listRoles 解包 data 并映射 int 数据范围 + 聚合计数', async () => {
    requestMock.mockResolvedValueOnce(ok([
      { id: 'r1', code: 'admin', name: '管理员', description: null, isBuiltin: true, dataScope: 1, memberCount: 2, permissionCount: 30, updatedAt: '2026-10-23' },
      { id: 'r2', code: 'custom', name: '自定义', description: 'd', isBuiltin: false, dataScope: 2, memberCount: 0, permissionCount: 1, updatedAt: '2026-10-23' },
    ]))
    const roles = await createTenantRoleAdminAdapter().listRoles()
    expect(lastConfig().url).toBe('/api/tenant/roles')
    expect(roles[0]).toMatchObject({ id: 'r1', dataScope: 'All', description: '', memberCount: 2, permissionCount: 30 })
    expect(roles[1].dataScope).toBe('Custom')
  })

  it('saveRole 新建走 POST 且 dataScope 转 int（Custom→2）', async () => {
    requestMock.mockResolvedValueOnce(ok(
      { id: 'r9', code: 'c', name: 'n', description: '', isBuiltin: false, dataScope: 2, memberCount: 0, permissionCount: 0, updatedAt: 't' },
    ))
    const role = await createTenantRoleAdminAdapter().saveRole(null, { name: 'n', description: '', dataScope: 'Custom' })
    const cfg = lastConfig()
    expect(cfg.method).toBe('POST')
    expect(cfg.url).toBe('/api/tenant/roles')
    expect((cfg.data as { dataScope: number }).dataScope).toBe(2)
    expect(role.id).toBe('r9')
  })

  it('saveRole 更新走 PUT {id}', async () => {
    requestMock.mockResolvedValueOnce(ok(
      { id: 'r1', code: 'c', name: 'n2', description: '', isBuiltin: false, dataScope: 5, memberCount: 1, permissionCount: 0, updatedAt: 't' },
    ))
    await createTenantRoleAdminAdapter().saveRole('r1', { name: 'n2', description: '', dataScope: 'Self' })
    const cfg = lastConfig()
    expect(cfg.method).toBe('PUT')
    expect(cfg.url).toBe('/api/tenant/roles/r1')
    expect((cfg.data as { dataScope: number }).dataScope).toBe(5)
  })

  it('角色成员 GET/PUT 混淆串数组直通（顶层裸数组）', async () => {
    const adapter = createTenantRoleAdminAdapter()
    requestMock.mockResolvedValueOnce(ok(['m1', 'm2']))
    await expect(adapter.fetchRoleMemberIds('r1')).resolves.toEqual(['m1', 'm2'])
    expect(lastConfig().url).toBe('/api/tenant/roles/r1/members')

    requestMock.mockResolvedValueOnce(ok(undefined))
    await adapter.saveRoleMembers('r1', ['m3', 'm4'])
    const cfg = lastConfig()
    expect(cfg.method).toBe('PUT')
    expect(cfg.data).toEqual(['m3', 'm4'])
  })

  it('角色权限 GET/PUT 权限码数组直通', async () => {
    const adapter = createTenantRoleAdminAdapter()
    requestMock.mockResolvedValueOnce(ok(['bas.a', 'bas.b']))
    await expect(adapter.fetchRolePermissions('r1')).resolves.toEqual(['bas.a', 'bas.b'])
    requestMock.mockResolvedValueOnce(ok(undefined))
    await adapter.saveRolePermissions('r1', ['bas.a'])
    expect(lastConfig().data).toEqual(['bas.a'])
  })

  it('fetchMemberOptions 过滤启用且非待邀请成员', async () => {
    requestMock.mockResolvedValueOnce(ok({
      items: [
        { tenantMemberId: 'm1', loginAccount: 'a', displayName: '甲', memberRole: 2, memberStatus: 1 },
        { tenantMemberId: 'm2', loginAccount: 'b', displayName: '乙', memberRole: 3, memberStatus: 1 },
        { tenantMemberId: 'm3', loginAccount: 'c', displayName: '丙', memberRole: 2, memberStatus: 2 },
      ],
      totalCount: 3, page: 1, pageSize: 500, totalPages: 1, isLastPage: true,
    }))
    const options = await createTenantRoleAdminAdapter().fetchMemberOptions()
    expect(options).toEqual([{ id: 'm1', name: '甲', account: 'a' }])
  })

  it('fetchPermissionGroups 走 /api/tenant/permissions 并归一可空字段', async () => {
    requestMock.mockResolvedValueOnce(ok([
      { moduleCode: 'bas', moduleName: '基础', icon: null, permissions: [{ code: 'bas.a', name: 'A', description: null }] },
    ]))
    const groups = await createTenantRoleAdminAdapter().fetchPermissionGroups()
    expect(lastConfig().url).toBe('/api/tenant/permissions')
    expect(groups).toEqual([{ moduleCode: 'bas', moduleName: '基础', icon: '', permissions: [{ code: 'bas.a', name: 'A', description: '' }] }])
  })

  it('success=false 时透传后端 message', async () => {
    requestMock.mockResolvedValueOnce({ data: { success: false, code: 'ROLE_CODE_EXISTS', message: '角色编码已存在' } } as AxiosResponse)
    await expect(createTenantRoleAdminAdapter().listRoles()).rejects.toThrow('角色编码已存在')
  })
})

// ================ portal 实现 ================

describe('createPortalRoleAdminAdapter', () => {
  it('caps 表达门户场景差异矩阵', () => {
    const caps = createPortalRoleAdminAdapter().caps
    expect(caps.isPortal).toBe(true)
    expect(caps.dataScope).toBe(false)
    expect(caps.scopeOptions).toEqual([])
    expect(caps.builtinLock).toBe('all')
    expect(caps.canEditBuiltin).toBe(false)
  })

  it('listRoles 解包分页 items 并归一字段（isSystem→isBuiltin，dataScope=null）', async () => {
    requestMock.mockResolvedValueOnce(ok({
      items: [{ roleId: 'p1', roleCode: 'portal.admin', roleName: '超管', description: null, isSystem: true, permissionCount: 33, memberCount: 2, updatedAt: 't' }],
      totalCount: 1, page: 1, pageSize: 100, totalPages: 1, isLastPage: true,
    }))
    const roles = await createPortalRoleAdminAdapter().listRoles()
    expect(lastConfig().url).toBe('/api/portal/admin/roles')
    expect(roles[0]).toMatchObject({ id: 'p1', code: 'portal.admin', name: '超管', isBuiltin: true, dataScope: null, memberCount: 2, permissionCount: 33 })
  })

  it('saveRole 新建/更新分别走 POST/PUT，dataScope 不下发', async () => {
    const adapter = createPortalRoleAdminAdapter()
    requestMock.mockResolvedValueOnce(ok({ roleId: 'p2', roleCode: 'portal.a', roleName: 'A', description: null, isSystem: false, updatedAt: 't' }))
    await adapter.saveRole(null, { name: 'A', description: '', dataScope: null })
    expect(lastConfig().data).toEqual({ roleName: 'A', description: '' })

    requestMock.mockResolvedValueOnce(ok({ roleId: 'p2', roleCode: 'portal.a', roleName: 'A2', description: null, isSystem: false, updatedAt: 't' }))
    await adapter.saveRole('p2', { name: 'A2', description: '', dataScope: null })
    const cfg = lastConfig()
    expect(cfg.url).toBe('/api/portal/admin/roles/p2')
    expect(cfg.data).toEqual({ roleName: 'A2', description: '' })
  })

  it('成员 GET 映射 userId 数组 / PUT 包装 { userIds }', async () => {
    const adapter = createPortalRoleAdminAdapter()
    requestMock.mockResolvedValueOnce(ok([{ userId: 'u1', loginAccount: 'a', displayName: '甲' }]))
    await expect(adapter.fetchRoleMemberIds('p1')).resolves.toEqual(['u1'])

    requestMock.mockResolvedValueOnce(ok(undefined))
    await adapter.saveRoleMembers('p1', ['u1', 'u2'])
    expect(lastConfig().data).toEqual({ userIds: ['u1', 'u2'] })
  })

  it('权限 PUT 包装 { permissionCodes }', async () => {
    requestMock.mockResolvedValueOnce(ok(undefined))
    await createPortalRoleAdminAdapter().saveRolePermissions('p1', ['portal.audit.login'])
    expect(lastConfig().data).toEqual({ permissionCodes: ['portal.audit.login'] })
  })

  it('fetchPermissionGroups 按 moduleCode 归一分组', async () => {
    requestMock.mockResolvedValueOnce(ok([
      { permissionCode: 'portal.a', moduleCode: 'portal', displayName: 'A', groupName: '门户' },
      { permissionCode: 'portal.b', moduleCode: 'portal', displayName: 'B', groupName: '门户' },
      { permissionCode: 'bas.a', moduleCode: 'bas', displayName: 'C', groupName: null },
    ]))
    const groups = await createPortalRoleAdminAdapter().fetchPermissionGroups('p1')
    expect(lastConfig().url).toBe('/api/portal/admin/roles/p1/permissions/permission-options')
    expect(groups).toHaveLength(2)
    expect(groups[0]).toMatchObject({ moduleCode: 'portal', moduleName: '门户' })
    expect(groups[0].permissions.map((p) => p.code)).toEqual(['portal.a', 'portal.b'])
    expect(groups[1].moduleName).toBe('bas')
  })

  it('fetchMemberOptions 映射 UserOption 且必须传 roleId', async () => {
    const adapter = createPortalRoleAdminAdapter()
    await expect(adapter.fetchMemberOptions()).rejects.toThrow('门户场景成员候选必须指定角色 Id')
    requestMock.mockResolvedValueOnce(ok([{ userId: 'u1', loginAccount: 'a', displayName: '甲' }]))
    await expect(adapter.fetchMemberOptions('p1')).resolves.toEqual([{ id: 'u1', name: '甲', account: 'a' }])
    expect(lastConfig().url).toBe('/api/portal/admin/roles/p1/member-options')
  })

  it('HTTP 错误响应透传后端 message', async () => {
    const err = Object.assign(new Error('conflict'), {
      isAxiosError: true,
      response: { data: { success: false, code: 'ROLE_IS_SYSTEM', message: '内置角色不可删除' } },
    })
    requestMock.mockRejectedValueOnce(err)
    await expect(createPortalRoleAdminAdapter().deleteRole('p1')).rejects.toThrow('内置角色不可删除')
  })
})
