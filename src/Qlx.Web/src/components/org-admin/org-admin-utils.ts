/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：组织组件族纯函数——扁平列表建树 / 节点查找 / 子树 Id 收集 / 层级选项缩进 +
 *       组织类型（D2 四级）与状态的中文映射 / 图标 / 色调（可单测，无框架依赖）
 * 说明：
 *   1. 树组装属展示加工，统一在前端公共层完成（B-2-1 / B-4）；
 *   2. 泛型化以兼容两类契约：旧租户组织（types/org-admin.ts 字符串枚举）与
 *      Bas 主数据组织（types/bas-master.ts 数值枚举，含编码 / 版本 / 有效期）；
 *   3. 中文映射字典唯一来源为 types/bas-master.ts，本文件只做取用与兜底。
 */

import type { BasOrgRow, BasOrgType, BasStatus } from '@/types/bas-master'
import {
  BAS_ORG_TYPE_ICONS,
  BAS_ORG_TYPE_LABELS,
  BAS_STATUS_LABELS,
} from '@/types/bas-master'
import type { OrgNode } from '@/types/org-admin'

/** 建树所需的最小字段集（组织扁平行均满足）。 */
export interface OrgTreeSource {
  id: string
  parentId: string | null
  name: string
  sortOrder: number
}

/** 组织树节点（原始扁平行 + children 嵌套）。 */
export type OrgTreeNode<T extends OrgTreeSource = OrgNode> = T & {
  children: OrgTreeNode<T>[]
}

/** Bas 组织树节点。 */
export type BasOrgTreeNode = OrgTreeNode<BasOrgRow>

/** 组织层级下拉选项（缩进表达层级）。 */
export interface OrgOption {
  label: string
  value: string
}

/** 展示色调（与 QlxDataTable 的 TagTone 同集合，避免纯函数层依赖组件类型）。 */
export type OrgTone = 'success' | 'warning' | 'danger' | 'info' | 'primary'

/**
 * 扁平组织列表 → 嵌套树。
 * 同级按 sortOrder 升序；父节点缺失的孤儿节点兜底挂为根（防御后端脏数据）。
 */
export function buildOrgTree<T extends OrgTreeSource>(flat: T[]): OrgTreeNode<T>[] {
  const byId = new Map<string, OrgTreeNode<T>>()
  for (const node of flat) {
    byId.set(node.id, { ...node, children: [] })
  }
  const roots: OrgTreeNode<T>[] = []
  for (const node of byId.values()) {
    const parent = node.parentId ? byId.get(node.parentId) : undefined
    if (parent) {
      parent.children.push(node)
    } else {
      roots.push(node)
    }
  }
  const bySortOrder = (a: OrgTreeNode<T>, b: OrgTreeNode<T>): number => a.sortOrder - b.sortOrder
  const sortRecursive = (nodes: OrgTreeNode<T>[]): void => {
    nodes.sort(bySortOrder)
    for (const node of nodes) {
      sortRecursive(node.children)
    }
  }
  sortRecursive(roots)
  return roots
}

/** 按 Id 深度优先查找树节点。 */
export function findOrgNode<T extends OrgTreeSource>(
  nodes: OrgTreeNode<T>[],
  id: string,
): OrgTreeNode<T> | null {
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
export function collectOrgIds(node: OrgTreeNode<OrgTreeSource>): Set<string> {
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
export function buildOrgOptions<T extends OrgTreeSource>(
  nodes: OrgTreeNode<T>[],
  exclude?: Set<string>,
): OrgOption[] {
  const options: OrgOption[] = []
  const walk = (list: OrgTreeNode<T>[], depth: number): void => {
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

/* ============ Bas 组织：四级类型（D2）与状态映射 ============ */

/** 组织类型中文（1=集团 / 2=子公司 / 3=部门 / 4=项目部）。 */
export function basOrgTypeLabel(type: number | null | undefined): string {
  return BAS_ORG_TYPE_LABELS[type as BasOrgType] ?? '—'
}

/** 组织类型图标名（AppIcon 图标集）。 */
export function basOrgTypeIcon(type: number | null | undefined): string {
  return BAS_ORG_TYPE_ICONS[type as BasOrgType] ?? 'OfficeBuilding'
}

/** 组织类型下拉选项（值一律数值，中文文案前端维护）。 */
export function basOrgTypeOptions(): Array<{ label: string; value: BasOrgType }> {
  const types: BasOrgType[] = [1, 2, 3, 4]
  return types.map((type) => ({ label: BAS_ORG_TYPE_LABELS[type], value: type }))
}

/** 组织启停状态中文。 */
export function basOrgStatusLabel(status: number | null | undefined): string {
  return BAS_STATUS_LABELS[status as BasStatus] ?? '—'
}

/** 组织启停状态色调（启用成功 / 停用灰）。 */
export function basOrgStatusTone(status: number | null | undefined): OrgTone {
  return status === 1 ? 'success' : 'info'
}

/** 是否停用（1=启用 / 2=停用）。 */
export function isBasOrgDisabled(status: number | null | undefined): boolean {
  return status === 2
}
