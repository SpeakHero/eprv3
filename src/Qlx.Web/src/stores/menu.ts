/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：菜单状态 Pinia store——消费 utils/menuTree.ts 纯函数组装前端菜单树
 */

import { defineStore } from 'pinia'
import type { NavGroupDTO, MenuFlatDTO, MenuGrouped } from '@/utils/menuTree'
import { buildTreeForTenant } from '@/utils/menuTree'
import { fetchMenus } from '@/api/menus'
import { useAuthStore } from '@/stores/auth'

export const useMenuStore = defineStore('menu', {
  state: () => ({
    /** 后端下发的导航分组（扁平 JSON）。 */
    rawNavGroups: [] as NavGroupDTO[],
    /** 后端下发的菜单条目（扁平 JSON）。 */
    rawMenus: [] as MenuFlatDTO[],
    /** 树组装后的导航分组 + 菜单树（utils/menuTree.ts 输出）。 */
    builtGroups: [] as MenuGrouped[],
    /** 是否已加载。 */
    loaded: false,
  }),

  getters: {
    /** 顶级菜单树（跨导航分组扁平化，Layout 侧边栏可直接消费）。 */
    topMenus: (state) =>
      state.builtGroups.flatMap((g) =>
        g.trees.map((t) => ({
          groupCode: g.groupCode,
          groupName: g.groupName,
          sortOrder: g.sortOrder,
          tree: t,
        })),
      ),
  },

  actions: {
    /**
     * 从后端拉取扁平菜单 + 调用 utils/menuTree.ts 组装树。
     * @param accessToken Bearer Token（身份级或租户级均可，后端不裁剪）。
     * @throws 网络错误 / 后端返回非 ApiResponse 格式错误。
     */
    async loadFromApi(accessToken: string): Promise<void> {
      const response = await fetchMenus(accessToken)
      this.rawNavGroups = response.navGroups
      this.rawMenus = response.menus
      this.rebuildTree()
      this.loaded = true
    },

    /** 基于当前 auth store 状态重新组装树（登录/进租户/切换角色后调用）。 */
    rebuildTree(): void {
      const auth = useAuthStore()
      this.builtGroups = buildTreeForTenant(
        this.rawNavGroups,
        this.rawMenus,
        auth.licensedModules,
        auth.permissions,
        auth.memberRole,
        auth.portalAdminMode,
      )
    },

    /** 登出/切换租户时清空。 */
    reset(): void {
      this.rawNavGroups = []
      this.rawMenus = []
      this.builtGroups = []
      this.loaded = false
    },
  },
})
