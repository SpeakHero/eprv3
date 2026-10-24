<!--
  所属公司：四川淇利馨集团有限公司
  描述：标准示例 · 多标签页——QlxTabs 标签条的标准用法（useTabManager 驱动）
  说明：QlxTabs 为纯通用渲染层，状态全部经 manager 驱动；本页演示：
        新增（去重）/ 关闭 / 激活 / 固定 / 拖拽排序 / 右键菜单 / 全局刷新总线 /
        关闭校验钩子 / 上限策略。内容区以 :key="tab.refreshKey" 重渲染演示刷新。
        详细 API 见 docs/公共组件/多标签页组件-QlxTabs.md。
-->
<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { ElMessage } from 'element-plus'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import { QlxTabs, useTabManager } from '@/components/tabs'
import { onTabRefresh, TAB_REFRESH_ALL } from '@/components/tabs'
import type { TabItem } from '@/components/tabs'

/* ============================================================
 * 1. 管理器：useTabManager（Pinia/Vuex 项目可改用 createTabManager 自行同步）
 *    常用选项：maxTabs 上限 / dedupe 同 id 去重 / closeLastBehavior 最后一个标签行为 /
 *              overflowStrategy 上限策略 / beforeClose 关闭校验钩子。
 * ============================================================ */
const { manager, state } = useTabManager({
  maxTabs: 8,
  dedupe: true,
  closeLastBehavior: 'prevent-close',
  overflowStrategy: 'reject',
  // 关闭校验钩子：含「未保存」标记的标签需确认（可返回 Promise 接异步确认框）
  beforeClose: (tab) => {
    if (tab.meta?.dirty) {
      return window.confirm(`「${tab.title}」有未保存修改，确认关闭？`)
    }
    return true
  },
})

/* ============================================================
 * 2. 打开标签：open(input) 返回 { tab, created }；
 *    dedupe 下重复 id 只激活不新增（created=false），适合「菜单点击打开页面」场景。
 * ============================================================ */
let seq = 0
function openDemoTab(): void {
  seq += 1
  manager.open({ id: `demo-${seq}`, title: `示例页面 ${seq}`, icon: 'Document', route: '/dev/examples/tabs' })
}

function openDirtyTab(): void {
  seq += 1
  manager.open({
    id: `dirty-${seq}`,
    title: `未保存表单 ${seq}`,
    icon: 'EditPen',
    meta: { dirty: true }, // meta 为业务自定义扩展数据：beforeClose 据此拦截
  })
}

function openPinnedTab(): void {
  manager.open({ id: 'pinned-home', title: '固定首页', icon: 'HomeFilled', pinned: true, closable: false })
}

/* ============================================================
 * 3. 全局刷新总线：tabRefreshBus
 *    标签条右键「刷新」与 manager.refresh(id) 都会投递到总线；
 *    任意页面可用 onTabRefresh 订阅（如跨页面联动刷新列表）。
 * ============================================================ */
const refreshLog = ref<string[]>([])
const offRefresh = onTabRefresh(TAB_REFRESH_ALL, (payload) => {
  const label = payload.id === TAB_REFRESH_ALL ? '全部标签' : payload.id
  refreshLog.value.unshift(`[${new Date().toLocaleTimeString()}] 刷新 ${label}（原因：${payload.reason ?? '手动'}）`)
  if (refreshLog.value.length > 8) {
    refreshLog.value.pop()
  }
})
onBeforeUnmount(offRefresh)

/* ============================================================
 * 4. 事件：全部经 QlxTabs 透传（tab-add / tab-close / tab-activate / tab-reorder /
 *    tab-pin / tab-refresh / tab-overflow / tab-evict / add-request / context-action）。
 * ============================================================ */
function onOverflow(payload: { maxTabs: number }): void {
  ElMessage.warning(`已达标签上限 ${payload.maxTabs} 个，请先关闭部分标签`)
}

function onAddRequest(): void {
  // 点击标签条「+」：新增什么由业务决定（此处打开一个演示标签）
  openDemoTab()
}

