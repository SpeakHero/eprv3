<!--
  所属公司：四川淇利馨集团有限公司
  描述：工作台——顶栏导航（悬停全量菜单）、指标卡、我的待办、常用功能、通知公告
  说明：业务功能页一律以原生新标签页打开（target="_blank"），工作台本身固定为第一个标签页
-->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import PortalFooter from '@/components/PortalFooter.vue'
import SiteHeader from '@/components/SiteHeader.vue'
import { findModule, findNavItem, moduleHref, navModules, quickEntries } from '@/data/navigation'
import {
  formatLastLogin,
  greetingOf,
  longDateText,
  workbenchNotices,
  workbenchStats,
  workbenchTodos,
} from '@/data/workbench'
import { fetchPortalWorkbenchOverview } from '@/api/admin'
import type { PortalWorkbenchOverview } from '@/api/admin'
import { fetchTenantModules } from '@/api/tenantAdmin'
import { useAuthStore } from '@/stores/auth'
import { memberRoleLabel } from '@/types/auth'
import { formatDate } from '@/utils/format'

/** 待办类型 → 办理页（原生新标签）。 */
const todoRoutes: Record<string, string> = {
  采购订单: 'pur/order',
  付款申请: 'fin/fund',
  销售合同: 'ctr/archive',
  费用报销: 'fin/expense',
  供应商准入: 'bas/supplier',
}

/** 门户管理模式常用功能（门户管理端页面）。 */
const portalQuickLinks = [
  { href: '/portal/tenants', icon: 'OfficeBuilding', name: '租户管理', moduleName: '开通 · 启停 · 许可' },
  { href: '/portal/users', icon: 'User', name: '用户管理', moduleName: '自然人 · 全局启停' },
  { href: '/portal/modules', icon: 'Setting', name: '模块许可', moduleName: '模块目录 · 授权矩阵' },
]

/** 门户职责清单（规范 4.1）。 */
const portalScopes = [
  '租户注册、开通、停用与注销',
  '租户管理员创建与初始激活',
  '模块启用/停用（许可控制）',
  '自然人全局停用（禁止登录任何租户）',
  '全局审计查询与系统参数配置',
]

const auth = useAuthStore()

/** 模块目录名称（以租户模块许可接口下发的权威名称为准，导航目录仅兜底）。 */
const moduleCatalogNames = ref<Record<string, string>>({})

/** 门户管理运营概览（门户管理模式从 /api/portal/admin/workbench/overview 加载）。 */
const portalOverview = ref<PortalWorkbenchOverview | null>(null)
const isPortal = computed(() => auth.portalAdminMode)

onMounted(() => {
  void loadModuleCatalog()
  if (!auth.portalAdminMode) {
    return
  }
  void fetchPortalWorkbenchOverview()
    .then((overview) => {
      portalOverview.value = overview
    })
    .catch(() => {
      // 工作台概览加载失败不阻断页面（保留骨架）
    })
})

const greeting = computed(() => greetingOf(new Date().getHours()))
const dateText = computed(() => longDateText(new Date()))
// X-9 整改：门户管理模式不套用租户角色/进入文案，按 portalAdminMode 分流为平台运营口径
const roleLabel = computed(() =>
  isPortal.value
    ? '平台管理员'
    : memberRoleLabel(auth.user?.memberRole ?? auth.currentTenant?.memberRole),
)
const lastLoginText = computed(() =>
  isPortal.value
    ? '平台运营视图'
    : auth.currentTenant?.lastLoginAt
      ? `最近进入 ${formatLastLogin(auth.currentTenant.lastLoginAt)}`
      : '首次进入本企业',
)

/** 是否可进入企业管理（租户管理员且非门户管理模式）。 */
const canManageTenant = computed(
  () =>
    !isPortal.value &&
    (auth.user?.memberRole === 'TenantAdmin' || auth.currentTenant?.memberRole === 'TenantAdmin'),
)

/** 企业管理入口（租户管理员常用功能首位）。 */
const managementEntry = {
  href: '/manage/organizations',
  icon: 'Setting',
  name: '企业管理',
  moduleName: '组织 · 成员 · 角色权限',
}

