/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：角色权限域适配器——RoleAdminAdapter 接口 + caps 能力集 + tenant/portal 双实现
 * 说明：
 *   1. 契约见 types/org-admin.ts；场景差异矩阵见《组织成员角色三模块同构重构实施方案》§4.2。
 *   2. tenant 实现走 /api/tenant/roles 全套（租户级 Token 由 http 拦截器注入）；
 *      portal 实现收口原 portal-role.ts / portal-permission.ts 调用，统一走 core/http.ts 实例。
 *   3. 角色成员 GET/PUT 为混淆串数组直通（后端 Service 层编解码，前端不做任何转换）。
 */

import type { PagedResult } from '@/types/admin'
import type {
  DataScope,
  MemberOption,
  PermissionGroup,
  RoleItem,
  SaveRoleInput,
} from '@/types/org-admin'
import { apiRequest } from './request'

// ================ caps 能力集（§4.2 场景差异矩阵） ================

/** 角色管理场景能力集。 */
export interface RoleAdminCaps {
  /** 是否支持数据范围配置（门户场景无）。 */
  dataScope: boolean
  /** 数据范围可选项（仅 dataScope=true 时有意义）。 */
  scopeOptions: DataScope[]
  /** 内置角色锁定粒度：permissions=仅权限只读（成员可配/描述可编辑）；all=全锁。 */
  builtinLock: 'permissions' | 'all'
  /** 是否允许编辑内置角色（当前两场景均不允许）。 */
  canEditBuiltin: boolean
  /** 门户场景标识（成员模型为 portal_users）。 */
  isPortal: boolean
}

/** 角色管理适配器契约。 */
export interface RoleAdminAdapter {
  readonly caps: RoleAdminCaps
  /** 全部角色列表（含 memberCount/permissionCount）。 */
  listRoles(): Promise<RoleItem[]>
  /** 新建（id=null）/ 更新角色。 */
  saveRole(id: string | null, input: SaveRoleInput): Promise<RoleItem>
  deleteRole(id: string): Promise<void>
  /** 角色已有权限码回显。 */
  fetchRolePermissions(id: string): Promise<string[]>
  /** 全量替换角色权限。 */
  saveRolePermissions(id: string, codes: string[]): Promise<void>
  /** 角色成员 Id 回显（混淆串数组）。 */
  fetchRoleMemberIds(id: string): Promise<string[]>
  /**
   * 成员候选清单。
   * 门户场景必须传 roleId（member-options 挂在角色路由下）；租户场景忽略该参数。
   */
  fetchMemberOptions(roleId?: string): Promise<MemberOption[]>
  /** 全量替换角色成员（混淆串数组直通）。 */
  saveRoleMembers(id: string, ids: string[]): Promise<void>
  /**
   * 权限分组定义（功能权限树数据源）。
   * 门户场景必须传 roleId（permission-options 挂在角色路由下，适配层按 moduleCode 归一分组）；
   * 租户场景忽略该参数（GET /api/tenant/permissions 直接返回分组）。
   */
  fetchPermissionGroups(roleId?: string): Promise<PermissionGroup[]>
}

// ================ 后端 DTO（扁平原始形态） ================

/** 租户角色行（Bas RoleRow：含聚合计数 + int 数据范围）。 */
interface TenantRoleRow {
  id: string
  code: string
  name: string
  description: string | null
  isBuiltin: boolean
  dataScope: number
  memberCount: number
  permissionCount: number
  updatedAt: string
}

/** 门户角色列表项（PortalRoleListItem）。 */
interface PortalRoleRow {
  roleId: string
  roleCode: string
  roleName: string
  description: string | null
  isSystem: boolean
  permissionCount: number
  memberCount: number
  updatedAt: string
}

/** 门户角色详情（PortalRoleDetail：无计数）。 */
interface PortalRoleDetailRow {
  roleId: string
  roleCode: string
  roleName: string
  description: string | null
  isSystem: boolean
  updatedAt: string
}

/** 门户权限选项（PermissionOption）。 */
interface PortalPermissionOption {
  permissionCode: string
  moduleCode: string
  displayName: string
  groupName: string | null
}

/** 门户角色成员（UserRoleMember）。 */
interface PortalRoleMemberRow {
  userId: string
  loginAccount: string
  displayName: string
}

/** 门户可选用户（UserOption）。 */
interface PortalUserOptionRow {
  userId: string
  loginAccount: string
  displayName: string
}

