<!--
  所属公司：四川淇利馨集团有限公司
  描述：租户管理端布局——沿用租户顶栏（SiteHeader），左侧企业管理导航 + 右侧内容区
  说明：仅租户上下文可进入（规范 5.1 租户自管理域）；模块启停与自然人全局停用不在本域
-->
<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'
import PortalFooter from '@/components/PortalFooter.vue'
import SiteHeader from '@/components/SiteHeader.vue'

interface TenantNavItem {
  path: string
  name: string
  icon: string
  hint: string
}

const tenantNavItems: TenantNavItem[] = [
  { path: '/manage/organizations', name: '组织机构', icon: 'OfficeBuilding', hint: '公司 · 部门 · 团队' },
  { path: '/manage/members', name: '成员管理', icon: 'User', hint: '邀请 · 导入 · 启停' },
  { path: '/manage/roles', name: '角色权限', icon: 'Lock', hint: '功能 · 数据范围' },
  { path: '/manage/modules', name: '已授权模块', icon: 'Grid', hint: '门户许可范围内' },
]

const supportNavItems: TenantNavItem[] = [
  { path: '/manage/support/approvals', name: '审批中心', icon: 'Checked', hint: '待办 · 已办 · 委托' },
  { path: '/manage/support/messages', name: '信息中心', icon: 'Bell', hint: '收件 · 已发 · 订阅' },
  { path: '/manage/support/jobs', name: '后台任务', icon: 'Clock', hint: '调度 · 执行 · 日志' },
  { path: '/manage/support/files', name: '文件中心', icon: 'Folder', hint: '上传 · 预览 · 回收站' },
  { path: '/manage/support/printing', name: '打印模板', icon: 'Printer', hint: '模板 · 渲染预览' },
]

const route = useRoute()
const pageTitle = computed(() => (route.meta.title as string | undefined) ?? '企业管理')

function isActive(item: TenantNavItem): boolean {
  return route.path === item.path || route.path.startsWith(`${item.path}/`)
}
</script>

<template>
  <div class="tenant-admin">
    <SiteHeader />

    <div class="tenant-admin__body">
      <aside class="tenant-admin__side">
        <p class="tenant-admin__label">企业管理</p>

        <nav class="tenant-nav" aria-label="企业管理导航">
          <RouterLink
            v-for="item in tenantNavItems"
            :key="item.path"
            :to="item.path"
            class="tenant-nav__item"
            :class="{ 'is-active': isActive(item) }"
          >
            <span class="tenant-nav__icon">
              <AppIcon :name="item.icon" :size="16" />
            </span>
            <span class="tenant-nav__text">
              <strong>{{ item.name }}</strong>
              <em>{{ item.hint }}</em>
            </span>
          </RouterLink>
        </nav>

        <p class="tenant-admin__label tenant-admin__label--support">支撑功能</p>

        <nav class="tenant-nav" aria-label="支撑功能导航">
          <RouterLink
            v-for="item in supportNavItems"
            :key="item.path"
            :to="item.path"
            class="tenant-nav__item"
            :class="{ 'is-active': isActive(item) }"
          >
            <span class="tenant-nav__icon">
              <AppIcon :name="item.icon" :size="16" />
            </span>
            <span class="tenant-nav__text">
              <strong>{{ item.name }}</strong>
              <em>{{ item.hint }}</em>
            </span>
          </RouterLink>
        </nav>

        <section class="tenant-admin__scope">
          <h2>职责边界</h2>
          <ul>
            <li>
              <AppIcon name="Finished" :size="13" />
              组织、成员、角色与业务数据
            </li>
            <li>
              <AppIcon name="Finished" :size="13" />
              租户内成员启停与权限分配
            </li>
            <li class="is-denied">
              <AppIcon name="Warning" :size="13" />
              模块启停由门户许可控制
            </li>
            <li class="is-denied">
              <AppIcon name="Warning" :size="13" />
              自然人全局停用由门户控制
            </li>
          </ul>
          <p>以上边界依据《SaaS 架构规范》5.1 租户自管理域。</p>
        </section>
      </aside>

      <main class="tenant-admin__main">
        <nav class="tenant-admin__crumb" aria-label="面包屑">
          <RouterLink to="/workbench">工作台</RouterLink>
          <span class="tenant-admin__crumb-sep">/</span>
          <span>企业管理</span>
          <span class="tenant-admin__crumb-sep">/</span>
          <span class="tenant-admin__crumb-current">{{ pageTitle }}</span>
        </nav>
        <RouterView />
      </main>
    </div>

    <PortalFooter />
  </div>
</template>

<style scoped>
.tenant-admin {
  display: flex;
  min-height: 100%;
  flex-direction: column;
  background: var(--qlx-canvas);
}