/** 加载许可模块的权威名称（租户上下文才有 /api/tenant/modules；失败回退导航目录）。 */
async function loadModuleCatalog(): Promise<void> {
  if (auth.portalAdminMode || !auth.tenantToken) {
    return
  }
  try {
    const modules = await fetchTenantModules()
    moduleCatalogNames.value = Object.fromEntries(
      modules.filter((item) => item.licensed).map((item) => [item.code, item.name]),
    )
  } catch {
    // 许可接口不可用时保持导航目录兜底名称
  }
}

/** 已授权模块（许可未下发时按目录全量展示）。 */
const licensedModuleNames = computed(() => {
  const codes = auth.licensedModules.length
    ? auth.licensedModules
    : navModules.map((module) => module.code)
  return codes
    .map((code) => moduleCatalogNames.value[code] ?? findModule(code)?.name)
    .filter((name): name is string => Boolean(name))
})

/** 常用功能（按许可过滤，最多 8 项；租户管理员首位为企业管理；门户管理模式展示门户管理入口）。 */
const quickLinks = computed(() => {
  if (isPortal.value) {
    return portalQuickLinks
  }
  const links = quickEntries
    .filter((entry) => auth.licensedModules.length === 0 || auth.isModuleLicensed(entry.moduleCode))
    .map((entry) => {
      const module = findModule(entry.moduleCode)
      const item = findNavItem(entry.moduleCode, entry.itemCode)
      return {
        href: moduleHref(entry.moduleCode, entry.itemCode),
        icon: item?.icon ?? 'Grid',
        name: item?.name ?? entry.itemCode,
        moduleName: module?.name ?? entry.moduleCode,
      }
    })
  return canManageTenant.value ? [managementEntry, ...links.slice(0, 7)] : links.slice(0, 8)
})

function todoHref(docType: string): string {
  const route = todoRoutes[docType]
  return route ? `/module/${route}` : '/module/bas/dictionary'
}
</script>

