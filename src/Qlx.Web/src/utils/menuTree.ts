/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：菜单树组装纯函数——零副作用、独立可单测；承接 B-2 职责边界（前端树组装 / 后端扁平 JSON）
 */

// ========== 扁平 DTO（后端 /api/portal/menus 直接映射） ==========

/** 后端下发的导航分组（扁平 JSON，后端不组装）。 */
export interface NavGroupDTO {
  groupCode: string
  groupName: string
  /** 逗号分隔的 moduleCode 列表，如 "portal,bas"。 */
  moduleCodes: string
  sortOrder: number
}

/** 后端下发的菜单条目（扁平 JSON）。 */
export interface MenuFlatDTO {
  moduleCode: string
  menuCode: string
  /** null 表示顶级菜单；否则为父菜单 menuCode。 */
  parentCode: string | null
  groupName: string
  displayName: string
  route: string
  icon: string
  /** null 表示仅模块许可即可见；否则需权限码精确匹配。 */
  requiredPermissionCode: string | null
  sortOrder: number
  openMode: number
}

// ========== 树组装输出 ==========

/** 树组装后的菜单节点（前端消费）。 */
export interface MenuTreeNode {
  menuCode: string
  moduleCode: string
  parentCode: string | null
  groupName: string
  displayName: string
  route: string
  icon: string
  requiredPermissionCode: string | null
  sortOrder: number
  openMode: number
  children: MenuTreeNode[]
}

/** 导航分组 + 其下菜单树。 */
export interface MenuGrouped {
  groupCode: string
  groupName: string
  sortOrder: number
  trees: MenuTreeNode[]
}

// ========== 核心纯函数 ==========

/**
 * 完整树组装 + 导航分组聚合（B-2 前端职责边界）。
 * @param navGroups 后端下发的导航分组（逗号分隔 moduleCodes）。
 * @param menus 后端下发的扁平菜单列表。
 * @param licensedModules 当前会话已许可的模块码。
 * @param permissions 当前会话的权限码列表。
 * @param memberRole 租户角色 TenantAdmin / OrdinaryMember（TenantAdmin 零计算旁路）。
 * @param portalAdminMode 是否门户管理模式（全权限旁路）。
 */
export function buildTreeForTenant(
  navGroups: NavGroupDTO[],
  menus: MenuFlatDTO[],
  licensedModules: string[],
  permissions: string[],
  memberRole: string,
  portalAdminMode: boolean,
): MenuGrouped[] {
  const bypass = portalAdminMode || memberRole === 'TenantAdmin'

  // 1. 模块许可裁剪
  const moduleLicensed = new Set(licensedModules)
  const filteredByModule = menus.filter((m) => moduleLicensed.has(m.moduleCode))

  // 2. 权限裁剪（TenantAdmin / portalAdminMode 旁路跳过）
  const finalMenus = bypass
    ? filteredByModule
    : filterByPermission(filteredByModule, permissions)

  // 3. 按 parentCode 组装树
  const treeMap = groupMenusIntoTree(finalMenus)

  // 4. 导航分组聚合（按 moduleCodes 匹配）
  return buildNavGroups(navGroups, treeMap)
}

/** 权限裁剪：菜单 requiredPermissionCode 为 null → 模块许可即可见；非空 → permissions 必须包含。 */
function filterByPermission(menus: MenuFlatDTO[], permissions: string[]): MenuFlatDTO[] {
  const permSet = new Set(permissions)
  return menus.filter((m) => !m.requiredPermissionCode || permSet.has(m.requiredPermissionCode))
}

/** 按 parentCode 组装树；返回 Map<parentCode | '__root__', MenuTreeNode[]>。 */
function groupMenusIntoTree(menus: MenuFlatDTO[]): Map<string | '__root__', MenuTreeNode[]> {
  // MenuFlatDTO.sort() 按 sortOrder 排序后再组装
  const sorted = [...menus].sort((a, b) => a.sortOrder - b.sortOrder)
  const nodes = sorted.map(toTreeNode)

  // 按 menuCode 建索引
  const byCode = new Map<string, MenuTreeNode>()
  for (const n of nodes) {
    byCode.set(n.menuCode, n)
  }

  // 挂载到根节点或父节点
  const roots: MenuTreeNode[] = []
  for (const n of nodes) {
    if (n.parentCode && byCode.has(n.parentCode)) {
      byCode.get(n.parentCode)!.children.push(n)
    } else {
      roots.push(n)
    }
  }

  // 清理被挂载的子节点不重复出现在 roots（上面过滤已天然实现：parentCode 有对应父节点时走 else 分支）
  return new Map([['__root__', roots]])
}

function toTreeNode(flat: MenuFlatDTO): MenuTreeNode {
  return {
    menuCode: flat.menuCode,
    moduleCode: flat.moduleCode,
    parentCode: flat.parentCode,
    groupName: flat.groupName,
    displayName: flat.displayName,
    route: flat.route,
    icon: flat.icon,
    requiredPermissionCode: flat.requiredPermissionCode,
    sortOrder: flat.sortOrder,
    openMode: flat.openMode,
    children: [],
  }
}

/** 导航分组聚合：按 moduleCodes.split(',') 匹配菜单 moduleCode。 */
function buildNavGroups(
  navGroups: NavGroupDTO[],
  treeMap: Map<string | '__root__', MenuTreeNode[]>,
): MenuGrouped[] {
  const roots = treeMap.get('__root__') ?? []
  // 按 moduleCode 建索引
  const byModule = new Map<string, MenuTreeNode[]>()
  for (const root of roots) {
    const bucket = byModule.get(root.moduleCode) ?? []
    bucket.push(root)
    byModule.set(root.moduleCode, bucket)
  }

  const result = [...navGroups]
    .map((g) => {
      const groupModules = g.moduleCodes
        .split(',')
        .map((s) => s.trim())
        .filter((s) => s.length > 0)

      const trees = groupModules
        .flatMap((m) => byModule.get(m) ?? [])
        .sort((a, b) => a.sortOrder - b.sortOrder)

      return {
        groupCode: g.groupCode,
        groupName: g.groupName,
        sortOrder: g.sortOrder,
        trees,
      }
    })
    .filter((g) => g.trees.length > 0)
    .sort((a, b) => a.sortOrder - b.sortOrder)

  return result
}