/** 租户成员行（成员候选来源，仅取所需字段）。 */
interface TenantMemberRowRaw {
  tenantMemberId: string
  loginAccount: string
  displayName: string
  memberRole: number
  memberStatus: number
}

/** 租户权限分组行（PermissionGroupRow）。 */
interface TenantPermissionGroupRow {
  moduleCode: string
  moduleName: string
  icon: string | null
  permissions: Array<{ code: string; name: string; description: string | null }>
}

// ================ 枚举映射（后端 int ↔ 前端联合类型） ================

// 后端枚举：All=1 / Custom=2 / DeptAndChildren=3 / Dept=4 / Self=5
const DATA_SCOPE_FROM_INT: Record<number, DataScope> = {
  1: 'All',
  2: 'Custom',
  3: 'DeptAndChild',
  4: 'Dept',
  5: 'Self',
}

const DATA_SCOPE_TO_INT: Record<DataScope, number> = {
  All: 1,
  Custom: 2,
  DeptAndChild: 3,
  Dept: 4,
  Self: 5,
}

function mapTenantRole(r: TenantRoleRow): RoleItem {
  return {
    id: r.id,
    code: r.code,
    name: r.name,
    description: r.description ?? '',
    isBuiltin: r.isBuiltin,
    dataScope: DATA_SCOPE_FROM_INT[r.dataScope] ?? 'Self',
    memberCount: r.memberCount,
    permissionCount: r.permissionCount,
    updatedAt: r.updatedAt,
  }
}

function mapPortalRole(r: PortalRoleRow): RoleItem {
  return {
    id: r.roleId,
    code: r.roleCode,
    name: r.roleName,
    description: r.description ?? '',
    isBuiltin: r.isSystem,
    dataScope: null,
    memberCount: r.memberCount,
    permissionCount: r.permissionCount,
    updatedAt: r.updatedAt,
  }
}

// ================ tenant 实现（/api/tenant/roles 全套） ================

/** 租户场景角色管理适配器。 */
export function createTenantRoleAdminAdapter(): RoleAdminAdapter {
  const caps: RoleAdminCaps = {
    dataScope: true,
    scopeOptions: ['Self', 'Dept', 'DeptAndChild', 'Custom', 'All'],
    builtinLock: 'permissions',
    canEditBuiltin: false,
    isPortal: false,
  }

  return {
    caps,

    listRoles() {
      return apiRequest<TenantRoleRow[]>({ method: 'GET', url: '/api/tenant/roles' })
        .then((rows) => rows.map(mapTenantRole))
    },

    saveRole(id, input) {
      // 后端 SaveRolePayload.DataScope 为 int（System.Text.Json 默认数值枚举）；编码由后端自动生成，前端不传
      const payload = {
        name: input.name,
        description: input.description,
        dataScope: DATA_SCOPE_TO_INT[input.dataScope ?? 'Self'],
      }
      const config = id === null
        ? { method: 'POST' as const, url: '/api/tenant/roles', data: payload }
        : { method: 'PUT' as const, url: `/api/tenant/roles/${id}`, data: payload }
      return apiRequest<TenantRoleRow>(config).then(mapTenantRole)
    },

    deleteRole(id) {
      return apiRequest<void>({ method: 'DELETE', url: `/api/tenant/roles/${id}` })
    },

    fetchRolePermissions(id) {
      return apiRequest<string[]>({ method: 'GET', url: `/api/tenant/roles/${id}/permissions` })
    },

    saveRolePermissions(id, codes) {
      // 入参为顶层 string 数组（后端 [FromBody] IReadOnlyList<string>）
      return apiRequest<void>({ method: 'PUT', url: `/api/tenant/roles/${id}/permissions`, data: codes })
    },

    fetchRoleMemberIds(id) {
      // 混淆串数组直通（后端 Service 层编解码）
      return apiRequest<string[]>({ method: 'GET', url: `/api/tenant/roles/${id}/members` })
    },

    fetchMemberOptions() {
      // 取启用且非待邀请成员作为候选（后端成员分页，一次拉全量）
      return apiRequest<PagedResult<TenantMemberRowRaw>>({
        method: 'GET',
        url: '/api/tenant/members',
        params: { page: 1, pageSize: 500 },
      }).then((res) =>
        res.items
          .filter((m) => m.memberStatus === 1 && m.memberRole !== 3)
          .map((m) => ({ id: m.tenantMemberId, name: m.displayName, account: m.loginAccount })),
      )
    },

    saveRoleMembers(id, ids) {
      return apiRequest<void>({ method: 'PUT', url: `/api/tenant/roles/${id}/members`, data: ids })
    },

    fetchPermissionGroups() {
      return apiRequest<TenantPermissionGroupRow[]>({ method: 'GET', url: '/api/tenant/permissions' })
        .then((groups) =>
          groups.map((g) => ({
            moduleCode: g.moduleCode,
            moduleName: g.moduleName,
            icon: g.icon ?? '',
            permissions: g.permissions.map((p) => ({
              code: p.code,
              name: p.name,
              description: p.description ?? '',
            })),
          })),
        )
    },
  }
}

