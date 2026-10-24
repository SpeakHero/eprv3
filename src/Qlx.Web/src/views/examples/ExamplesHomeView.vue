<!--
  所属公司：四川淇利馨集团有限公司
  描述：标准示例 · 总览——示例场景导航、公共组件适用场景速查、既有业务视图参考入口
  说明：静态挂载于 /dev/examples（免登录）；卡片点击跳转即演示「菜单点击后挂载」。
-->
<script setup lang="ts">
import AppIcon from '@/components/AppIcon.vue'

/** 示例场景卡片（与 ExamplesLayout 侧边导航同源，路由路径保持一致）。 */
const scenes = [
  {
    path: '/dev/examples/crud-list',
    icon: 'List',
    name: '标准列表页',
    desc: '查询区 + 数据表格 + 分页 + 工具栏按钮 + 新建/编辑弹窗 + 删除确认 + 详情查看 + 批量导入，一页覆盖 90% 列表型业务页面。',
    components: ['QlxPagePanel', 'QlxSearchBar', 'QlxDataTable', 'QlxFormDialog', 'QlxImportDialog', 'useCrudTable'],
  },
  {
    path: '/dev/examples/form',
    icon: 'EditPen',
    name: '表单与弹窗',
    desc: 'QlxFormDialog 全字段类型（input / textarea / select / number / switch）、正则校验、必填校验、服务端 422 字段级错误透出。',
    components: ['QlxFormDialog', 'QlxImportDialog'],
  },
  {
    path: '/dev/examples/tree-detail',
    icon: 'Files',
    name: '左树右详情',
    desc: '树形面板（关键字过滤 / 节点插槽计数）+ 详情描述列表 + 关联数据表格，适用于组织机构、分类档案等主从结构页面。',
    components: ['QlxTreePanel', 'QlxDescriptionList', 'QlxDataTable'],
  },
  {
    path: '/dev/examples/tabs',
    icon: 'CopyDocument',
    name: '多标签页',
    desc: 'QlxTabs 标签条：新增 / 关闭 / 激活 / 固定 / 拖拽排序 / 右键菜单 / 全局刷新总线，适配任意状态管理方案。',
    components: ['QlxTabs', 'useTabManager', 'tabRefreshBus'],
  },
]

/** 既有业务视图参考（真实接口形态，需登录/租户上下文）。 */
const references = [
  { path: '/manage/members', name: '租户 · 成员管理', note: '树 + 列表 + 邀请/导入弹窗' },
  { path: '/manage/roles', name: '租户 · 角色权限', note: '列表 + 权限面板' },
  { path: '/manage/organizations', name: '租户 · 组织机构', note: '左树右详情 + 表单弹窗' },
  { path: '/portal/users', name: '门户 · 用户管理', note: '查询 + 表格 + 行内操作 + 弹窗' },
]
</script>

<template>
  <div class="example-home">
    <!-- 场景卡片：点击后由 ExamplesLayout 的 RouterView 挂载目标示例视图 -->
    <div class="example-home__grid">
      <RouterLink
        v-for="scene in scenes"
        :key="scene.path"
        :to="scene.path"
        class="scene-card"
      >
        <header class="scene-card__head">
          <span class="scene-card__icon">
            <AppIcon :name="scene.icon" :size="18" />
          </span>
          <h2 class="scene-card__name">{{ scene.name }}</h2>
        </header>
        <p class="scene-card__desc">{{ scene.desc }}</p>
        <footer class="scene-card__tags">
          <code v-for="component in scene.components" :key="component" class="scene-card__tag">
            {{ component }}
          </code>
        </footer>
      </RouterLink>
    </div>

    <!-- 使用约定速查 -->
    <section class="example-home__panel">
      <h2 class="example-home__panel-title">使用约定速查</h2>
      <ul class="example-home__rules">
        <li>
          <strong>列表页骨架</strong>：QlxPagePanel（标题 + 工具按钮）→ QlxSearchBar（查询区）
          → QlxDataTable（表格 + 分页），状态逻辑一律由 useCrudTable(fetcher) 驱动，
          fetcher 签名与真实 REST 一致（PagedQuery → PagedResult），mock 可直接平移替换。
        </li>
        <li>
          <strong>弹窗表单</strong>：QlxFormDialog 配置驱动字段（fields 数组），父级监听
          @submit 拿 payload 调接口并自行关闭弹窗；服务端 422 字段错误经 fieldErrors 回透。
        </li>
        <li>
          <strong>操作按钮</strong>：工具栏主操作用 el-button type="primary"（如「新增」），
          行内操作用文字链 .cell-link，危险操作加 .is-danger 并弹 ElMessageBox 确认。
        </li>
        <li>
          <strong>展示组装在前端（B-2）</strong>：树层级、枚举中文名、时间/金额格式化均由前端完成，
          后端只下发扁平原始字段；枚举映射字典维护在 types 或 mock 模块（如 goodsCategoryLabels）。
        </li>
        <li>
          <strong>编码字段（N-1）</strong>：表单不出现编码输入框，编码由后端生成、只读回显；
          表格中编码列用 type: 'code'（点击复制）。
        </li>
      </ul>
    </section>

    <!-- 既有业务视图参考 -->
    <section class="example-home__panel">
      <h2 class="example-home__panel-title">真实业务视图参考（需登录 / 租户上下文）</h2>
      <p class="example-home__panel-desc">
        以下页面即上述公共组件在真实业务中的落地形态，可直接对照源码（src/views 下同名视图）阅读：
      </p>
      <div class="example-home__refs">
        <RouterLink v-for="ref_ in references" :key="ref_.path" :to="ref_.path" class="ref-link">
          <AppIcon name="Link" :size="13" />
          <span class="ref-link__name">{{ ref_.name }}</span>
          <span class="ref-link__note">{{ ref_.note }}</span>
        </RouterLink>
      </div>
    </section>
  </div>
