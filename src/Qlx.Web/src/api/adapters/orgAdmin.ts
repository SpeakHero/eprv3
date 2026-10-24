/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：组织/岗位域适配器——OrgAdminAdapter 接口 + tenant 实现（门户无组织域，不实现）
 * 说明：
 *   1. 契约见 types/org-admin.ts；组织为扁平列表（树组装在组件族纯函数，B-2/B-4）。
 *   2. 端点：/api/tenant/organizations/* 与 /api/tenant/positions/*（含 B-5 岗位启停）。
 */

import type { PagedQuery, PagedResult } from '@/types/admin'
import type {
  EntityStatus,
  OrgNode,
  OrgNodeType,
  PositionItem,
  SaveOrgInput,
  SavePositionInput,
} from '@/types/org-admin'
import { apiRequest } from './request'

// ================ 适配器契约 ================

/** 组织/岗位管理适配器（仅租户场景）。 */
export interface OrgAdminAdapter {
  /** 组织扁平列表（含 memberCount 直属成员计数）。 */
  listOrgNodes(): Promise<OrgNode[]>
  /** 新建（id=null）/ 更新组织。 */
  saveOrg(id: string | null, input: SaveOrgInput): Promise<OrgNode>
  /** 组织启停（根公司 409 ORG_ROOT_PROTECTED）。 */
  toggleOrg(id: string): Promise<OrgNode>
  /** 岗位分页。 */
  fetchPositions(query: PagedQuery & { orgId?: string }): Promise<PagedResult<PositionItem>>
  /** 岗位新建/保存（编码唯一 409 POSITION_CODE_EXISTS）。 */
  savePosition(input: SavePositionInput): Promise<PositionItem>
  /** 岗位启停（Enabled↔Disabled，有在职成员不阻断）。 */
  togglePosition(id: string): Promise<PositionItem>
}

// ================ 后端 DTO（扁平原始形态） ================

interface OrgFlatRow {
  id: string
  parentId: string | null
  name: string
  type: OrgNodeType
  managerMemberId: string | null
  status: EntityStatus
  sortOrder: number
  isBuiltin: boolean
  createdAt: string
  memberCount: number
}

interface PositionRow {
  id: string
  orgId: string
  name: string
  code: string
  headcount: number
  currentCount: number
  status: EntityStatus
  description: string | null
  updatedAt: string
}

// ================ 映射 ================

function mapOrgNode(f: OrgFlatRow): OrgNode {
  // 后端 DefaultIgnoreCondition 可能省略 null 属性，归一化为 null
  return {
    id: f.id,
    parentId: f.parentId ?? null,
    name: f.name,
    type: f.type,
    managerMemberId: f.managerMemberId ?? null,
    status: f.status,
    sortOrder: f.sortOrder,
    isBuiltin: f.isBuiltin,
    memberCount: f.memberCount,
    createdAt: f.createdAt,
  }
}

function mapPosition(p: PositionRow): PositionItem {
  return {
    id: p.id,
    orgId: p.orgId,
    name: p.name,
    code: p.code,
    headcount: p.headcount,
    currentCount: p.currentCount,
    status: p.status,
    description: p.description ?? '',
    updatedAt: p.updatedAt,
  }
}

// ================ tenant 实现 ================

/** 租户场景组织/岗位管理适配器。 */
export function createTenantOrgAdminAdapter(): OrgAdminAdapter {
  return {
    listOrgNodes() {
      return apiRequest<OrgFlatRow[]>({ method: 'GET', url: '/api/tenant/organizations/tree' })
        .then((rows) => rows.map(mapOrgNode))
    },

    saveOrg(id, input) {
      const config = id === null
        ? { method: 'POST' as const, url: '/api/tenant/organizations', data: input }
        : { method: 'PUT' as const, url: `/api/tenant/organizations/${id}`, data: input }
      return apiRequest<OrgFlatRow>(config).then(mapOrgNode)
    },

    toggleOrg(id) {
      return apiRequest<OrgFlatRow>({ method: 'POST', url: `/api/tenant/organizations/${id}/toggle` })
        .then(mapOrgNode)
    },

    fetchPositions(query) {
      const params: Record<string, string | number> = { page: query.page, pageSize: query.pageSize }
      if (query.keyword?.trim()) params.keyword = query.keyword.trim()
      if (query.orgId) params.orgId = query.orgId
      return apiRequest<PagedResult<PositionRow>>({ method: 'GET', url: '/api/tenant/positions', params })
        .then((res) => ({ ...res, items: res.items.map(mapPosition) }))
    },

    savePosition(input) {
      return apiRequest<PositionRow>({ method: 'POST', url: '/api/tenant/positions', data: input })
        .then(mapPosition)
    },

    togglePosition(id) {
      return apiRequest<PositionRow>({ method: 'POST', url: `/api/tenant/positions/${id}/toggle` })
        .then(mapPosition)
    },
  }
}
