/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：按钮级权限指令——零代码移除无权限元素；TenantAdmin / portalAdminMode 零计算旁路
 */

import type { Directive } from 'vue'
import { usePermission } from '@/composables/usePermission'

/** v-permission="'portal.role.edit'" — 字符串或数组；多码 OR 语义（任一命中保留）。 */
export const vPermission: Directive<HTMLElement, string | string[]> = {
  mounted(el, binding) {
    const { hasPermission } = usePermission()
    const codes = Array.isArray(binding.value) ? binding.value : [binding.value]
    const has = codes.some((c) => hasPermission(c))
    if (!has && el.parentNode) {
      el.parentNode.removeChild(el)
    }
  },
}
