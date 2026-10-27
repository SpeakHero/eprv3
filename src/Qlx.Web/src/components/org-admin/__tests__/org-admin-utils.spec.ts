/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：org-admin-utils 单元测试——建树 / 查找 / 子树排除 / 层级选项缩进 +
 *       Bas 组织四级类型（D2）与状态映射
 */

import { describe, expect, it } from 'vitest'
import type { BasOrgRow } from '@/types/bas-master'
import {
  basOrgStatusLabel,
  basOrgStatusTone,
  basOrgTypeIcon,
  basOrgTypeLabel,
  basOrgTypeOptions,
  buildOrgOptions,
  buildOrgTree,
  collectOrgIds,
  findOrgNode,
  isBasOrgDisabled,
} from '../org-admin-utils'

/** Bas 组织扁平行工厂（数值枚举：type 1~4 / status 1~2）。 */
function node(partial: Partial<BasOrgRow> & { id: string }): BasOrgRow {
  return {
    parentId: null,
    code: `ORG-${partial.id}`,
    name: partial.id,
    type: 3,
    leaderPersonId: null,
    effectDate: '2026-01-01',
    expiryDate: null,
    version: 1,
    status: 1,
    sortOrder: 0,
    isBuiltin: false,
    createdAt: '2026-10-23T00:00:00+08:00',
    updatedAt: '2026-10-23T00:00:00+08:00',
    ...partial,
  }
}

// 结构：corp（根，集团，sort 2）
//        ├─ deptA（部门，sort 2）
//        │   └─ teamA1（项目部）
//        └─ deptB（部门，sort 1）
// orphan（父缺失，兜底为根，子公司，sort 1）
const flat: BasOrgRow[] = [
  node({ id: 'teamA1', parentId: 'deptA', name: '一组', type: 4 }),
  node({ id: 'deptA', parentId: 'corp', name: '采购部', sortOrder: 2 }),
  node({ id: 'orphan', parentId: 'missing', name: '孤儿节点', type: 2, sortOrder: 1 }),
  node({ id: 'corp', name: '总部', type: 1, sortOrder: 2 }),
  node({ id: 'deptB', parentId: 'corp', name: '销售部', sortOrder: 1 }),
]

describe('buildOrgTree', () => {
  it('扁平列表 → 嵌套树，同级按 sortOrder 升序', () => {
    const tree = buildOrgTree(flat)
    // 根：orphan(sort 1) 在 corp(sort 2) 前
    expect(tree.map((n) => n.id)).toEqual(['orphan', 'corp'])
    const corp = tree[1]
    // corp 下级：deptB(sort 1) 在 deptA(sort 2) 前
    expect(corp.children.map((n) => n.id)).toEqual(['deptB', 'deptA'])
    expect(corp.children[1].children.map((n) => n.id)).toEqual(['teamA1'])
  })

  it('父缺失的孤儿节点兜底挂为根', () => {
    const tree = buildOrgTree(flat)
    expect(tree.some((n) => n.id === 'orphan')).toBe(true)
  })

  it('空列表返回空树', () => {
    expect(buildOrgTree([])).toEqual([])
  })
})

describe('findOrgNode', () => {
  it('命中深层节点 / 未命中返回 null', () => {
    const tree = buildOrgTree(flat)
    expect(findOrgNode(tree, 'teamA1')?.name).toBe('一组')
    // 四级类型以数值对接：1=集团 / 2=子公司 / 3=部门 / 4=项目部
    expect(findOrgNode(tree, 'corp')?.type).toBe(1)
    expect(findOrgNode(tree, 'orphan')?.type).toBe(2)
    expect(findOrgNode(tree, 'nope')).toBeNull()
  })
})

describe('collectOrgIds', () => {
  it('包含自身与全部下级（用于编辑时排除子树）', () => {
    const tree = buildOrgTree(flat)
    const deptA = findOrgNode(tree, 'deptA')!
    expect([...collectOrgIds(deptA)].sort()).toEqual(['deptA', 'teamA1'])
    const corp = findOrgNode(tree, 'corp')!
    expect([...collectOrgIds(corp)].sort()).toEqual(['corp', 'deptA', 'deptB', 'teamA1'])
  })
})

describe('buildOrgOptions', () => {
  it('按层级缩进（　× depth + └ ）', () => {
    const tree = buildOrgTree(flat)
    const options = buildOrgOptions(tree)
    const corpIdx = options.findIndex((o) => o.value === 'corp')
    expect(options[corpIdx].label).toBe('总部')
    expect(options[corpIdx + 1].label).toBe('　└ 销售部')
    expect(options[corpIdx + 3].label).toBe('　　└ 一组')
  })

  it('exclude 排除子树集合（编辑时自身及下级均不可选为上级）', () => {
    const tree = buildOrgTree(flat)
    const deptA = findOrgNode(tree, 'deptA')!
    const options = buildOrgOptions(tree, collectOrgIds(deptA))
    expect(options.some((o) => o.value === 'deptA')).toBe(false)
    expect(options.some((o) => o.value === 'teamA1')).toBe(false)
    expect(options.some((o) => o.value === 'deptB')).toBe(true)
  })
})

describe('Bas 组织四级类型与状态映射', () => {
  it('四个类型依次映射为集团 / 子公司 / 部门 / 项目部', () => {
    expect([1, 2, 3, 4].map((type) => basOrgTypeLabel(type))).toEqual([
      '集团',
      '子公司',
      '部门',
      '项目部',
    ])
  })

  it('未知类型兜底为占位符', () => {
    expect(basOrgTypeLabel(9)).toBe('—')
    expect(basOrgTypeLabel(null)).toBe('—')
  })

  it('类型下拉选项以数值作为 value', () => {
    expect(basOrgTypeOptions()).toEqual([
      { label: '集团', value: 1 },
      { label: '子公司', value: 2 },
      { label: '部门', value: 3 },
      { label: '项目部', value: 4 },
    ])
  })

  it('类型图标取自既有图标集', () => {
    expect(basOrgTypeIcon(1)).toBe('OfficeBuilding')
    expect(basOrgTypeIcon(3)).toBe('House')
    expect(basOrgTypeIcon(99)).toBe('OfficeBuilding')
  })

  it('状态文案 / 色调 / 停用判定', () => {
    expect(basOrgStatusLabel(1)).toBe('启用')
    expect(basOrgStatusLabel(2)).toBe('停用')
    expect(basOrgStatusTone(1)).toBe('success')
    expect(basOrgStatusTone(2)).toBe('info')
    expect(isBasOrgDisabled(2)).toBe(true)
    expect(isBasOrgDisabled(1)).toBe(false)
  })
})
