<!--
  所属公司：四川淇利馨集团有限公司
  描述：工作台顶栏——品牌 / 一级导航（悬停展开全量菜单，业务页以原生新标签打开）/ 用户与企业操作
  说明：菜单项均为 target="_blank" 的原生标签页，不使用路由内组件式标签
-->
<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import BrandMark from '@/components/BrandMark.vue'
import { navModules, topMenus } from '@/data/navigation'
import type { NavModule } from '@/data/navigation'
import { useAuthStore } from '@/stores/auth'
import { memberRoleLabel } from '@/types/auth'

const auth = useAuthStore()
const router = useRouter()

/** 当前展开的一级菜单编码。 */
const activeMenu = ref('')

/** 模块是否已授权（许可信息未下发时按全量展示）。 */
function licensed(moduleCode: string): boolean {
  return auth.licensedModules.length === 0 || auth.isModuleLicensed(moduleCode)
}

/** 仅展示含已授权模块的一级菜单；工作台恒显示；门户管理模式隐藏业务菜单。 */
const visibleMenus = computed(() => {
  if (auth.portalAdminMode) {
    return topMenus.filter((menu) => menu.code === 'workbench')
  }
  return topMenus.filter(
    (menu) => menu.code === 'workbench' || menu.modules.some((code) => licensed(code)),
  )
})

/** 当前展开菜单下的模块目录。 */
const panelModules = computed<NavModule[]>(() => {
  const menu = topMenus.find((item) => item.code === activeMenu.value)
  if (!menu) {
    return []
  }
  return navModules.filter(
    (module) => menu.modules.includes(module.code) && licensed(module.code),
  )
})

const initial = computed(() => (auth.displayName || auth.loginAccount || '员').slice(0, 1))
const roleLabel = computed(() => memberRoleLabel(auth.user?.memberRole ?? auth.currentTenant?.memberRole))

function open(menuCode: string): void {
  if (menuCode === 'workbench') {
    activeMenu.value = ''
    return
  }
  activeMenu.value = menuCode
}

function toggle(menuCode: string): void {
  activeMenu.value = activeMenu.value === menuCode ? '' : menuCode
}

function close(): void {
  activeMenu.value = ''
}

function notifyBuilding(): void {
  ElMessage.info('信息中心将在后续阶段接入')
}

