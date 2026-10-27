/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：路由表与访问守卫——公开 / 需身份 / 需租户上下文三级
 */

import { createRouter, createWebHistory } from 'vue-router'
import { resolveRouteGuard } from '@/router/guard'
import { useAuthStore } from '@/stores/auth'

declare module 'vue-router' {
  interface RouteMeta {
    /** 公开页面（登录页）。 */
    publicPage?: boolean
    /** 仅需身份级会话（企业选择页）。 */
    requiresIdentity?: boolean
    /** 需租户上下文 Token（工作台及业务页面）。 */
    requiresTenant?: boolean
    /** 需真实租户上下文（门户管理模式不可进入租户自管理域）。 */
    requiresTenantData?: boolean
    /** 仅门户管理员可访问（门户管理端）。 */
    requiresPortalAdmin?: boolean
    /** 页面级菜单权限点（后端菜单下发同源，供前端按钮级裁剪与路由自查）。 */
    requiredPermissionCode?: string
    title?: string
  }
}

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/workbench' },
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/LoginView.vue'),
      meta: { publicPage: true, title: '用户登录' },
    },
    {
      path: '/select-tenant',
      name: 'select-tenant',
      component: () => import('@/views/TenantSelectView.vue'),
      meta: { requiresIdentity: true, title: '选择企业' },
    },
    {
      path: '/workbench',
      name: 'workbench',
      component: () => import('@/views/WorkbenchView.vue'),
      meta: { requiresTenant: true, title: '工作台' },
    },
    {
      // 阶段二公共组件演示页（mock 驱动，免登录直达；验收用）
      path: '/dev/components',
      name: 'dev-components',
      component: () => import('@/views/dev/ComponentDemoView.vue'),
      meta: { title: '公共组件演示' },
    },
    {
      // 标准示例页面体系（mock 驱动，免登录直达；开发参考）
      // 挂载机制：静态挂载（本路由表直接注册）+ 菜单点击挂载（ExamplesLayout 侧边导航 → RouterView）
      path: '/dev/examples',
      component: () => import('@/layouts/ExamplesLayout.vue'),
      meta: { title: '标准示例' },
      children: [
        {
          path: '',
          name: 'dev-examples',
          component: () => import('@/views/examples/ExamplesHomeView.vue'),
          meta: { title: '示例总览' },
        },
        {
          path: 'crud-list',
          name: 'dev-examples-crud-list',
          component: () => import('@/views/examples/CrudListExampleView.vue'),
          meta: { title: '标准列表页示例' },
        },
        {
          path: 'form',
          name: 'dev-examples-form',
          component: () => import('@/views/examples/FormExampleView.vue'),
          meta: { title: '表单与弹窗示例' },
        },
        {
          path: 'tree-detail',
          name: 'dev-examples-tree-detail',
          component: () => import('@/views/examples/TreeDetailExampleView.vue'),
          meta: { title: '左树右详情示例' },
        },
        {
          path: 'tabs',
          name: 'dev-examples-tabs',
          component: () => import('@/views/examples/TabsExampleView.vue'),
          meta: { title: '多标签页示例' },
        },
      ],
    },
    {
      path: '/module/:moduleCode/:itemCode',
      name: 'module-item',
      component: () => import('@/views/ModulePlaceholderView.vue'),
      meta: { requiresTenant: true, title: '业务功能' },
    },
    {
      path: '/manage',
      component: () => import('@/layouts/TenantAdminLayout.vue'),
      meta: { requiresTenantData: true, title: '企业管理' },
      children: [
        { path: '', redirect: '/manage/organizations' },
        {
          path: 'organizations',
          name: 'manage-organizations',
          component: () => import('@/views/tenant/TenantOrganizationView.vue'),
          meta: { requiresTenantData: true, title: '组织机构' },
        },
        {
          path: 'members',
          name: 'manage-members',
          component: () => import('@/views/tenant/TenantMemberView.vue'),
          meta: { requiresTenantData: true, title: '成员管理' },
        },
        {
          path: 'roles',
          name: 'manage-roles',
          component: () => import('@/views/tenant/TenantRoleView.vue'),
          meta: { requiresTenantData: true, title: '角色权限' },
        },
        {
          path: 'modules',
          name: 'manage-modules',
          component: () => import('@/views/tenant/TenantModuleView.vue'),
          meta: { requiresTenantData: true, title: '已授权模块' },
        },
        // ---- 基础信息中心（模块 01）：权限点与后端 BasModule 菜单定义同源 ----
        {
          path: 'master/categories',
          name: 'manage-master-categories',
          component: () => import('@/views/master/ItemCategoryView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'bas.category.menu',
            title: '商品分类',
          },
        },
        {
          path: 'master/units',
          name: 'manage-master-units',
          component: () => import('@/views/master/UnitMasterView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'bas.unit.menu',
            title: '计量单位',
          },
        },
        {
          path: 'master/suppliers',
          name: 'manage-master-suppliers',
          component: () => import('@/views/master/SupplierMasterView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'bas.supplier.menu',
            title: '供应商管理',
          },
        },
        {
          path: 'master/customers',
          name: 'manage-master-customers',
          component: () => import('@/views/master/CustomerMasterView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'bas.customer.menu',
            title: '客户管理',
          },
        },
        {
          path: 'master/persons',
          name: 'manage-master-persons',
          component: () => import('@/views/master/PersonMasterView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'bas.person.menu',
            title: '人员主数据',
          },
        },
        {
          path: 'master/dictionaries',
          name: 'manage-master-dictionaries',
          component: () => import('@/views/master/DictionaryMasterView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'bas.dict.menu',
            title: '数据字典',
          },
        },
        {
          path: 'master/code-rules',
          name: 'manage-master-code-rules',
          component: () => import('@/views/master/CodeRuleView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'bas.coderule.menu',
            title: '编码规则',
          },
        },
        {
          path: 'changelogs',
          name: 'manage-changelogs',
          component: () => import('@/views/master/ChangeLogView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'bas.changelog.menu',
            title: '变更日志',
          },
        },
        // ---- 物流管理（Wms）：仓库档案 ----
        {
          path: 'wms/warehouses',
          name: 'manage-wms-warehouses',
          component: () => import('@/views/wms/WarehouseView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'wms.warehouse.menu',
            title: '仓库档案',
          },
        },
        {
          path: 'wms/inbound',
          name: 'manage-wms-inbound',
          component: () => import('@/views/wms/StockInView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'wms.inbound.menu',
            title: '入库作业',
          },
        },
        {
          path: 'wms/outbound',
          name: 'manage-wms-outbound',
          component: () => import('@/views/wms/StockOutView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'wms.outbound.menu',
            title: '出库作业',
          },
        },
        {
          path: 'wms/transfers',
          name: 'manage-wms-transfers',
          component: () => import('@/views/wms/TransferView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'wms.transfer.menu',
            title: '物资调拨',
          },
        },
        {
          path: 'wms/stocktakes',
          name: 'manage-wms-stocktakes',
          component: () => import('@/views/wms/StockTakeView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'wms.stocktake.menu',
            title: '库存盘点',
          },
        },
        {
          path: 'wms/adjustments',
          name: 'manage-wms-adjustments',
          component: () => import('@/views/wms/InventoryAdjustView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'wms.adjust.menu',
            title: '盘盈盘亏',
          },
        },
        {
          path: 'wms/alerts',
          name: 'manage-wms-alerts',
          component: () => import('@/views/wms/InventoryAlertView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'wms.alert.menu',
            title: '库存预警',
          },
        },
        // ---- 商品与库存（Stk）：六页（与后端 Manifest 菜单顺序一致） ----
        {
          path: 'stk/items',
          name: 'manage-stk-items',
          component: () => import('@/views/stk/ItemMasterView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'stk.item.menu',
            title: '商品档案',
          },
        },
        {
          path: 'stk/openings',
          name: 'manage-stk-openings',
          component: () => import('@/views/stk/OpeningView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'stk.opening.menu',
            title: '期初建账',
          },
        },
        {
          path: 'stk/ledger',
          name: 'manage-stk-ledger',
          component: () => import('@/views/stk/StockLedgerView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'stk.ledger.menu',
            title: '库存台账',
          },
        },
        {
          path: 'stk/txns',
          name: 'manage-stk-txns',
          component: () => import('@/views/stk/StockTxnView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'stk.txn.menu',
            title: '出入库流水',
          },
        },
        {
          path: 'stk/cost-adjust',
          name: 'manage-stk-cost-adjust',
          component: () => import('@/views/stk/CostAdjustView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'stk.costadjust.menu',
            title: '成本调整',
          },
        },
        {
          path: 'stk/batches',
          name: 'manage-stk-batches',
          component: () => import('@/views/stk/BatchStockView.vue'),
          meta: {
            requiresTenantData: true,
            requiredPermissionCode: 'stk.batch.menu',
            title: '批次库存',
          },
        },
        {
          path: 'support/approvals',
          name: 'support-approvals',
          component: () => import('@/views/support/ApprovalCenterView.vue'),
          meta: { requiresTenantData: true, title: '审批中心' },
        },
        {
          path: 'support/messages',
          name: 'support-messages',
          component: () => import('@/views/support/MessageCenterView.vue'),
          meta: { requiresTenantData: true, title: '信息中心' },
        },
        {
          path: 'support/jobs',
          name: 'support-jobs',
          component: () => import('@/views/support/JobCenterView.vue'),
          meta: { requiresTenantData: true, title: '后台任务中心' },
        },
        {
          path: 'support/files',
          name: 'support-files',
          component: () => import('@/views/support/FileCenterView.vue'),
          meta: { requiresTenantData: true, title: '文件中心' },
        },
        {
          path: 'support/printing',
          name: 'support-printing',
          component: () => import('@/views/support/PrintingTemplateView.vue'),
          meta: { requiresTenantData: true, title: '打印模板' },
        },
      ],
    },
    {
      path: '/portal',
      component: () => import('@/layouts/PortalAdminLayout.vue'),
      meta: { requiresPortalAdmin: true, title: '门户管理' },
      children: [
        { path: '', redirect: '/portal/tenants' },
        {
          path: 'tenants',
          name: 'portal-tenants',
          component: () => import('@/views/portal/PortalTenantListView.vue'),
          meta: { requiresPortalAdmin: true, title: '租户管理' },
        },
        {
          path: 'tenants/:id',
          name: 'portal-tenant-detail',
          component: () => import('@/views/portal/PortalTenantDetailView.vue'),
          meta: { requiresPortalAdmin: true, title: '租户详情' },
        },
        {
          path: 'users',
          name: 'portal-users',
          component: () => import('@/views/portal/PortalUserListView.vue'),
          meta: { requiresPortalAdmin: true, title: '用户管理' },
        },
        {
          path: 'users/:id',
          name: 'portal-user-detail',
          component: () => import('@/views/portal/PortalUserDetailView.vue'),
          meta: { requiresPortalAdmin: true, title: '用户详情' },
        },
        {
          path: 'modules',
          name: 'portal-modules',
          component: () => import('@/views/portal/PortalModuleLicenseView.vue'),
          meta: { requiresPortalAdmin: true, title: '模块许可' },
        },
        {
          // 门户角色单页（左角色列表 + 右三 Tab，与租户角色页同构；方案 D4）
          path: 'roles',
          name: 'portal-roles',
          component: () => import('@/views/portal/PortalRoleAdminView.vue'),
          meta: { requiresPortalAdmin: true, title: '门户角色管理' },
        },
        {
          // 旧独立分配页已并入单页三 Tab，重定向保留一个版本周期（方案 D4）
          path: 'roles/:id/permissions',
          name: 'portal-role-permissions',
          redirect: { name: 'portal-roles' },
          meta: { requiresPortalAdmin: true, title: '角色权限分配' },
        },
        {
          path: 'roles/:id/members',
          name: 'portal-role-members',
          redirect: { name: 'portal-roles' },
          meta: { requiresPortalAdmin: true, title: '角色成员分配' },
        },
      ],
    },
    { path: '/:pathMatch(.*)*', redirect: '/workbench' },
  ],
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  auth.restore()

  document.title = to.meta.title
    ? `${to.meta.title} · 淇利馨集团数智经营管理平台`
    : '淇利馨集团数智经营管理平台'

  return resolveRouteGuard(to, auth)
})

export default router