const activeTab = computed<TabItem | null>(
  () => state.tabs.find((tab) => tab.id === state.activeId) ?? null,
)
</script>

<template>
  <QlxPagePanel
    title="多标签页示例"
    subtitle="新增 / 关闭 / 激活 / 固定 / 拖拽排序 / 右键菜单 / 全局刷新总线 / 关闭校验 / 上限策略"
  >
    <div class="tabs-demo">
      <!-- 触发区 -->
      <div class="tabs-demo__actions">
        <el-button type="primary" size="small" @click="openDemoTab">打开普通标签</el-button>
        <el-button size="small" @click="openDirtyTab">打开含未保存标记的标签</el-button>
        <el-button size="small" @click="openPinnedTab">打开固定标签（去重）</el-button>
        <el-button
          size="small"
          :disabled="!state.activeId"
          @click="state.activeId && manager.refresh(state.activeId, '按钮触发')"
        >
          刷新当前标签
        </el-button>
      </div>

      <!-- 标签条：draggable 拖拽排序；show-add 显示「+」；context-menu 右键菜单（可传函数自定义）；
           tap-active-refresh 移动端再次点击激活标签触发刷新 -->
      <QlxTabs
        :manager="manager"
        draggable
        show-add
        :context-menu="true"
        tap-active-refresh
        empty-text="点击上方按钮打开标签"
        @add-request="onAddRequest"
        @tab-overflow="onOverflow"
      />

      <!-- 内容区：真实项目中这里通常是 <RouterView :key="activeTab.refreshKey">（配 KeepAlive） -->
      <div class="tabs-demo__content">
        <template v-if="activeTab">
          <p class="tabs-demo__content-title">
            当前标签：{{ activeTab.title }}
            <el-tag v-if="activeTab.pinned" size="small" type="warning" effect="light">已固定</el-tag>
            <el-tag v-if="activeTab.meta?.dirty" size="small" type="danger" effect="light">未保存</el-tag>
          </p>
          <p class="tabs-demo__content-meta">
            id：{{ activeTab.id }} · route：{{ activeTab.route ?? '—' }} · refreshKey：{{ activeTab.refreshKey }}
            （每次刷新 +1，内容区以 :key="refreshKey" 重渲染即实现独立刷新）
          </p>
        </template>
        <p v-else class="tabs-demo__content-empty">暂无激活标签</p>
      </div>

      <!-- 刷新总线日志 -->
      <div class="tabs-demo__log">
        <p class="tabs-demo__log-title">全局刷新总线（tabRefreshBus）事件：</p>
        <p v-for="(line, index) in refreshLog" :key="index" class="tabs-demo__log-line">{{ line }}</p>
        <p v-if="refreshLog.length === 0" class="tabs-demo__log-line">暂无事件，试试右键标签 → 刷新。</p>
      </div>
    </div>
  </QlxPagePanel>
</template>

<style scoped>
.tabs-demo {
  display: flex;
  flex-direction: column;
  gap: var(--qlx-space-3);
}

.tabs-demo__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--qlx-space-2);
}

.tabs-demo__content {
  padding: var(--qlx-space-4);
  border: 1px dashed var(--qlx-ink-200);
  border-radius: var(--qlx-radius-sm);
  background: var(--qlx-canvas);
}

.tabs-demo__content-title {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  color: var(--qlx-ink-800);
  font-size: var(--qlx-text-base);
  font-weight: 500;
}

.tabs-demo__content-meta {
  margin-top: var(--qlx-space-1);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

.tabs-demo__content-empty {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-sm);
}

.tabs-demo__log {
  padding: var(--qlx-space-3);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-sm);
  background: var(--qlx-canvas);
}

.tabs-demo__log-title {
  margin-bottom: var(--qlx-space-1);
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
  font-weight: 600;
}

.tabs-demo__log-line {
  color: var(--qlx-ink-500);
  font-family: var(--qlx-font-mono);
  font-size: var(--qlx-text-xs);
  line-height: 1.8;
}
</style>
