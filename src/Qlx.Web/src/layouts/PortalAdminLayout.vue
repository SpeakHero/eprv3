<!--
  所属公司：四川淇利馨集团有限公司
  描述：门户管理端布局——顶栏（品牌 + 门户徽记 + 用户操作）+ 左侧导航 + 内容区
  说明：门户管理员专享（凭身份级 Token）；与租户侧 SiteHeader 工作台布局区分
-->
<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import BrandMark from '@/components/BrandMark.vue'
import { useAuthStore } from '@/stores/auth'

interface AdminNavItem {
  path: string
  name: string
  icon: string
  /** 占位项（后续阶段开放）。 */
  placeholder?: boolean
}

const navItems: AdminNavItem[] = [
  { path: '/portal/tenants', name: '租户管理', icon: 'OfficeBuilding' },
  { path: '/portal/users', name: '用户管理', icon: 'User' },
  { path: '/portal/modules', name: '模块许可', icon: 'Setting' },
  { path: '/portal/roles', name: '门户角色', icon: 'UserFilled' },
  { path: '/portal/audit', name: '审计中心', icon: 'Document', placeholder: true },
  { path: '/portal/settings', name: '系统配置', icon: 'SetUp', placeholder: true },
]

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const initial = computed(() => (auth.displayName || auth.loginAccount || '管').slice(0, 1))
const pageTitle = computed(() => (route.meta.title as string | undefined) ?? '')

function isActive(item: AdminNavItem): boolean {
  return route.path === item.path || route.path.startsWith(`${item.path}/`)
}

function onNav(item: AdminNavItem): void {
  if (item.placeholder) {
    ElMessage.info('该功能将在后续阶段开放')
    return
  }
  void router.push(item.path)
}

async function signOut(): Promise<void> {
  try {
    await ElMessageBox.confirm('退出后需重新登录，是否继续？', '退出登录', {
      confirmButtonText: '退出登录',
      cancelButtonText: '取消',
      type: 'warning',
    })
  } catch {
    return
  }
  await auth.signOut()
  await router.replace('/login')
}
</script>

<template>
  <div class="admin-layout">
    <header class="admin-layout__bar">
      <div class="admin-layout__brand">
        <BrandMark theme="dark" compact />
        <span class="admin-layout__badge">
          <AppIcon name="Setting" :size="12" />
          门户管理
        </span>
      </div>
      <div class="admin-layout__actions">
        <RouterLink class="admin-layout__back" to="/workbench">
          <AppIcon name="ArrowLeft" :size="14" />
          返回工作台
        </RouterLink>
        <el-dropdown trigger="click" @command="(cmd: string) => cmd === 'logout' && signOut()">
          <button type="button" class="admin-layout__user">
            <span class="admin-layout__avatar" aria-hidden="true">{{ initial }}</span>
            <span class="admin-layout__meta">
              <strong>{{ auth.displayName || auth.loginAccount }}</strong>
              <span>平台管理员</span>
            </span>
            <AppIcon name="ArrowDown" :size="12" />
          </button>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item disabled>
                <span class="admin-layout__account qlx-numeric">{{ auth.loginAccount }}</span>
              </el-dropdown-item>
              <el-dropdown-item command="logout" divided>
                <AppIcon name="SwitchButton" :size="14" />
                退出登录
              </el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
      </div>
    </header>

    <div class="admin-layout__body">
      <aside class="admin-layout__side">
        <nav aria-label="门户管理导航">
          <button
            v-for="item in navItems"
            :key="item.path"
            type="button"
            class="admin-nav__item"
            :class="{ 'is-active': isActive(item), 'is-placeholder': item.placeholder }"
            @click="onNav(item)"
          >
            <AppIcon :name="item.icon" :size="16" />
            <span>{{ item.name }}</span>
            <em v-if="item.placeholder">后续阶段</em>
          </button>
        </nav>

        <p class="admin-layout__side-note">
          门户负责全局管控：租户开通、模块许可与自然人账号。租户内部组织、角色与业务数据由租户自行管理。
        </p>
      </aside>

      <main class="admin-layout__content">
        <nav class="admin-layout__crumb" aria-label="面包屑">
          <RouterLink to="/portal/tenants">门户管理</RouterLink>
          <span class="admin-layout__crumb-sep">/</span>
          <span>{{ pageTitle }}</span>
        </nav>
        <RouterView />
      </main>
    </div>
  </div>
</template>

<style scoped>
.admin-layout {
  display: flex;
  min-height: 100%;
  flex-direction: column;
  background: var(--qlx-canvas);
}

/* ============ 顶栏 ============ */
.admin-layout__bar {
  display: flex;
  height: var(--qlx-header-height);
  flex: 0 0 auto;
  align-items: center;
  justify-content: space-between;
  gap: var(--qlx-space-6);
  padding: 0 var(--qlx-space-6);
  background: linear-gradient(90deg, var(--qlx-brand-900), var(--qlx-brand-800) 62%, var(--qlx-brand-700));
  border-bottom: 2px solid var(--qlx-gold-500);
}

