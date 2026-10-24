/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：组织/成员/角色权限三模块场景无关契约（租户场景 + 门户场景同构）
 * 说明：
 *   1. 本文件是适配层（api/adapters/*）与组件族（components/*-admin/*）的唯一契约来源，
 *      页面不直接感知后端 DTO 差异（租户 /api/tenant/* 与门户 /api/portal/admin/* 在此归一）。
 *   2. Id 一律为 Hashids 混淆字符串（与后端收发边界约定一致）。
 *   3. 全部为扁平原始字段：树组装、显示名填充、格式化一律在组件族纯函数中完成（B-2/B-4）。
 */

import type {
  DataScope,
  EntityStatus,
  OrgNodeType,
  TenantMemberRole,
} from './tenant'

// ================ 角色 ================

/** 场景无关角色项（租户 RoleRow / 门户 PortalRoleListItem 归一）。 */
export interface RoleItem {
  id: string
  code: string
  name: string
  description: string
  /** 内置角色（租户 isBuiltin / 门户 isSystem）。 */
  isBuiltin: boolean
  /** 数据范围（门户场景恒为 null）。 */
  dataScope: DataScope | null
  memberCount: number
  permissionCount: number
  updatedAt: string
}

/** 新建/编辑角色入参（id 由适配器方法签名承载；编码由后端自动生成，前端不传）。 */
export interface SaveRoleInput {
  name: string
  description: string
  /** 门户场景忽略（恒 null）。 */
  dataScope: DataScope | null
}

// ================ 组织 ================

/** 组织扁平节点（后端 OrganizationFlatRow 直通；树组装在组件族纯函数）。 */
export interface OrgNode {
  id: string
  parentId: string | null
  name: string
  type: OrgNodeType
  managerMemberId: string | null
  status: EntityStatus
  sortOrder: number
  isBuiltin: boolean
  /** 直属成员数（不含下级组织）。 */
  memberCount: number
  createdAt: string
}

/** 新建/编辑组织入参。 */
export interface SaveOrgInput {
  parentId: string | null
  name: string
  type: OrgNodeType
  managerMemberId: string | null
  sortOrder: number
}

// ================ 岗位 ================

/** 岗位项（后端 PositionRow 直通）。 */
export interface PositionItem {
  id: string
  orgId: string
  name: string
  code: string
  /** 编制人数。 */
  headcount: number
  /** 在职人数。 */
  currentCount: number
  status: EntityStatus
  description: string
  updatedAt: string
}

/** 新建/编辑岗位入参（id 为空表示新建；编码由后端自动生成，前端不传）。 */
export interface SavePositionInput {
  id?: string
  name: string
  orgId: string
  headcount: number
  description: string
}

// ================ 成员 ================

/** 场景无关成员项（租户 MemberRow 归一；门户场景成员模型为 portal_users）。 */
export interface MemberItem {
  id: string
  /** 关联自然人（portal_users.id 混淆串）。 */
  portalUserId: string
  loginAccount: string
  /** 本租户内展示名。 */
  displayName: string
  phone: string
  email: string
  orgId: string | null
  roleCodes: string[]
  memberRole: TenantMemberRole
  memberStatus: EntityStatus
  joinAt: string | null
  lastLoginAt: string | null
}

/** 成员登录日志项（后端 MemberLoginLogRow 归一）。 */
export interface MemberLoginLogItem {
  id: string
  at: string
  ipAddress: string
  result: 'Success' | 'Failure'
  failureReason: string | null
}

/** 邀请成员入参。 */
export interface InviteMemberInput {
  /** 手机号或邮箱。 */
  account: string
  displayName: string
  orgId: string
  roleCodes: string[]
}

/** 邀请成员结果（邀请已存在用户时后端返回 null）。 */
export interface InviteMemberResult {
  tenantMemberId: string
  portalUserId: string
}

/** 批量导入行（Excel/CSV 解析后按名称提交，后端负责匹配）。 */
export interface ImportRow {
  line: number
  displayName: string
  account: string
  orgName: string
  roleName: string
}

/** 批量导入失败行。 */
export interface ImportFailure {
  line: number
  reason: string
}

/** 批量导入结果。 */
export interface ImportResult {
  invited: number
  failed: ImportFailure[]
}

// ================ 公共选项 ================

/** 成员候选项（负责人/角色成员分配等场景共用）。 */
export interface MemberOption {
  id: string
  /** 展示名（租户内展示名 / 门户显示名）。 */
  name: string
  /** 登录账号。 */
  account: string
  /** 所属组织显示名（门户场景无）。 */
  orgName?: string
}

// 权限分组契约直接复用 types/tenant.ts（PermissionGroup/PermissionPoint），
// 此处 re-export 使适配层/组件族只需引用本文件即可拿到全部契约。
export type { DataScope, EntityStatus, OrgNodeType, TenantMemberRole } from './tenant'
export type { PermissionGroup, PermissionPoint } from './tenant'
