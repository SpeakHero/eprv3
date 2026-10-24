/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：成员域适配器——MemberAdminAdapter 接口 + tenant 实现
 * 说明：
 *   1. 契约见 types/org-admin.ts；端点 /api/tenant/members/*（含 B-6 重发邀请 / B-7 批量导入 / B-8 登录日志）。
 *   2. 原 tenantAdmin.ts 中的 resendMemberInvite / importTenantMembers mock 占位由本文件真实实现替代。
 */

import type { PagedQuery, PagedResult } from '@/types/admin'
import type {
  EntityStatus,
  ImportResult,
  ImportRow,
  InviteMemberInput,
  InviteMemberResult,
  MemberItem,
  MemberLoginLogItem,
  TenantMemberRole,
} from '@/types/org-admin'
import { apiRequest } from './request'

// ================ 适配器契约 ================

/** 成员分页查询（租户场景筛选项）。 */
export interface MemberQuery extends PagedQuery {
  orgId?: string
  memberRole?: TenantMemberRole
  memberStatus?: EntityStatus
}

/** 成员管理适配器（仅租户场景）。 */
export interface MemberAdminAdapter {
  /** 成员分页。 */
  fetchMembers(query: MemberQuery): Promise<PagedResult<MemberItem>>
  /** 成员详情（越租户 404）。 */
  fetchMemberDetail(id: string): Promise<MemberItem>
  /** 邀请成员（已是成员 409 MEMBER_ALREADY_EXISTS）。 */
  inviteMember(input: InviteMemberInput): Promise<InviteMemberResult | null>
  /** 成员启停。 */
  toggleMember(id: string): Promise<void>
  /** 重置成员密码。 */
  resetMemberPassword(id: string, newPassword: string): Promise<void>
  /** 移除成员（管理员保护 409）。 */
  removeMember(id: string): Promise<void>
  /** 重发邀请（仅 PendingInvite，否则 409 MEMBER_NOT_PENDING_INVITE）。 */
  resendInvite(id: string): Promise<void>
  /** 批量导入（行级隔离，逐行独立提交）。 */
  importMembers(rows: ImportRow[]): Promise<ImportResult>
  /** 成员登录日志分页（页大小上限 20，超出由适配层收敛）。 */
  fetchLoginLogs(id: string, page: number, pageSize?: number): Promise<PagedResult<MemberLoginLogItem>>
}

// ================ 后端 DTO（扁平原始形态） ================

interface MemberRowRaw {
  tenantMemberId: string
  portalUserId: string
  loginAccount: string
  displayName: string
  phone: string | null
  email: string | null
  orgId: string | null
  roleCodes: string[]
  memberRole: number
  memberStatus: number
  joinAt: string | null
  lastLoginAt: string | null
}

interface MemberLoginLogRowRaw {
  eventId: string | number
  loginAt: string
  ipAddress: string | null
  isSuccess: boolean
  failureReason: string | null
}

// ================ 枚举映射（后端 int ↔ 前端联合类型） ================

const MEMBER_ROLE_FROM_INT: Record<number, TenantMemberRole> = {
  1: 'TenantAdmin',
  2: 'OrdinaryMember',
  3: 'PendingInvite',
}

const MEMBER_ROLE_TO_INT: Record<TenantMemberRole, number> = {
  TenantAdmin: 1,
  OrdinaryMember: 2,
  PendingInvite: 3,
}

const MEMBER_STATUS_FROM_INT: Record<number, EntityStatus> = {
  1: 'Enabled',
  2: 'Disabled',
}

function mapMember(m: MemberRowRaw): MemberItem {
  return {
    id: m.tenantMemberId,
    portalUserId: m.portalUserId,
    loginAccount: m.loginAccount,
    displayName: m.displayName,
    phone: m.phone ?? '',
    email: m.email ?? '',
    orgId: m.orgId ?? null,
    roleCodes: m.roleCodes,
    memberRole: MEMBER_ROLE_FROM_INT[m.memberRole] ?? 'OrdinaryMember',
    memberStatus: MEMBER_STATUS_FROM_INT[m.memberStatus] ?? 'Enabled',
    joinAt: m.joinAt ?? null,
    lastLoginAt: m.lastLoginAt ?? null,
  }
}

function mapLoginLog(l: MemberLoginLogRowRaw): MemberLoginLogItem {
  return {
    // EventId 为雪花 long，序列化形态（string/number）按后端 JSON 约定兜底兼容
    id: String(l.eventId),
    at: l.loginAt,
    ipAddress: l.ipAddress ?? '',
    result: l.isSuccess ? 'Success' : 'Failure',
    failureReason: l.failureReason ?? null,
  }
}

// ================ tenant 实现 ================

/** 租户场景成员管理适配器。 */
export function createTenantMemberAdminAdapter(): MemberAdminAdapter {
  const base = '/api/tenant/members'

  return {
    fetchMembers(query) {
      const params: Record<string, string | number> = { page: query.page, pageSize: query.pageSize }
      if (query.keyword?.trim()) params.keyword = query.keyword.trim()
      if (query.orgId) params.orgId = query.orgId
      if (query.memberRole) params.memberRole = MEMBER_ROLE_TO_INT[query.memberRole]
      if (query.memberStatus === 'Enabled') params.memberStatus = 1
      if (query.memberStatus === 'Disabled') params.memberStatus = 2
      return apiRequest<PagedResult<MemberRowRaw>>({ method: 'GET', url: base, params })
        .then((res) => ({ ...res, items: res.items.map(mapMember) }))
    },

    fetchMemberDetail(id) {
      return apiRequest<MemberRowRaw>({ method: 'GET', url: `${base}/${id}` }).then(mapMember)
    },

    inviteMember(input) {
      return apiRequest<InviteMemberResult | null>({ method: 'POST', url: `${base}/invite`, data: input })
    },

    toggleMember(id) {
      return apiRequest<void>({ method: 'POST', url: `${base}/${id}/toggle` })
    },

    resetMemberPassword(id, newPassword) {
      return apiRequest<void>({ method: 'POST', url: `${base}/${id}/reset`, data: { newPassword } })
    },

    removeMember(id) {
      return apiRequest<void>({ method: 'DELETE', url: `${base}/${id}` })
    },

    resendInvite(id) {
      return apiRequest<void>({ method: 'POST', url: `${base}/${id}/resend-invite` })
    },

    importMembers(rows) {
      // 入参为顶层行数组（后端 [FromBody] IReadOnlyList<ImportMemberRow>）
      return apiRequest<ImportResult>({ method: 'POST', url: `${base}/import`, data: rows })
        .then((res) => ({
          invited: res.invited,
          failed: (res.failed ?? []).map((f) => ({ line: f.line, reason: f.reason })),
        }))
    },

    fetchLoginLogs(id, page, pageSize = 20) {
      // 后端强制页大小上限 20，适配层先行收敛
      const safePageSize = Math.min(Math.max(pageSize, 1), 20)
      return apiRequest<PagedResult<MemberLoginLogRowRaw>>({
        method: 'GET',
        url: `${base}/${id}/login-logs`,
        params: { page, pageSize: safePageSize },
      }).then((res) => ({ ...res, items: res.items.map(mapLoginLog) }))
    },
  }
}