.admin-layout__brand {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-4);
}

.admin-layout__badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 3px 10px;
  border: 1px solid var(--qlx-gold-400);
  border-radius: var(--qlx-radius-xs);
  color: var(--qlx-gold-400);
  font-size: var(--qlx-text-xs);
  letter-spacing: 0.12em;
}

.admin-layout__actions {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-4);
}

.admin-layout__back {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 6px 12px;
  border: 1px solid rgb(255 255 255 / 18%);
  border-radius: var(--qlx-radius-sm);
  color: rgb(255 255 255 / 82%);
  font-size: var(--qlx-text-sm);
  transition: background var(--qlx-duration-fast) var(--qlx-ease-out), color var(--qlx-duration-fast) var(--qlx-ease-out);
}

.admin-layout__back:hover {
  background: rgb(255 255 255 / 12%);
  color: #fff;
}

.admin-layout__user {
  display: inline-flex;
  align-items: center;
  gap: var(--qlx-space-2);
  padding: 4px var(--qlx-space-3);
  border: 1px solid rgb(255 255 255 / 18%);
  border-radius: var(--qlx-radius-sm);
  background: transparent;
  color: #fff;
  cursor: pointer;
}

.admin-layout__user:hover {
  background: rgb(255 255 255 / 12%);
}

.admin-layout__avatar {
  display: inline-flex;
  width: 26px;
  height: 26px;
  align-items: center;
  justify-content: center;
  border-radius: var(--qlx-radius-xs);
  background: var(--qlx-gold-500);
  color: var(--qlx-brand-900);
  font-family: var(--qlx-font-serif);
  font-size: 14px;
  font-weight: 600;
}

.admin-layout__meta {
  display: flex;
  flex-direction: column;
  gap: 1px;
  line-height: 1.25;
  text-align: left;
}

.admin-layout__meta strong {
  font-size: var(--qlx-text-sm);
  font-weight: 500;
}

.admin-layout__meta span {
  color: rgb(255 255 255 / 62%);
  font-size: var(--qlx-text-xs);
}

.admin-layout__account {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

/* ============ 主体 ============ */
.admin-layout__body {
  display: flex;
  flex: 1;
  min-height: 0;
}

.admin-layout__side {
  display: flex;
  width: 216px;
  flex: 0 0 auto;
  flex-direction: column;
  padding: var(--qlx-space-4) var(--qlx-space-3);
  border-right: 1px solid var(--qlx-line);
  background: var(--qlx-surface);
}

.admin-nav__item {
  display: flex;
  width: 100%;
  align-items: center;
  gap: var(--qlx-space-2);
  padding: 10px var(--qlx-space-3);
  border: 0;
  border-radius: var(--qlx-radius-sm);
  background: transparent;
  color: var(--qlx-ink-700);
  font-size: var(--qlx-text-base);
  text-align: left;
  cursor: pointer;
  transition: background var(--qlx-duration-fast) var(--qlx-ease-out), color var(--qlx-duration-fast) var(--qlx-ease-out);
}

.admin-nav__item:hover {
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-700);
}

.admin-nav__item.is-active {
  background: var(--qlx-brand-800);
  color: #fff;
}

.admin-nav__item.is-placeholder {
  color: var(--qlx-ink-300);
  cursor: not-allowed;
}

.admin-nav__item.is-placeholder:hover {
  background: transparent;
  color: var(--qlx-ink-400);
}

.admin-nav__item em {
  margin-left: auto;
  padding: 1px 6px;
  border-radius: var(--qlx-radius-full);
  background: var(--qlx-canvas-deep);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  font-style: normal;
}

.admin-layout__side-note {
  margin-top: auto;
  padding: var(--qlx-space-3);
  border-top: 1px dashed var(--qlx-ink-200);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

/* ============ 内容区 ============ */
.admin-layout__content {
  flex: 1;
  min-width: 0;
  padding: var(--qlx-space-4) var(--qlx-space-6) var(--qlx-space-8);
}

.admin-layout__crumb {
  display: flex;
  margin-bottom: var(--qlx-space-4);
  align-items: center;
  gap: var(--qlx-space-2);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-sm);
}

.admin-layout__crumb a {
  color: var(--qlx-ink-500);
}

.admin-layout__crumb a:hover {
  color: var(--qlx-brand-600);
}

.admin-layout__crumb-sep {
  color: var(--qlx-ink-300);
}

.admin-layout__crumb > span:last-child {
  color: var(--qlx-ink-700);
}

@media (max-width: 1080px) {
  .admin-layout__side {
    width: 64px;
    padding: var(--qlx-space-4) var(--qlx-space-2);
  }

  .admin-nav__item span,
  .admin-nav__item em,
  .admin-layout__side-note {
    display: none;
  }

  .admin-nav__item {
    justify-content: center;
  }
}
</style>