<template>
  <div class="workbench">
    <SiteHeader />

    <main class="workbench__main">
      <div class="qlx-container">
        <section class="hero">
          <div>
            <p class="qlx-eyebrow">
              {{ auth.portalAdminMode ? 'PORTAL ADMIN · 门户管理' : `WORKBENCH · ${auth.currentTenant?.tenantCode}` }}
            </p>
            <h1 class="hero__title">{{ greeting }}，{{ auth.displayName }}</h1>
            <p class="hero__sub">
              {{ auth.tenantName }}
              <span class="hero__dot" aria-hidden="true">·</span>
              {{ roleLabel }}
              <span class="hero__dot" aria-hidden="true">·</span>
              {{ lastLoginText }}
            </p>
          </div>
          <div class="hero__aside">
            <p class="hero__date qlx-numeric">{{ dateText }}</p>
            <RouterLink v-if="canManageTenant" class="hero__manage" to="/manage/organizations">
              企业管理
            </RouterLink>
            <RouterLink class="hero__switch" to="/select-tenant">切换企业</RouterLink>
          </div>
        </section>

        <section v-if="isPortal" class="stats" aria-label="平台运营概览">
          <article
            v-for="stat in portalOverview?.stats ?? []"
            :key="stat.key"
            class="stat"
            :class="`stat--${stat.tone}`"
          >
            <div class="stat__top">
              <span>{{ stat.label }}</span>
              <AppIcon :name="stat.icon" :size="16" />
            </div>
            <p class="stat__value qlx-numeric">
              {{ stat.value }}<em>{{ stat.unit }}</em>
            </p>
            <p class="stat__trend">{{ stat.hint }}</p>
          </article>
        </section>
        <section v-else class="stats" aria-label="经营概览">
          <article v-for="stat in workbenchStats" :key="stat.key" class="stat" :class="`stat--${stat.tone}`">
            <div class="stat__top">
              <span>{{ stat.label }}</span>
              <AppIcon :name="stat.icon" :size="16" />
            </div>
            <p class="stat__value qlx-numeric">
              {{ stat.value }}<em>{{ stat.unit }}</em>
            </p>
            <p class="stat__trend">{{ stat.trend }}</p>
          </article>
        </section>

        <div class="columns">
          <div class="columns__primary">
            <section class="card">
              <header class="card__head">
                <h2>{{ isPortal ? '运营待办' : '我的待办' }}</h2>
                <span class="card__hint">
                  共 {{ (isPortal ? portalOverview?.todos.length : workbenchTodos.length) ?? 0 }} 件待处理
                </span>
                <span class="card__more">全部待办 →</span>
              </header>

              <!-- 门户管理模式：平台运营待办（租户预配 / 到期 / 许可复核） -->
              <div v-if="isPortal" class="portal-todo">
                <a
                  v-for="todo in portalOverview?.todos ?? []"
                  :key="todo.id"
                  class="portal-todo__item"
                  :href="todo.href"
                  target="_blank"
                  rel="noopener"
                >
                  <span class="pill" :class="`pill--${todo.level}`">{{ todo.type }}</span>
                  <span class="portal-todo__title">{{ todo.title }}</span>
                  <span class="portal-todo__time qlx-numeric">{{ formatDate(todo.at) }}</span>
                  <AppIcon class="portal-todo__go" name="ArrowRight" :size="13" />
                </a>
                <p v-if="!portalOverview?.todos.length" class="card__empty">暂无运营待办。</p>
              </div>

              <div v-else class="todo-wrap">
                <table class="todo">
                  <thead>
                    <tr>
                      <th scope="col">单据</th>
                      <th scope="col">提交人</th>
                      <th scope="col" class="is-num">金额</th>
                      <th scope="col">提交时间</th>
                      <th scope="col">状态</th>
                      <th scope="col" class="is-action">操作</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="todo in workbenchTodos" :key="todo.docNo">
                      <td>
                        <span class="todo__type">{{ todo.docType }}</span>
                        <span class="todo__no qlx-numeric">{{ todo.docNo }}</span>
                      </td>
                      <td>{{ todo.submitter }}</td>
                      <td class="is-num qlx-numeric">{{ todo.amount }}</td>
                      <td class="qlx-numeric">{{ todo.submittedAt }}</td>
                      <td>
                        <span class="pill" :class="`pill--${todo.statusType}`">{{ todo.status }}</span>
                      </td>
                      <td class="is-action">
                        <a :href="todoHref(todo.docType)" target="_blank" rel="noopener" class="todo__handle">
                          办理 →
                        </a>
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </section>

            <section class="card">
              <header class="card__head">
                <h2>常用功能</h2>
                <span class="card__hint">点击在新标签页打开，可与工作台并行办理</span>
              </header>
              <div v-if="quickLinks.length" class="quick">
                <a
                  v-for="entry in quickLinks"
                  :key="entry.href"
                  class="quick__item"
                  :href="entry.href"
                  target="_blank"
                  rel="noopener"
                >
                  <span class="quick__icon"><AppIcon :name="entry.icon" :size="18" /></span>
                  <span class="quick__text">
                    <strong>{{ entry.name }}</strong>
                    <em>{{ entry.moduleName }}</em>
                  </span>
                </a>
              </div>
              <p v-else class="card__empty">暂无已开通的功能模块，请联系集团管理员调整许可。</p>
            </section>
          </div>

          <aside class="columns__side">
            <!-- 门户管理模式：职责边界说明 -->
            <section v-if="isPortal" class="card">
              <header class="card__head">
                <h2>门户职责边界</h2>
              </header>
              <ul class="portal-scope">
                <li v-for="item in portalScopes" :key="item">
                  <AppIcon name="Finished" :size="13" />
                  {{ item }}
                </li>
              </ul>
              <p class="portal-scope__note">
                租户内部组织、角色权限与业务数据由租户自行管理，门户不参与（规范 4.1 / 5.1）。
              </p>
            </section>

            <template v-else>
              <section class="card">
                <header class="card__head">
                  <h2>通知公告</h2>
                  <span class="card__more">更多 →</span>
                </header>
                <ul class="notice">
                  <li v-for="notice in workbenchNotices" :key="notice.title">
                    <span class="notice__tag">{{ notice.tag }}</span>
                    <span class="notice__title">{{ notice.title }}</span>
                    <span class="notice__date qlx-numeric">{{ notice.date }}</span>
                  </li>
                </ul>
              </section>

              <section class="card">
                <header class="card__head">
                  <h2>已开通模块</h2>
                  <span class="card__hint">{{ licensedModuleNames.length }} 个</span>
                </header>
                <div v-if="licensedModuleNames.length" class="license">
                  <span v-for="name in licensedModuleNames" :key="name" class="license__chip">
                    {{ name }}
                  </span>
                </div>
                <p v-else class="card__empty">暂无模块许可记录。</p>
                <p class="license__note">如需开通其他模块，请联系集团管理员调整许可。</p>
              </section>
            </template>
          </aside>
        </div>
      </div>
    </main>

    <PortalFooter />
  </div>
