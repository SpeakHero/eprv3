/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：role-admin-utils 单元测试——权限树构建 / 勾选码提取 / 计数 / 角色过滤
 */

import { describe, expect, it } from 'vitest'
import type { PermissionGroup, RoleItem } from '@/types/org-admin'
import {
  allPermissionCodes,
  buildPermissionTree,
  countBuiltinRoles,
  countPermissions,
  extractCheckedCodes,
  filterRoles,
} from '../role-admin-utils'

const groups: PermissionGroup[] = [
  {
    moduleCode: 'bas',
    moduleName: '基础数据',
    icon: 'Folder',
    permissions: [
      { code: 'bas.org.menu', name: '组织菜单', description: '' },
      { code: 'bas.org.action', name: '组织操作', description: '增删改' },
    ],
  },
  {
    moduleCode: 'wms',
    moduleName: '仓储',
    icon: '',
    permissions: [{ code: 'wms.stock.menu', name: '库存菜单', description: '' }],
  },
]

function role(partial: Partial<RoleItem>): RoleItem {
  return {
    id: 'r1',
    code: 'admin',
    name: '管理员',
    description: '',
    isBuiltin: false,
    dataScope: 'Self',
    memberCount: 0,
    permissionCount: 0,
    updatedAt: '2026-10-23',
    ...partial,
  }
}

describe('buildPermissionTree', () => {
  it('模块为父节点（module: 前缀），权限码为叶子', () => {
    const tree = buildPermissionTree(groups)
    expect(tree).toHaveLength(2)
    expect(tree[0].id).toBe('module:bas')
    expect(tree[0].label).toBe('基础数据')
    expect(tree[0].icon).toBe('Folder')
    expect(tree[0].children).toHaveLength(2)
    expect(tree[0].children![1]).toMatchObject({ id: 'bas.org.action', description: '增删改' })
  })

  it('空 icon / 空 description 归一为 undefined', () => {
    const tree = buildPermissionTree(groups)
    expect(tree[1].icon).toBeUndefined()
    expect(tree[1].children![0].description).toBeUndefined()
  })
})

describe('allPermissionCodes / countPermissions', () => {
  it('扁平展开全部权限码', () => {
    expect(allPermissionCodes(groups)).toEqual(['bas.org.menu', 'bas.org.action', 'wms.stock.menu'])
    expect(countPermissions(groups)).toBe(3)
  })

  it('空分组返回 0 / 空数组', () => {
    expect(allPermissionCodes([])).toEqual([])
    expect(countPermissions([])).toBe(0)
  })
})

describe('extractCheckedCodes', () => {
  it('剔除 module: 前缀的父节点 key', () => {
    expect(extractCheckedCodes(['module:bas', 'bas.org.menu', 42])).toEqual(['bas.org.menu', '42'])
  })

  it('纯模块勾选返回空', () => {
    expect(extractCheckedCodes(['module:bas', 'module:wms'])).toEqual([])
  })
})

describe('filterRoles / countBuiltinRoles', () => {
  const roles = [
    role({ id: 'r1', code: 'Admin', name: '企业管理员', isBuiltin: true }),
    role({ id: 'r2', code: 'buyer', name: '采购专员' }),
  ]

  it('按名称 / 编码大小写不敏感过滤', () => {
    expect(filterRoles(roles, '采购')).toHaveLength(1)
    expect(filterRoles(roles, 'admin')).toHaveLength(1)
    expect(filterRoles(roles, '')).toHaveLength(2)
    expect(filterRoles(roles, '不存在')).toHaveLength(0)
  })

  it('统计内置角色数量', () => {
    expect(countBuiltinRoles(roles)).toBe(1)
  })
})
