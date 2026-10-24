/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：租户自管理域类型定义（对齐 saas-architecture-spec.md 第 5 章 租户自管理域）
 * 说明：Id 一律为 Hashids 混淆字符串（与后端收发边界约定一致）；数据源为真实 REST
 *       （api/adapters/{orgAdmin,memberAdmin,roleAdmin}.ts，经 core/http 实例）。
 */

export type EntityStatus = 'Enabled' | 'Disabled'

/** ============ 组织机构（organizations） ============ */

export type OrgNodeType = 'Company' | 'Department' | 'Team'

export interface TenantOrgNode {
  id: string
  parentId: string | null
  name: string
  type: OrgNodeType
  /** 负责人：指向 portal DB 的 tenant_members.id。 */
  managerMemberId: string | null
  managerName: string | null
  status: EntityStatus
  sortOrder: number
  /** 直属成员数（不含下级组织）。 */
  memberCount: number
  createdAt: string
  children?: TenantOrgNode[]
}

/** 新建 / 编辑组织入参。 */
export interface SaveOrgPayload {
  parentId: string | null
  name: string
  type: OrgNodeType
  managerMemberId: string | null
  sortOrder: number
}

/** ============ 岗位（与部门关联，规范 5.2.1） ============ */

export interface TenantPosition {
  id: string
  name: string
  code: string
  orgId: string
  orgName: string
  /** 编制人数。 */
  headcount: number
  status: EntityStatus
  description: string
  updatedAt: string
}

export interface SavePositionPayload {
  /** 为空表示新建。 */
  id?: string
  name: string
  orgId: string
  headcount: number
  description: string
}

/** 负责人候选（成员摘要）。 */
export interface OrgMemberOption {
  id: string
  /** 租户内展示名。 */
  name: string
  loginAccount: string
  orgName: string
}

/** ============ 成员（tenant_members 租户视角，规范 5.2.2） ============ */

export type TenantMemberRole = 'TenantAdmin' | 'OrdinaryMember' | 'PendingInvite'

export interface TenantMemberRow {
  id: string
  /** 关联自然人（portal_users.id）。 */
  portalUserId: string
  /** 全局登录账号（辅助识别重名）。 */
  loginAccount: string
  /** 本租户内展示名。 */
  displayNameInTenant: string
  phone: string
  email: string
  orgId: string
  orgName: string
  roleCodes: string[]
  roleNames: string[]
  memberRole: TenantMemberRole
  memberStatus: EntityStatus
  joinAt: string
  lastLoginAt: string | null
}

/** 成员在本租户的登录记录。 */
export interface MemberLoginLog {
  id: string
  at: string
  ipAddress: string
  device: string
  result: 'Success' | 'Failure'
}

export interface TenantMemberDetail extends TenantMemberRow {
  logs: MemberLoginLog[]
}

export interface InviteMemberPayload {
  /** 手机号或邮箱（用于匹配既有 PortalUser 或创建待邀请记录）。 */
  account: string
  displayName: string
  orgId: string
  roleCodes: string[]
}

/** 批量导入行（Excel/CSV 解析后按名称提交，后端负责匹配自然人）。 */
export interface ImportMemberRow {
  line: number
  displayName: string
  account: string
  orgName: string
  roleName: string
}

export interface ImportMembersResult {
  invited: number
  failed: Array<{ line: number; reason: string }>
}

/** ============ 角色与权限（roles / role_permissions / member_roles，规范 5.2.3） ============ */

export type DataScope = 'Self' | 'Dept' | 'DeptAndChild' | 'Custom' | 'All'

export interface TenantRole {
  id: string
  code: string
  name: string
  description: string
  /** 系统内置角色（不可删除 / 编码不可改）。 */
  isBuiltin: boolean
  dataScope: DataScope
  permissionCodes: string[]
  memberIds: string[]
  memberCount: number
  permissionCount: number
  updatedAt: string
}

export interface SaveRolePayload {
  name: string
  code: string
  description: string
  dataScope: DataScope
}

export interface PermissionPoint {
  code: string
  name: string
  description: string
}

/** 按模块分组的功能权限定义（permissions 表，Schema 模板自带）。 */
export interface PermissionGroup {
  moduleCode: string
  moduleName: string
  icon: string
  permissions: PermissionPoint[]
}

/** ============ 业务模块（门户授权范围内，规范 5.2.4） ============ */

export interface TenantModuleCard {
  code: string
  name: string
  version: string
  description: string
  features: string[]
  licensed: boolean
  licensedAt: string | null
}

/** ============ 枚举标签映射（各页面共用，禁止散落硬编码） ============ */

export const orgTypeLabels: Record<OrgNodeType, string> = {
  Company: '公司',
  Department: '部门',
  Team: '团队',
}

export const orgTypeIcons: Record<OrgNodeType, string> = {
  Company: 'OfficeBuilding',
  Department: 'House',
  Team: 'UserFilled',
}

export const entityStatusLabels: Record<EntityStatus, string> = {
  Enabled: '启用',
  Disabled: '已停用',
}

export const entityStatusTones: Record<EntityStatus, 'success' | 'danger'> = {
  Enabled: 'success',
  Disabled: 'danger',
}

export const dataScopeLabels: Record<DataScope, string> = {
  Self: '仅本人',
  Dept: '本部门',
  DeptAndChild: '本部门及下级',
  Custom: '自定义',
  All: '全部',
}

export const dataScopeHints: Record<DataScope, string> = {
  Self: '只能查看本人创建或经办的业务数据',
  Dept: '可查看所属组织内全部成员的业务数据',
  DeptAndChild: '可查看所属组织及其所有下级组织的数据',
  Custom: '按自定义成员/组织清单授权（谨慎授予）',
  All: '可查看本企业全部业务数据（谨慎授予）',
}

export const loginResultLabels: Record<MemberLoginLog['result'], string> = {
  Success: '成功',
  Failure: '失败',
}
