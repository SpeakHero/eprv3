<!--
  所属公司：四川淇利馨集团有限公司
  描述：标准示例体系布局——左侧示例导航 + 右侧内容区（RouterView）
  说明：复用与 TenantAdminLayout 相同的「菜单点击 → RouterView 挂载」机制（无顶栏/免登录，
        便于开发验收直达 /dev/examples）；导航项路由需与 router/index.ts 中 /dev/examples
        子路由一一对应。新增示例页时：1) 在下方 navItems 追加菜单项 2) 在路由表注册同名子路由。
-->
<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'

interface ExampleNavItem {
  path: string
  name: string
  icon: string
  hint: string
}

/** 示例导航：菜单点击后经 RouterView 挂载对应示例视图（与业务布局同机制）。 */
const navItems: ExampleNavItem[] = [
  { path: '/dev/examples', name: '示例总览', icon: 'Compass', hint: '索引 · 使用说明' },
  { path: '/dev/examples/crud-list', name: '标准列表页', icon: 'List', hint: '查询 · 表格 · 分页 · 弹窗' },
  { path: '/dev/examples/form', name: '表单与弹窗', icon: 'EditPen', hint: '全字段类型 · 校验' },
  { path: '/dev/examples/tree-detail', name: '左树右详情', icon: 'Files', hint: '树面板 · 描述列表' },
  { path: '/dev/examples/tabs', name: '多标签页', icon: 'CopyDocument', hint: '拖拽 · 右键 · 刷新' },
]

const route = useRoute()
const pageTitle = computed(() => (route.meta.title as string | undefined) ?? '标准示例')

function isActive(item: ExampleNavItem): boolean {
  // 总览为精确匹配，其余允许前缀匹配（便于将来示例页带二级路由）
  if (item.path === '/dev/examples') {
    return route.path === item.path
  }
  return route.path === item.path || route.path.startsWith(`${item.path}/`)
}
</script>

<template>
  <div class="examples">
    <header class="examples__head">
      <div class="examples__head-inner">
        <div>
          <h1 class="examples__title">标准示例页面体系</h1>
          <p class="examples__desc">
            覆盖表单 / 表格 / 查询区 / 操作按钮 / 弹窗 / 分页等通用业务场景的标准用法，
            全部基于 src/components/crud 等公共组件与 mock 数据，可直接参考复用。
          </p>
        </div>
        <div class="examples__head-links">
          <RouterLink class="examples__back" to="/workbench">返回工作台</RouterLink>
          <RouterLink class="examples__back" to="/dev/components">阶段二组件演示</RouterLink>
        </div>
      </div>
    </header>

    <div class="examples__body">
      <aside class="examples__side">
        <p class="examples__label">示例场景</p>
        <nav class="examples-nav" aria-label="示例导航">
          <RouterLink
            v-for="item in navItems"
            :key="item.path"
            :to="item.path"
            class="examples-nav__item"
            :class="{ 'is-active': isActive(item) }"
          >
            <span class="examples-nav__icon">
              <AppIcon :name="item.icon" :size="16" />
            </span>
            <span class="examples-nav__text">
              <strong>{{ item.name }}</strong>
              <em>{{ item.hint }}</em>
            </span>
          </RouterLink>
        </nav>

        <section class="examples__note">
          <h2>挂载机制</h2>
          <p>本布局复用与 TenantAdminLayout 相同的规范：</p>
          <ul>
            <li>静态挂载：路由表直接注册（/dev/examples/*，免登录直达）</li>
            <li>菜单挂载：点击左侧菜单 → RouterView 挂载对应视图组件</li>
          </ul>
        </section>
      </aside>

      <main class="examples__main">
        <nav class="examples__crumb" aria-label="面包屑">
          <RouterLink to="/dev/examples">标准示例</RouterLink>
          <span class="examples__crumb-sep">/</span>
          <span class="examples__crumb-current">{{ pageTitle }}</span>
        </nav>
        <RouterView />
      </main>
    </div>
  </div>
</template>

<style scoped>
.examples {
  min-height: 100%;
  background: var(--qlx-canvas);
}

/* ============ 页头 ============ */
.examples__head {
  border-bottom: 1px solid var(--qlx-line);
  background: var(--qlx-surface);
}

