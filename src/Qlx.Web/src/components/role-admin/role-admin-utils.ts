/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：角色权限组件族纯函数——权限树构建 / 勾选码提取 / 计数 / 角色过滤（可单测，无框架依赖）
 * 说明：承接原 TenantRoleView.vue 内私有实现（permissionTreeData / readCheckedCodes / 全选计数），
 *       组件（RolePermissionPanel / RoleListPanel）只调用本文件，不再重复实现。
 */

import type { PermissionGroup, RoleItem } from '@/types/org-admin'

/** 权限树节点（el-tree 数据形态）。 */
export interface PermissionTreeNode {
  id: string
  label: string
  icon?: string
  description?: string
  children?: PermissionTreeNode[]
}

/** 模块节点 id 前缀（与权限码区分，提交时剔除）。 */
export const MODULE_NODE_PREFIX = 'module:'

/** 权限分组 → el-tree 树数据（模块为父节点，权限码为叶子节点）。 */
export function buildPermissionTree(groups: PermissionGroup[]): PermissionTreeNode[] {
  return groups.map((group) => ({
    id: `${MODULE_NODE_PREFIX}${group.moduleCode}`,
    label: group.moduleName,
    icon: group.icon || undefined,
    children: group.permissions.map((permission) => ({
      id: permission.code,
      label: permission.name,
      description: permission.description || undefined,
    })),
  }))
}

/** 全部权限码（叶子节点，扁平展开）。 */
export function allPermissionCodes(groups: PermissionGroup[]): string[] {
  return groups.flatMap((group) => group.permissions.map((permission) => permission.code))
}

/** 权限点总数（用于「已选 N / M 项」计数）。 */
export function countPermissions(groups: PermissionGroup[]): number {
  return groups.reduce((total, group) => total + group.permissions.length, 0)
}

/** 从 el-tree 勾选 key 中提取权限码（剔除 module: 前缀的父节点）。 */
export function extractCheckedCodes(keys: Array<string | number>): string[] {
  return keys.map(String).filter((key) => !key.startsWith(MODULE_NODE_PREFIX))
}

/** 角色按名称 / 编码模糊过滤（大小写不敏感）。 */
export function filterRoles(roles: RoleItem[], keyword: string): RoleItem[] {
  const text = keyword.trim().toLowerCase()
  if (!text) {
    return roles
  }
  return roles.filter(
    (role) => role.name.toLowerCase().includes(text) || role.code.toLowerCase().includes(text),
  )
}

/** 内置角色数量（列表头部「内置 N」徽标）。 */
export function countBuiltinRoles(roles: RoleItem[]): number {
  return roles.filter((role) => role.isBuiltin).length
}