</template>

<style scoped>
.workbench {
  display: flex;
  min-height: 100%;
  flex-direction: column;
  background: var(--qlx-canvas);
}

.workbench__main {
  flex: 1;
  padding: var(--qlx-space-6) 0 var(--qlx-space-10);
}

/* ============ 门户管理模式（平台运营视图） ============ */
.portal-todo {
  display: flex;
  flex-direction: column;
}

.portal-todo__item {
  display: flex;
  padding: var(--qlx-space-3) 0;
  border-bottom: 1px dashed var(--qlx-ink-100);
  align-items: center;
  gap: var(--qlx-space-3);
}

.portal-todo__item:last-of-type {
  border-bottom: 0;
}

.portal-todo__title {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  color: var(--qlx-ink-700);
  font-size: var(--qlx-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.portal-todo__time {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.portal-todo__go {
  color: var(--qlx-ink-300);
  transition: color var(--qlx-duration-fast) var(--qlx-ease-out);
}

.portal-todo__item:hover .portal-todo__go {
  color: var(--qlx-brand-600);
}

.portal-scope {
  display: flex;
  flex-direction: column;
  gap: var(--qlx-space-2);
}

.portal-scope li {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  color: var(--qlx-ink-700);
  font-size: var(--qlx-text-sm);
}

.portal-scope li :deep(.el-icon) {
  color: var(--qlx-success);
}

.portal-scope__note {
  margin-top: var(--qlx-space-3);
  padding-top: var(--qlx-space-3);
  border-top: 1px dashed var(--qlx-ink-100);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

/* 门户指标卡补充色调 */
.stat--gold .stat__top :deep(.el-icon) {
  color: var(--qlx-gold-600);
}

.stat--success .stat__top :deep(.el-icon) {
  color: var(--qlx-success);
}

/* ============ 问候区 ============ */
.hero {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: var(--qlx-space-4);
  padding: var(--qlx-space-5) var(--qlx-space-6);
  border: 1px solid var(--qlx-line);
  border-left: 3px solid var(--qlx-brand-700);
  border-radius: var(--qlx-radius-md);
  background:
    linear-gradient(96deg, var(--qlx-surface) 0%, var(--qlx-surface) 55%, var(--qlx-brand-50) 100%);
}

.hero__title {
  margin-top: var(--qlx-space-1);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-2xl);
  font-weight: 600;
  letter-spacing: 0.05em;
}

.hero__sub {
  margin-top: var(--qlx-space-1);
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-sm);
}

.hero__dot {
  margin: 0 6px;
  color: var(--qlx-ink-300);
}

.hero__aside {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  gap: var(--qlx-space-4);
}

.hero__date {
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-sm);
}

.hero__switch {
  display: inline-flex;
  height: 32px;
  align-items: center;
  padding: 0 var(--qlx-space-4);
  border: 1px solid var(--qlx-brand-700);
  border-radius: var(--qlx-radius-sm);
  color: var(--qlx-brand-700);
  font-size: var(--qlx-text-sm);
  transition: background var(--qlx-duration-fast) var(--qlx-ease-out),
    color var(--qlx-duration-fast) var(--qlx-ease-out);
}

.hero__switch:hover {
  background: var(--qlx-brand-700);
  color: #fff;
}

.hero__manage {
  display: inline-flex;
  height: 32px;
  align-items: center;
  padding: 0 var(--qlx-space-4);
  border: 1px solid var(--qlx-brand-700);
  border-radius: var(--qlx-radius-sm);
  background: var(--qlx-brand-700);
  color: #fff;
  font-size: var(--qlx-text-sm);
  transition: background var(--qlx-duration-fast) var(--qlx-ease-out),
    border-color var(--qlx-duration-fast) var(--qlx-ease-out);
}

.hero__manage:hover {
  border-color: var(--qlx-brand-600);
  background: var(--qlx-brand-600);
}

/* ============ 指标卡 ============ */
.stats {
  display: grid;
  gap: var(--qlx-space-4);
  margin-top: var(--qlx-space-4);
  grid-template-columns: repeat(4, minmax(0, 1fr));
}

.stat {
  padding: var(--qlx-space-4) var(--qlx-space-5);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
}

.stat__top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
}