.examples__head-inner {
  display: flex;
  max-width: var(--qlx-content-max);
  margin: 0 auto;
  align-items: flex-end;
  justify-content: space-between;
  gap: var(--qlx-space-4);
  padding: var(--qlx-space-5) var(--qlx-space-5) var(--qlx-space-4);
}

.examples__title {
  color: var(--qlx-brand-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-xl);
  font-weight: 600;
  letter-spacing: 0.04em;
}

.examples__desc {
  margin-top: var(--qlx-space-1);
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-sm);
  line-height: 1.7;
}

.examples__head-links {
  display: flex;
  flex: 0 0 auto;
  gap: var(--qlx-space-3);
}

.examples__back {
  color: var(--qlx-brand-600);
  font-size: var(--qlx-text-sm);
}

.examples__back:hover {
  text-decoration: underline;
}

/* ============ 主体 ============ */
.examples__body {
  display: flex;
  max-width: var(--qlx-content-max);
  margin: 0 auto;
  align-items: stretch;
}

.examples__side {
  display: flex;
  width: 216px;
  flex: 0 0 auto;
  flex-direction: column;
  padding: var(--qlx-space-4) var(--qlx-space-3) var(--qlx-space-6);
  border-right: 1px solid var(--qlx-line);
  background: var(--qlx-surface);
}

.examples__label {
  padding: 0 var(--qlx-space-3) var(--qlx-space-2);
  color: var(--qlx-ink-400);
  font-family: var(--qlx-font-mono);
  font-size: var(--qlx-text-xs);
  letter-spacing: 0.14em;
}

.examples-nav {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.examples-nav__item {
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

.examples-nav__item:hover {
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-700);
}

.examples-nav__item.is-active {
  border-left-color: var(--qlx-brand-700);
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-800);
}

.examples-nav__icon {
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

.examples-nav__item.is-active .examples-nav__icon {
  border-color: var(--qlx-brand-100);
  background: var(--qlx-brand-700);
  color: #fff;
}

.examples-nav__text {
  display: flex;
  min-width: 0;
  flex-direction: column;
  line-height: 1.35;
}

.examples-nav__text strong {
  font-size: var(--qlx-text-base);
  font-weight: 500;
}

.examples-nav__text em {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  font-style: normal;
}

.examples-nav__item.is-active .examples-nav__text em {
  color: var(--qlx-brand-600);
}

/* ============ 挂载机制说明 ============ */
.examples__note {
  margin-top: auto;
  padding: var(--qlx-space-3);
  border-top: 1px dashed var(--qlx-ink-200);
}

.examples__note h2 {
  margin-bottom: var(--qlx-space-2);
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
  font-weight: 600;
  letter-spacing: 0.08em;
}

.examples__note p,
.examples__note li {
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

.examples__note ul {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-top: var(--qlx-space-1);
}

/* ============ 内容区 ============ */
.examples__main {
  min-width: 0;
  flex: 1;
  padding: var(--qlx-space-4) var(--qlx-space-5) var(--qlx-space-10);
}

.examples__crumb {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  margin-bottom: var(--qlx-space-3);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.examples__crumb a:hover {
  color: var(--qlx-brand-600);
}

.examples__crumb-current {
  color: var(--qlx-ink-600);
}

/* ============ 响应式（与 TenantAdminLayout 断点一致：720px） ============ */
@media (max-width: 720px) {
  .examples__head-inner {
    flex-direction: column;
    align-items: flex-start;
  }

  .examples__body {
    flex-direction: column;
  }

  .examples__side {
    width: 100%;
    border-right: 0;
    border-bottom: 1px solid var(--qlx-line);
  }

  .examples-nav {
    flex-direction: row;
    overflow-x: auto;
  }

  .examples-nav__item {
    flex: 0 0 auto;
    border-bottom: 3px solid transparent;
    border-left: 0;
    border-radius: var(--qlx-radius-sm) var(--qlx-radius-sm) 0 0;
  }

  .examples-nav__item.is-active {
    border-bottom-color: var(--qlx-brand-700);
  }

  .examples-nav__text em {
    display: none;
  }

  .examples__note {
    display: none;
  }

  .examples__main {
    padding: var(--qlx-space-3) var(--qlx-space-3) var(--qlx-space-8);
  }
}
</style>
