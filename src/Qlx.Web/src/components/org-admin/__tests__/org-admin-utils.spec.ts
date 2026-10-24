/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：org-admin-utils 单元测试——建树 / 查找 / 子树排除 / 层级选项缩进
 */

import { describe, expect, it } from 'vitest'
import type { OrgNode } from '@/types/org-admin'
import { buildOrgOptions, buildOrgTree, collectOrgIds, findOrgNode } from '../org-admin-utils'

function node(partial: Partial<OrgNode> & { id: string }): OrgNode {
  return {
    parentId: null,
    name: partial.id,
    type: 'Department',
    managerMemberId: null,
    status: 'Enabled',
    sortOrder: 0,
    isBuiltin: false,
    memberCount: 0,
    createdAt: '2026-10-23',
    ...partial,
  }
}

// 结构：corp（根，sort 2）
//        ├─ deptA（sort 2）
//        │   └─ teamA1
//        └─ deptB（sort 1）
// orphan（父缺失，兜底为根，sort 1）
const flat: OrgNode[] = [
  node({ id: 'teamA1', parentId: 'deptA', name: '一组', type: 'Team' }),
  node({ id: 'deptA', parentId: 'corp', name: '采购部', sortOrder: 2 }),
  node({ id: 'orphan', parentId: 'missing', name: '孤儿节点', sortOrder: 1 }),
  node({ id: 'corp', name: '总部', type: 'Company', sortOrder: 2 }),
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
    expect(findOrgNode(tree, 'corp')?.type).toBe('Company')
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
