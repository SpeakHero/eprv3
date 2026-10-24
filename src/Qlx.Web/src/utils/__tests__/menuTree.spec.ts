/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：utils/menuTree.ts 单元测试——覆盖模块许可裁剪 / 权限裁剪 / TenantAdmin 旁路 / 树组装 / 导航分组聚合 / 空态
 */

import { describe, expect, it } from 'vitest'
import { buildTreeForTenant, type MenuFlatDTO, type NavGroupDTO } from '../menuTree'

const sampleMenus: MenuFlatDTO[] = [
  { moduleCode: 'bas', menuCode: 'supplier.root', parentCode: null, groupName: '往来单位', displayName: '供应商', route: '/bas/supplier', icon: 'Shop', requiredPermissionCode: null, sortOrder: 10, openMode: 1 },
  { moduleCode: 'bas', menuCode: 'supplier.list', parentCode: 'supplier.root', groupName: '往来单位', displayName: '供应商列表', route: '/bas/supplier/list', icon: '', requiredPermissionCode: 'bas.supplier.view', sortOrder: 11, openMode: 1 },
  { moduleCode: 'bas', menuCode: 'supplier.edit', parentCode: 'supplier.root', groupName: '往来单位', displayName: '供应商编辑', route: '/bas/supplier/edit', icon: '', requiredPermissionCode: 'bas.supplier.edit', sortOrder: 12, openMode: 1 },
  { moduleCode: 'stk', menuCode: 'stock.root', parentCode: null, groupName: '库存', displayName: '库存', route: '/stk', icon: 'Box', requiredPermissionCode: null, sortOrder: 20, openMode: 1 },
]

const sampleNavGroups: NavGroupDTO[] = [
  { groupCode: 'workbench', groupName: '工作台', moduleCodes: 'bas,stk', sortOrder: 0 },
  { groupCode: 'finance', groupName: '财务', moduleCodes: 'fin,ctr', sortOrder: 1 },
]

describe('buildTreeForTenant', () => {
  it('TenantAdmin 零计算旁路：跳过权限裁剪，但仍然裁剪模块许可', () => {
    const result = buildTreeForTenant(
      sampleNavGroups,
      sampleMenus,
      ['bas', 'stk'], // 两个模块都许可
      [], // TenantAdmin 不写 permissions
      'TenantAdmin',
      false,
    )
    // bas/stk 都在 workbench groupCode 里
    expect(result).toHaveLength(1)
    expect(result[0].groupCode).toBe('workbench')
    // supplier.root → supplier.list + supplier.edit 都应该出现（TenantAdmin 旁路）
    const workbench = result[0]
    expect(workbench.trees).toHaveLength(2) // supplier.root + stock.root
    const supplierRoot = workbench.trees.find((t) => t.menuCode === 'supplier.root')!
    expect(supplierRoot.children).toHaveLength(2)
    expect(supplierRoot.children[0].menuCode).toBe('supplier.list')
  })

  it('普通成员：权限精确匹配——无 bas.supplier.edit → supplier.edit 子菜单被裁剪', () => {
    const result = buildTreeForTenant(
      sampleNavGroups,
      sampleMenus,
      ['bas', 'stk'],
      ['bas.supplier.view'], // 有 view 无 edit
      'OrdinaryMember',
      false,
    )
    const supplierRoot = result[0].trees.find((t) => t.menuCode === 'supplier.root')!
    expect(supplierRoot.children).toHaveLength(1)
    expect(supplierRoot.children[0].menuCode).toBe('supplier.list')
    // stock.root 无 requiredPermissionCode → 模块许可即可见
    const stockRoot = result[0].trees.find((t) => t.menuCode === 'stock.root')!
    expect(stockRoot.children).toHaveLength(0)
  })

  it('portalAdminMode：同 TenantAdmin 旁路', () => {
    const result = buildTreeForTenant(
      sampleNavGroups,
      sampleMenus,
      ['bas', 'stk'],
      [],
      'OrdinaryMember',
      true,
    )
    const supplierRoot = result[0].trees.find((t) => t.menuCode === 'supplier.root')!
    expect(supplierRoot.children).toHaveLength(2) // list + edit 都可见
  })

  it('模块许可裁剪：licensedModules 不含 stk → 完全不出现', () => {
    const result = buildTreeForTenant(
      sampleNavGroups,
      sampleMenus,
      ['bas'], // 仅许可 bas
      [],
      'TenantAdmin',
      false,
    )
    const workbench = result[0]
    expect(workbench.trees).toHaveLength(1) // 只有 supplier.root
  })

  it('导航分组聚合：groupCode=finance 匹配的是 fin,ctr → 无菜单 → 被过滤掉', () => {
    const result = buildTreeForTenant(
      sampleNavGroups,
      sampleMenus,
      ['bas', 'stk'],
      [],
      'TenantAdmin',
      false,
    )
    // finance groupCode 因为没有匹配模块的菜单被过滤掉
    expect(result).toHaveLength(1)
  })

  it('空态：menus 空 → 返回空数组', () => {
    const result = buildTreeForTenant(sampleNavGroups, [], ['bas'], [], 'TenantAdmin', false)
    expect(result).toHaveLength(0)
  })

  it('空态：navGroups 空 → 返回空数组', () => {
    const result = buildTreeForTenant([], sampleMenus, ['bas'], [], 'TenantAdmin', false)
    expect(result).toHaveLength(0)
  })

  it('空态：licensedModules 空 → 全部被裁剪 → 返回空数组', () => {
    const result = buildTreeForTenant(sampleNavGroups, sampleMenus, [], [], 'TenantAdmin', false)
    expect(result).toHaveLength(0)
  })
})