</template>

<style scoped>
.example-home {
  display: flex;
  flex-direction: column;
  gap: var(--qlx-space-5);
}

/* ============ 场景卡片 ============ */
.example-home__grid {
  display: grid;
  gap: var(--qlx-space-4);
  grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
}

.scene-card {
  display: flex;
  flex-direction: column;
  gap: var(--qlx-space-3);
  padding: var(--qlx-space-4) var(--qlx-space-4) var(--qlx-space-3);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
  box-shadow: var(--qlx-shadow-xs);
  transition:
    border-color var(--qlx-duration-fast) var(--qlx-ease-out),
    box-shadow var(--qlx-duration-fast) var(--qlx-ease-out);
}

.scene-card:hover {
  border-color: var(--qlx-brand-300);
  box-shadow: var(--qlx-shadow-sm);
}

.scene-card__head {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
}

.scene-card__icon {
  display: inline-flex;
  width: 34px;
  height: 34px;
  align-items: center;
  justify-content: center;
  border-radius: var(--qlx-radius-sm);
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-700);
}

.scene-card__name {
  color: var(--qlx-brand-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-md);
  font-weight: 600;
}

.scene-card__desc {
  flex: 1;
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-sm);
  line-height: 1.7;
}

.scene-card__tags {
  display: flex;
  flex-wrap: wrap;
  gap: var(--qlx-space-1);
}

.scene-card__tag {
  padding: 1px 6px;
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-xs);
  background: var(--qlx-canvas);
  color: var(--qlx-ink-500);
  font-family: var(--qlx-font-mono);
  font-size: 11px;
}

/* ============ 说明面板 ============ */
.example-home__panel {
  padding: var(--qlx-space-4) var(--qlx-space-5) var(--qlx-space-5);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
  box-shadow: var(--qlx-shadow-xs);
}

.example-home__panel-title {
  margin-bottom: var(--qlx-space-3);
  color: var(--qlx-brand-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-lg);
  font-weight: 600;
}

.example-home__panel-desc {
  margin-bottom: var(--qlx-space-3);
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-sm);
}

.example-home__rules {
  display: flex;
  flex-direction: column;
  gap: var(--qlx-space-2);
}

.example-home__rules li {
  padding-left: var(--qlx-space-3);
  border-left: 3px solid var(--qlx-brand-100);
  color: var(--qlx-ink-700);
  font-size: var(--qlx-text-sm);
  line-height: 1.8;
}

.example-home__rules strong {
  color: var(--qlx-brand-800);
}

.example-home__refs {
  display: flex;
  flex-direction: column;
  gap: var(--qlx-space-2);
}

.ref-link {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  color: var(--qlx-brand-600);
  font-size: var(--qlx-text-sm);
}

.ref-link:hover {
  text-decoration: underline;
}

.ref-link__name {
  font-weight: 500;
}

.ref-link__note {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}
</style>