.stat--brand .stat__top :deep(.el-icon) {
  color: var(--qlx-brand-600);
}

.stat--warning .stat__top :deep(.el-icon) {
  color: var(--qlx-warning);
}

.stat--info .stat__top :deep(.el-icon) {
  color: var(--qlx-info);
}

.stat--danger .stat__top :deep(.el-icon) {
  color: var(--qlx-danger);
}

.stat__value {
  margin-top: var(--qlx-space-2);
  color: var(--qlx-ink-900);
  font-size: 30px;
  font-weight: 600;
  letter-spacing: 0.02em;
  line-height: 1.2;
}

.stat__value em {
  margin-left: 4px;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  font-style: normal;
  font-weight: 400;
}

.stat__trend {
  margin-top: var(--qlx-space-1);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

/* ============ 两栏 ============ */
.columns {
  display: grid;
  align-items: start;
  gap: var(--qlx-space-4);
  margin-top: var(--qlx-space-4);
  grid-template-columns: minmax(0, 2.15fr) minmax(0, 1fr);
}

.columns__primary,
.columns__side {
  display: flex;
  flex-direction: column;
  gap: var(--qlx-space-4);
}

/* ============ 卡片 ============ */
.card {
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
}

.card__head {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-3);
  padding: var(--qlx-space-4) var(--qlx-space-5) var(--qlx-space-3);
  border-bottom: 1px solid var(--qlx-ink-100);
}

.card__head h2 {
  position: relative;
  padding-left: var(--qlx-space-3);
  color: var(--qlx-ink-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-md);
  font-weight: 600;
  letter-spacing: 0.04em;
}

.card__head h2::before {
  position: absolute;
  top: 50%;
  left: 0;
  width: 3px;
  height: 14px;
  background: var(--qlx-brand-700);
  content: '';
  transform: translateY(-50%);
}

.card__hint {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.card__more {
  margin-left: auto;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  cursor: default;
}

.card__empty {
  padding: var(--qlx-space-6) var(--qlx-space-5);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-sm);
}

/* ============ 待办表 ============ */
.todo-wrap {
  overflow-x: auto;
}

.todo {
  width: 100%;
  border-collapse: collapse;
  font-size: var(--qlx-text-sm);
}

.todo th {
  padding: var(--qlx-space-2) var(--qlx-space-4);
  border-bottom: 1px solid var(--qlx-ink-100);
  background: var(--qlx-canvas);
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
  font-weight: 500;
  text-align: left;
  white-space: nowrap;
}

.todo td {
  padding: var(--qlx-space-3) var(--qlx-space-4);
  border-bottom: 1px solid var(--qlx-ink-100);
  color: var(--qlx-ink-700);
  vertical-align: middle;
}

.todo tbody tr:last-child td {
  border-bottom: 0;
}

.todo tbody tr {
  transition: background var(--qlx-duration-fast) var(--qlx-ease-out);
}

.todo tbody tr:hover {
  background: var(--qlx-brand-50);
}

.todo .is-num {
  text-align: right;
  white-space: nowrap;
}