.tenant-admin__body {
  display: flex;
  width: 100%;
  max-width: var(--qlx-content-max);
  flex: 1;
  margin: 0 auto;
  align-items: stretch;
}

/* ============ 左侧导航 ============ */
.tenant-admin__side {
  display: flex;
  width: 224px;
  flex: 0 0 auto;
  flex-direction: column;
  padding: var(--qlx-space-4) var(--qlx-space-3) var(--qlx-space-6);
  border-right: 1px solid var(--qlx-line);
  background: var(--qlx-surface);
}

.tenant-admin__label {
  padding: 0 var(--qlx-space-3) var(--qlx-space-2);
  color: var(--qlx-ink-400);
  font-family: var(--qlx-font-mono);
  font-size: var(--qlx-text-xs);
  letter-spacing: 0.14em;
}

.tenant-admin__label--support {
  margin-top: var(--qlx-space-5);
}

.tenant-nav {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.tenant-nav__item {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-3);
  padding: var(--qlx-space-2) var(--qlx-space-3);
  border-left: 3px solid transparent;
  border-radius: 0 var(--qlx-radius-sm) var(--qlx-radius-sm) 0;
  color: var(--qlx-ink-700);
  transition:
    background var(--qlx-duration-fast) var(--qlx-ease-out),
    color var(--qlx-duration-fast) var(--qlx-ease-out);
}

.tenant-nav__item:hover {
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-700);
}

.tenant-nav__item.is-active {
  border-left-color: var(--qlx-brand-700);
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-800);
}

.tenant-nav__icon {
  display: inline-flex;
  width: 30px;
  height: 30px;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-xs);
  background: var(--qlx-surface);
  color: currentcolor;
}

.tenant-nav__item.is-active .tenant-nav__icon {
  border-color: var(--qlx-brand-100);
  background: var(--qlx-brand-700);
  color: #fff;
}

.tenant-nav__text {
  display: flex;
  min-width: 0;
  flex-direction: column;
  line-height: 1.35;
}

.tenant-nav__text strong {
  font-size: var(--qlx-text-base);
  font-weight: 500;
}

.tenant-nav__text em {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  font-style: normal;
}

.tenant-nav__item.is-active .tenant-nav__text em {
  color: var(--qlx-brand-600);
}

/* ============ 职责边界 ============ */
.tenant-admin__scope {
  margin-top: auto;
  padding: var(--qlx-space-3);
  border-top: 1px dashed var(--qlx-ink-200);
}

.tenant-admin__scope h2 {
  margin-bottom: var(--qlx-space-2);
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
  font-weight: 600;
  letter-spacing: 0.08em;
}

.tenant-admin__scope ul {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.tenant-admin__scope li {
  display: flex;
  align-items: flex-start;
  gap: 6px;
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-xs);
  line-height: 1.6;
}

.tenant-admin__scope li :deep(.el-icon) {
  margin-top: 2px;
  color: var(--qlx-success);
}

.tenant-admin__scope li.is-denied :deep(.el-icon) {
  color: var(--qlx-warning);
}

.tenant-admin__scope p {
  margin-top: var(--qlx-space-2);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

/* ============ 内容区 ============ */
.tenant-admin__main {
  flex: 1;
  min-width: 0;
  padding: var(--qlx-space-4) var(--qlx-space-6) var(--qlx-space-10);
}

.tenant-admin__crumb {
  display: flex;
  margin-bottom: var(--qlx-space-4);
  align-items: center;
  gap: var(--qlx-space-2);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-sm);
}

.tenant-admin__crumb a {
  color: var(--qlx-ink-500);
}

.tenant-admin__crumb a:hover {
  color: var(--qlx-brand-600);
}

.tenant-admin__crumb-sep {
  color: var(--qlx-ink-300);
}

.tenant-admin__crumb-current {
  color: var(--qlx-ink-700);
}

@media (max-width: 1080px) {
  .tenant-admin__side {
    width: 68px;
    padding: var(--qlx-space-4) var(--qlx-space-2);
  }

  .tenant-admin__label,
  .tenant-nav__text,
  .tenant-admin__scope {
    display: none;
  }

  .tenant-nav__item {
    justify-content: center;
    border-left-width: 2px;
    padding: var(--qlx-space-2);
  }
}

@media (max-width: 720px) {
  .tenant-admin__body {
    flex-direction: column;
  }

  .tenant-admin__side {
    width: 100%;
    flex-direction: row;
    padding: var(--qlx-space-2);
    border-right: 0;
    border-bottom: 1px solid var(--qlx-line);
    overflow-x: auto;
  }

  .tenant-nav {
    flex-direction: row;
  }

  .tenant-admin__main {
    padding: var(--qlx-space-4);
  }
}
</style>