async function onCommand(command: string): Promise<void> {
  close()
  if (command === 'manage') {
    await router.push('/manage/organizations')
    return
  }
  if (command === 'tenant') {
    await router.push('/select-tenant')
    return
  }
  if (command === 'logout') {
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
}
</script>

<template>
  <header class="site-header" @mouseleave="close" @keydown.esc="close">
    <div class="site-header__bar">
      <RouterLink to="/workbench" class="site-header__brand" aria-label="返回工作台">
        <BrandMark theme="dark" compact />
      </RouterLink>

      <nav class="site-header__nav" aria-label="主导航">
        <div
          v-for="menu in visibleMenus"
          :key="menu.code"
          class="nav-item"
          :class="{ 'is-active': activeMenu === menu.code }"
        >
          <RouterLink
            v-if="menu.code === 'workbench'"
            to="/workbench"
            class="nav-item__link"
            @mouseenter="open(menu.code)"
          >
            工作台
          </RouterLink>
          <button
            v-else
            type="button"
            class="nav-item__link"
            :aria-expanded="activeMenu === menu.code"
            @mouseenter="open(menu.code)"
            @focus="open(menu.code)"
            @click="toggle(menu.code)"
          >
            {{ menu.name }}
            <AppIcon name="ArrowDown" :size="12" />
          </button>
        </div>
      </nav>

      <div class="site-header__actions">
        <button type="button" class="icon-btn" aria-label="信息中心" @click="notifyBuilding">
          <AppIcon name="Bell" :size="18" />
          <span class="icon-btn__dot" aria-hidden="true"></span>
        </button>

        <el-dropdown trigger="click" @command="onCommand">
          <button type="button" class="user-btn">
            <span class="user-btn__avatar" aria-hidden="true">{{ initial }}</span>
            <span class="user-btn__meta">
              <strong>{{ auth.displayName || auth.loginAccount }}</strong>
              <span>{{ auth.tenantName }} · {{ roleLabel }}</span>
            </span>
            <AppIcon name="ArrowDown" :size="12" />
          </button>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item disabled>
                <span class="user-menu__account">{{ auth.loginAccount }}</span>
              </el-dropdown-item>
              <el-dropdown-item v-if="auth.tenantToken && !auth.portalAdminMode" command="manage">
                <AppIcon name="Setting" :size="14" />
                <span class="user-menu__text">企业管理</span>
              </el-dropdown-item>
              <el-dropdown-item command="tenant">切换企业</el-dropdown-item>
              <el-dropdown-item command="logout" divided>
                <AppIcon name="SwitchButton" :size="14" />
                <span class="user-menu__text">退出登录</span>
              </el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
      </div>
    </div>

    <Transition name="mega">
      <div v-if="activeMenu" class="site-header__mega" @mouseenter="open(activeMenu)">
        <div class="qlx-container mega">
          <p class="mega__hint">
            <AppIcon name="ArrowRight" :size="13" />
            功能将在新标签页打开，可与工作台并行办理
          </p>
          <div class="mega__grid">
            <section v-for="module in panelModules" :key="module.code" class="mega__module">
              <h3 class="mega__module-name">{{ module.name }}</h3>
              <div v-for="group in module.groups" :key="group.name" class="mega__group">
                <p class="mega__group-name">{{ group.name }}</p>
                <div class="mega__links">
                  <a
                    v-for="item in group.items"
                    :key="item.code"
                    class="mega__link"
                    :href="`/module/${module.code}/${item.code}`"
                    target="_blank"
                    rel="noopener"
                    @click="close"
                  >
                    <AppIcon :name="item.icon" :size="14" />
                    <span>{{ item.name }}</span>
                  </a>
                </div>
              </div>
            </section>
          </div>
        </div>
      </div>
    </Transition>
  </header>
</template>

<style scoped>
.site-header {
  position: sticky;
  top: 0;
  z-index: var(--qlx-z-header);
}

.site-header__bar {
  display: flex;
  height: var(--qlx-header-height);
  align-items: center;
  gap: var(--qlx-space-6);
  padding: 0 var(--qlx-space-6);
  background: linear-gradient(90deg, var(--qlx-brand-900), var(--qlx-brand-800) 62%, var(--qlx-brand-700));
  border-bottom: 2px solid var(--qlx-gold-500);
}

.site-header__brand {
  flex: 0 0 auto;
}

.site-header__nav {
  display: flex;
  height: 100%;
  min-width: 0;
  align-items: stretch;
  gap: var(--qlx-space-1);
}

.nav-item {
  position: relative;
  display: flex;
  align-items: stretch;
}

.nav-item__link {
  display: inline-flex;
  height: 100%;
  align-items: center;
  gap: 4px;
  padding: 0 var(--qlx-space-4);
  border: 0;
  border-bottom: 2px solid transparent;
  background: transparent;
  color: rgb(255 255 255 / 82%);
  font-size: var(--qlx-text-base);
  letter-spacing: 0.04em;
  transition: color var(--qlx-duration-fast) var(--qlx-ease-out),
    background var(--qlx-duration-fast) var(--qlx-ease-out);
}

.nav-item__link:hover {
  background: rgb(255 255 255 / 8%);
  color: #fff;
}

.nav-item.is-active .nav-item__link {
  border-bottom-color: var(--qlx-gold-400);
  color: #fff;
  background: rgb(255 255 255 / 10%);
}

.site-header__actions {
  display: flex;
  margin-left: auto;
  align-items: center;
  gap: var(--qlx-space-2);
}

.icon-btn {
  position: relative;
  display: inline-flex;
  width: 34px;
  height: 34px;
  align-items: center;
  justify-content: center;
  border: 1px solid rgb(255 255 255 / 18%);
  border-radius: var(--qlx-radius-sm);
  background: transparent;
  color: rgb(255 255 255 / 86%);
  transition: background var(--qlx-duration-fast) var(--qlx-ease-out),
    color var(--qlx-duration-fast) var(--qlx-ease-out);
}

.icon-btn:hover {
  background: rgb(255 255 255 / 12%);
  color: #fff;
}

.icon-btn__dot {
  position: absolute;
  top: 7px;
  right: 7px;
  width: 6px;
  height: 6px;
  border-radius: var(--qlx-radius-full);
  background: var(--qlx-seal);
  box-shadow: 0 0 0 2px var(--qlx-brand-800);
}

.user-btn {
  display: inline-flex;
  height: 40px;
  align-items: center;
  gap: var(--qlx-space-2);
  padding: 0 var(--qlx-space-3);
  border: 1px solid rgb(255 255 255 / 18%);
  border-radius: var(--qlx-radius-sm);
  background: transparent;
  color: #fff;
  text-align: left;
  transition: background var(--qlx-duration-fast) var(--qlx-ease-out);
}

.user-btn:hover {
  background: rgb(255 255 255 / 12%);
}

.user-btn__avatar {
  display: inline-flex;
  width: 26px;
  height: 26px;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  border-radius: var(--qlx-radius-xs);
  background: var(--qlx-gold-500);
  color: var(--qlx-brand-900);
  font-family: var(--qlx-font-serif);
  font-size: 14px;
  font-weight: 600;
}

.user-btn__meta {
  display: flex;
  flex-direction: column;
  gap: 1px;
  line-height: 1.25;
}

.user-btn__meta strong {
  font-size: var(--qlx-text-sm);
  font-weight: 500;
}

.user-btn__meta span {
  color: rgb(255 255 255 / 62%);
  font-size: var(--qlx-text-xs);
}

.user-menu__account {
  color: var(--qlx-ink-400);
  font-family: var(--qlx-font-mono);
  font-size: var(--qlx-text-xs);
}

.user-menu__text {
  margin-left: 6px;
}

/* ============ 悬停全量菜单 ============ */
.site-header__mega {
  position: absolute;
  top: 100%;
  right: 0;
  left: 0;
  max-height: calc(100vh - var(--qlx-header-height));
  overflow: auto;
  border-bottom: 1px solid var(--qlx-line);
  background: var(--qlx-surface);
  box-shadow: var(--qlx-shadow-lg);
}

.mega {
  padding-top: var(--qlx-space-5);
  padding-bottom: var(--qlx-space-6);
}

.mega__hint {
  display: inline-flex;
  margin-bottom: var(--qlx-space-4);
  align-items: center;
  gap: 6px;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.mega__grid {
  display: grid;
  gap: var(--qlx-space-6) var(--qlx-space-8);
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
}

.mega__module {
  padding-left: var(--qlx-space-4);
  border-left: 2px solid var(--qlx-brand-100);
}

.mega__module-name {
  margin-bottom: var(--qlx-space-3);
  color: var(--qlx-brand-800);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-md);
  font-weight: 600;
  letter-spacing: 0.06em;
}

.mega__group + .mega__group {
  margin-top: var(--qlx-space-3);
}

.mega__group-name {
  margin-bottom: var(--qlx-space-1);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  letter-spacing: 0.08em;
}

.mega__links {
  display: grid;
  gap: 2px;
  grid-template-columns: repeat(2, minmax(0, 1fr));
}

.mega__link {
  display: inline-flex;
  min-width: 0;
  align-items: center;
  gap: 6px;
  padding: 5px 6px;
  border-radius: var(--qlx-radius-xs);
  color: var(--qlx-ink-700);
  font-size: var(--qlx-text-sm);
  transition: background var(--qlx-duration-fast) var(--qlx-ease-out),
    color var(--qlx-duration-fast) var(--qlx-ease-out);
}

.mega__link span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mega__link:hover {
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-700);
}

/* 展开动效 */
.mega-enter-active,
.mega-leave-active {
  transition: opacity var(--qlx-duration-fast) var(--qlx-ease-out),
    transform var(--qlx-duration-fast) var(--qlx-ease-out);
}

.mega-enter-from,
.mega-leave-to {
  opacity: 0;
  transform: translateY(-4px);
}

@media (max-width: 1080px) {
  .site-header__bar {
    gap: var(--qlx-space-3);
    padding: 0 var(--qlx-space-4);
  }

  .nav-item__link {
    padding: 0 var(--qlx-space-2);
    font-size: var(--qlx-text-sm);
  }

  .user-btn__meta {
    display: none;
  }
}
</style>
