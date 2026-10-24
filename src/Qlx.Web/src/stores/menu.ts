/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：菜单状态 Pinia store——消费 utils/menuTree.ts 纯函数组装前端菜单树；
 *       数据来源：租户上下文 GET /api/tenant/menus（按许可裁剪）、门户 GET /api/portal/menus
 */

import { defineStore } from 'pinia'
import type { NavGroupDTO, MenuFlatDTO, MenuGrouped } from '@/utils/menuTree'
import { buildTreeForTenant } from '@/utils/menuTree'
import { fetchMenus, fetchTenantMenus, type MenusFlatResponse } from '@/api/menus'
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
    /** 是否正在加载。 */
    loading: false,
    /** 最近一次加载失败原因（空态提示用）。 */
    loadError: '',
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
    /** 是否无任何可见菜单（空态提示）。 */
    isEmpty: (state) => state.loaded && state.builtGroups.length === 0,
  },

  actions: {
    /**
     * 拉取租户上下文菜单（/api/tenant/menus）并组装树。
     * 失败不抛出：记录 loadError，由布局回退静态导航，避免阻断租户端进入。
     */
    async loadForTenant(): Promise<void> {
      if (this.loading) return
      this.loading = true
      this.loadError = ''
      try {
        this.apply(await fetchTenantMenus())
      } catch (error) {
        this.loadError = error instanceof Error ? error.message : '菜单加载失败'
      } finally {
        this.loading = false
      }
    },

    /**
     * 拉取门户全量菜单（/api/portal/menus）并组装树。
     * @throws 网络错误 / 后端 success=false（调用方决定是否回退）。
     */
    async loadPortal(): Promise<void> {
      this.loading = true
      this.loadError = ''
      try {
        this.apply(await fetchMenus())
      } catch (error) {
        this.loadError = error instanceof Error ? error.message : '菜单加载失败'
        throw error
      } finally {
        this.loading = false
      }
    },

    /** 写入原始扁平数据并重组树。 */
    apply(response: MenusFlatResponse): void {
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
      this.loading = false
      this.loadError = ''
    },
  },
})
