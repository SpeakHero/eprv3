/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：菜单 API 封装——统一走 @/core/http 共享实例 + ApiResponse 解包（G-1）；
 *       /api/portal/menus（门户全量扁平）、/api/tenant/menus（按租户模块许可裁剪的扁平数据）。
 *       后端只下发扁平 JSON，树组装由 utils/menuTree.ts 承担（B-2）。
 */

import { apiRequest } from '@/api/adapters/request'
import type { NavGroupDTO, MenuFlatDTO } from '@/utils/menuTree'

/** 菜单扁平 API 响应。 */
export interface MenusFlatResponse {
  navGroups: NavGroupDTO[]
  menus: MenuFlatDTO[]
}

/**
 * 获取门户全量菜单扁平数据（门户管理端 / 身份级会话使用）。
 * @throws 网络错误 / 后端 success=false 时抛后端 message。
 */
export function fetchMenus(): Promise<MenusFlatResponse> {
  return apiRequest<MenusFlatResponse>({ method: 'GET', url: '/api/portal/menus' })
}

/**
 * 获取当前租户可见菜单扁平数据（租户上下文 Token，后端已按模块许可裁剪）。
 * @throws 网络错误 / 无租户上下文（401）/ 后端 success=false。
 */
export function fetchTenantMenus(): Promise<MenusFlatResponse> {
  return apiRequest<MenusFlatResponse>({ method: 'GET', url: '/api/tenant/menus' })
}