// ================ portal 实现（/api/portal/admin/roles 全套） ================

/** 门户场景角色管理适配器。 */
export function createPortalRoleAdminAdapter(): RoleAdminAdapter {
  const caps: RoleAdminCaps = {
    dataScope: false,
    scopeOptions: [],
    builtinLock: 'all',
    canEditBuiltin: false,
    isPortal: true,
  }

  const base = '/api/portal/admin/roles'

  return {
    caps,

    listRoles() {
      // 后端分页（pageSize 上限 100），门户角色量少一次拉全
      return apiRequest<PagedResult<PortalRoleRow>>({
        method: 'GET',
        url: base,
        params: { page: 1, pageSize: 100 },
      }).then((res) => res.items.map(mapPortalRole))
    },

    saveRole(id, input) {
      if (id === null) {
        // 角色编码由后端自动生成，前端不传
        return apiRequest<PortalRoleDetailRow>({
          method: 'POST',
          url: base,
          data: { roleName: input.name, description: input.description },
        }).then((d) => mapPortalRole({ ...d, permissionCount: 0, memberCount: 0 }))
      }
      // 门户 RoleCode 唯一索引不可改，PUT 只提交名称/描述
      return apiRequest<PortalRoleDetailRow>({
        method: 'PUT',
        url: `${base}/${id}`,
        data: { roleName: input.name, description: input.description },
      }).then((d) => mapPortalRole({ ...d, permissionCount: 0, memberCount: 0 }))
    },

    deleteRole(id) {
      return apiRequest<void>({ method: 'DELETE', url: `${base}/${id}` })
    },

    fetchRolePermissions(id) {
      return apiRequest<string[]>({ method: 'GET', url: `${base}/${id}/permissions` })
    },

    saveRolePermissions(id, codes) {
      return apiRequest<void>({
        method: 'PUT',
        url: `${base}/${id}/permissions`,
        data: { permissionCodes: codes },
      })
    },

    fetchRoleMemberIds(id) {
      return apiRequest<PortalRoleMemberRow[]>({ method: 'GET', url: `${base}/${id}/members` })
        .then((rows) => rows.map((m) => m.userId))
    },

    fetchMemberOptions(roleId) {
      if (!roleId) {
        return Promise.reject(new Error('门户场景成员候选必须指定角色 Id'))
      }
      return apiRequest<PortalUserOptionRow[]>({ method: 'GET', url: `${base}/${roleId}/member-options` })
        .then((rows) => rows.map((u) => ({ id: u.userId, name: u.displayName, account: u.loginAccount })))
    },

    saveRoleMembers(id, ids) {
      return apiRequest<void>({
        method: 'PUT',
        url: `${base}/${id}/members`,
        data: { userIds: ids },
      })
    },

    fetchPermissionGroups(roleId) {
      if (!roleId) {
        return Promise.reject(new Error('门户场景权限分组必须指定角色 Id'))
      }
      return apiRequest<PortalPermissionOption[]>({
        method: 'GET',
        // 后端路由：权限分配控制器挂在 roles/{roleId}/permissions 下（PortalRolePermissionController）
        url: `${base}/${roleId}/permissions/permission-options`,
      }).then((options) => {
        // 按 moduleCode 归一为 PermissionGroup（方案 §4.2：权限定义源差异由适配层抹平）
        const byModule = new Map<string, PermissionGroup>()
        for (const o of options) {
          let group = byModule.get(o.moduleCode)
          if (!group) {
            group = {
              moduleCode: o.moduleCode,
              moduleName: o.groupName ?? o.moduleCode,
              icon: '',
              permissions: [],
            }
            byModule.set(o.moduleCode, group)
          }
          group.permissions.push({ code: o.permissionCode, name: o.displayName, description: '' })
        }
        return [...byModule.values()]
      })
    },
  }
}
