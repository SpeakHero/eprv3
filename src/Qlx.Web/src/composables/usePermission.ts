/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：权限判定 composable——封装 auth store 的三重条件（TenantAdmin / portalAdminMode / permissions 精确匹配）
 */

import { useAuthStore } from '@/stores/auth'

/** 权限判定状态片段：封装"页面里到处写 hasPermission()"的重复。 */
export function usePermission() {
  const auth = useAuthStore()

  /**  TenantAdmin / portalAdminMode 零计算旁路；否则 permissions 精确匹配。 */
  const hasPermission = (code: string): boolean => {
    if (auth.portalAdminMode || auth.isTenantAdmin) {
      return true
    }
    return auth.permissions.includes(code)
  }

  /** 多码 OR 语义（任一命中即有权限）。 */
  const hasAnyPermission = (...codes: string[]): boolean => codes.some(hasPermission)

  return {
    hasPermission,
    hasAnyPermission,
    isTenantAdmin: () => auth.isTenantAdmin,
    portalAdminMode: () => auth.portalAdminMode,
  }
}
