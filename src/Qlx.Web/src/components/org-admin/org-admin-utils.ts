/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：组织组件族纯函数——扁平列表建树 / 节点查找 / 子树 Id 收集 / 层级选项缩进（可单测，无框架依赖）
 * 说明：替代原 TenantOrganizationView.vue / TenantMemberView.vue 内私有 findOrgNode / collectOrgIds /
 *       orgSelectOptions walk 实现（B-4：树组装属展示加工，统一在前端公共层）。
 */

import type { OrgNode } from '@/types/org-admin'

/** 组织树节点（OrgNode + children 嵌套）。 */
export interface OrgTreeNode extends OrgNode {
  children: OrgTreeNode[]
}

/** 组织层级下拉选项（缩进表达层级）。 */
export interface OrgOption {
  label: string
  value: string
}

/**
 * 扁平组织列表 → 嵌套树。
 * 同级按 sortOrder 升序；父节点缺失的孤儿节点兜底挂为根（防御后端脏数据）。
 */
export function buildOrgTree(flat: OrgNode[]): OrgTreeNode[] {
  const byId = new Map<string, OrgTreeNode>()
  for (const node of flat) {
    byId.set(node.id, { ...node, children: [] })
  }
  const roots: OrgTreeNode[] = []
  for (const node of byId.values()) {
    const parent = node.parentId ? byId.get(node.parentId) : undefined
    if (parent) {
      parent.children.push(node)
    } else {
      roots.push(node)
    }
  }
  const bySortOrder = (a: OrgTreeNode, b: OrgTreeNode): number => a.sortOrder - b.sortOrder
  const sortRecursive = (nodes: OrgTreeNode[]): void => {
    nodes.sort(bySortOrder)
    for (const node of nodes) {
      sortRecursive(node.children)
    }
  }
  sortRecursive(roots)
  return roots
}

/** 按 Id 深度优先查找树节点。 */
export function findOrgNode(nodes: OrgTreeNode[], id: string): OrgTreeNode | null {
  for (const node of nodes) {
    if (node.id === id) {
      return node
    }
    const found = findOrgNode(node.children, id)
    if (found) {
      return found
    }
  }
  return null
}

/** 收集某节点及其全部下级节点 Id（编辑组织时上级选项排除自身与下级，避免环形层级）。 */
export function collectOrgIds(node: OrgTreeNode): Set<string> {
  const result = new Set<string>([node.id])
  for (const child of node.children) {
    for (const id of collectOrgIds(child)) {
      result.add(id)
    }
  }
  return result
}

/**
 * 树 → 层级下拉选项（深度缩进：「　」× depth + 「└ 」）。
 * exclude 中的 Id 及其文本不出现（配合 collectOrgIds 排除自身子树）。
 */
export function buildOrgOptions(nodes: OrgTreeNode[], exclude?: Set<string>): OrgOption[] {
  const options: OrgOption[] = []
  const walk = (list: OrgTreeNode[], depth: number): void => {
    for (const node of list) {
      if (!exclude?.has(node.id)) {
        options.push({
          label: `${depth ? `${'　'.repeat(depth)}└ ` : ''}${node.name}`,
          value: node.id,
        })
      }
      walk(node.children, depth + 1)
    }
  }
  walk(nodes, 0)
  return options
}
