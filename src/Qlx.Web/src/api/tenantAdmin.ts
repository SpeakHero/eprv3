/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：租户自管理域 API 封装（过渡壳，T7 删除）
 * 说明：
 *   1. 组织 / 岗位 / 成员 / 角色实现已全部迁入适配层 api/adapters/{orgAdmin,memberAdmin,roleAdmin}.ts，
 *      页面不再消费本文件（T4 起）；此处仅 re-export 适配器工厂保持过渡期兼容。
 *   2. mock 占位（resendMemberInvite / importTenantMembers / togglePositionStatus）已删除，
 *      真实实现见适配层（B-5 / B-6 / B-7 端点）。
 *   3. fetchTenantModules 暂保留本文件（TenantModuleView 仍消费，模块卡片域未纳入三模块重构范围）。
 */

import { apiRequest } from '@/api/adapters/request'
import type { TenantModuleCard } from '@/types/tenant'

export { createTenantOrgAdminAdapter } from './adapters/orgAdmin'
export { createTenantMemberAdminAdapter } from './adapters/memberAdmin'
export { createTenantRoleAdminAdapter, createPortalRoleAdminAdapter } from './adapters/roleAdmin'

interface BackendModuleCard {
  code: string
  name: string
  description: string
  icon?: string | null
  version?: string | null
  licensed: boolean
  licensedAt?: string | null
}

/** 已授权模块卡片（TenantModuleView 消费）。 */
export function fetchTenantModules(): Promise<TenantModuleCard[]> {
  // 后端 TenantModuleRow 不含 features 字段，version 可空——前端补默认值
  return apiRequest<BackendModuleCard[]>({
    method: 'GET',
    url: '/api/tenant/modules',
  }).then((items) =>
    items.map((m) => ({
      code: m.code,
      name: m.name,
      description: m.description,
      version: m.version ?? '',
      features: [],
      licensed: m.licensed,
      licensedAt: m.licensedAt ?? null,
    })),
  )
}