.todo .is-action {
  text-align: right;
  white-space: nowrap;
}

.todo__type {
  display: block;
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
}

.todo__no {
  color: var(--qlx-ink-800);
  font-size: var(--qlx-text-xs);
  letter-spacing: 0.02em;
}

.todo__handle {
  color: var(--qlx-brand-700);
  font-size: var(--qlx-text-xs);
  white-space: nowrap;
}

.todo__handle:hover {
  text-decoration: underline;
}

.pill {
  display: inline-flex;
  padding: 1px 8px;
  border: 1px solid transparent;
  border-radius: var(--qlx-radius-xs);
  font-size: var(--qlx-text-xs);
  white-space: nowrap;
}

.pill--warning {
  border-color: var(--qlx-warning-soft);
  background: var(--qlx-warning-soft);
  color: var(--qlx-warning);
}

.pill--info {
  border-color: var(--qlx-info-soft);
  background: var(--qlx-info-soft);
  color: var(--qlx-info);
}

.pill--success {
  border-color: var(--qlx-success-soft);
  background: var(--qlx-success-soft);
  color: var(--qlx-success);
}

.pill--danger {
  border-color: var(--qlx-danger-soft);
  background: var(--qlx-danger-soft);
  color: var(--qlx-danger);
}

/* ============ 常用功能 ============ */
.quick {
  display: grid;
  gap: var(--qlx-space-2);
  padding: var(--qlx-space-4);
  grid-template-columns: repeat(4, minmax(0, 1fr));
}

.quick__item {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-3);
  padding: var(--qlx-space-3);
  border: 1px solid transparent;
  border-radius: var(--qlx-radius-sm);
  transition:
    border-color var(--qlx-duration-fast) var(--qlx-ease-out),
    background var(--qlx-duration-fast) var(--qlx-ease-out);
}

.quick__item:hover {
  border-color: var(--qlx-brand-100);
  background: var(--qlx-brand-50);
}

.quick__icon {
  display: inline-flex;
  width: 34px;
  height: 34px;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--qlx-brand-100);
  border-radius: var(--qlx-radius-sm);
  background: var(--qlx-surface);
  color: var(--qlx-brand-700);
}

.quick__text {
  display: flex;
  min-width: 0;
  flex-direction: column;
  line-height: 1.35;
}

.quick__text strong {
  overflow: hidden;
  color: var(--qlx-ink-800);
  font-size: var(--qlx-text-sm);
  font-weight: 500;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.quick__text em {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  font-style: normal;
}

/* ============ 通知公告 ============ */
.notice {
  padding: var(--qlx-space-1) var(--qlx-space-5) var(--qlx-space-3);
}

.notice li {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  padding: var(--qlx-space-3) 0;
  border-bottom: 1px dashed var(--qlx-ink-100);
}

.notice li:last-child {
  border-bottom: 0;
}

.notice__tag {
  flex: 0 0 auto;
  padding: 0 6px;
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-xs);
  color: var(--qlx-ink-500);
  font-size: 11px;
  line-height: 18px;
}

.notice__title {
  overflow: hidden;
  flex: 1;
  color: var(--qlx-ink-700);
  font-size: var(--qlx-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.notice__date {
  flex: 0 0 auto;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

/* ============ 模块许可 ============ */
.license {
  display: flex;
  flex-wrap: wrap;
  gap: var(--qlx-space-2);
  padding: var(--qlx-space-4) var(--qlx-space-5) 0;
}

.license__chip {
  padding: 2px 10px;
  border: 1px solid var(--qlx-brand-100);
  border-radius: var(--qlx-radius-xs);
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-700);
  font-size: var(--qlx-text-xs);
}

.license__note {
  padding: var(--qlx-space-3) var(--qlx-space-5) var(--qlx-space-4);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

@media (max-width: 1080px) {
  .columns {
    grid-template-columns: minmax(0, 1fr);
  }

  .stats {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .quick {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 720px) {
  .hero {
    flex-direction: column;
    align-items: flex-start;
  }

  .hero__aside {
    width: 100%;
    justify-content: space-between;
  }

  .stats {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
