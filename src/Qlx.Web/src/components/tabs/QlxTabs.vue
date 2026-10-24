<!--
  所属公司：四川淇利馨集团有限公司
  描述：QlxTabs 多标签页条——纯通用渲染层，无业务侵入。
        状态：全部经由 props.manager（createTabManager）驱动；
        视图：默认样式遵循项目设计令牌，可通过插槽与 CSS 变量整体换肤；
        交互：新增 / 关闭 / 激活 / 固定 / 拖拽排序（跨区域、实时反馈、边界修正）
              / 右键菜单（可整体替换）/ 全局刷新（tab-refresh 事件 + tabRefreshBus）。
-->
<script setup lang="ts">
import {
  computed,
  onBeforeUnmount,
  reactive,
  ref,
  toRef,
  type CSSProperties,
} from 'vue'
import type { TabManager } from './tabManager'
import { emitTabRefresh } from './tabRefreshBus'
import type {
  TabContextAction,
  TabContextMenuItem,
  TabItem,
  TabManagerEventMap,
  TabManagerEventName,
} from './types'
import { useTabDrag } from './useTabDrag'

interface Props {
  /** 标签管理器实例（createTabManager / useTabManager 返回值中的 manager） */
  manager: TabManager
  /** 是否启用拖拽排序，默认 true */
  draggable?: boolean
  /** 是否显示「新增」按钮，默认 false */
  showAdd?: boolean
  /**
   * 右键菜单：false 关闭；true 使用默认菜单；
   * 函数返回自定义菜单项（仍可复用默认渲染）
   */
  contextMenu?: boolean | ((tab: TabItem) => TabContextMenuItem[])
  /** 空态文案 */
  emptyText?: string
  /** 再次点击已激活标签时触发刷新（移动端常用手势），默认 false */
  tapActiveRefresh?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  draggable: true,
  showAdd: false,
  contextMenu: true,
  emptyText: '暂无打开的标签页',
  tapActiveRefresh: false,
})

const emit = defineEmits<{
  (e: 'tab-add', payload: TabManagerEventMap['tab-add']): void
  (e: 'tab-close', payload: TabManagerEventMap['tab-close']): void
  (e: 'tab-activate', payload: TabManagerEventMap['tab-activate']): void
  (e: 'tab-reorder', payload: TabManagerEventMap['tab-reorder']): void
  (e: 'tab-pin', payload: TabManagerEventMap['tab-pin']): void
  (e: 'tab-refresh', payload: TabManagerEventMap['tab-refresh']): void
  (e: 'tab-overflow', payload: TabManagerEventMap['tab-overflow']): void
  (e: 'tab-evict', payload: TabManagerEventMap['tab-evict']): void
  /** 点击「新增」按钮（具体新增什么由业务决定） */
  (e: 'add-request'): void
  /** 右键菜单动作被选中（默认菜单与自定义菜单均会触发） */
  (e: 'context-action', payload: { action: TabContextAction; tab: TabItem }): void
}>()

/* ============ 状态同步：manager → 响应式镜像 ============ */
const view = reactive<{ tabs: TabItem[]; activeId: string | null }>({
  tabs: [],
  activeId: null,
})

const offs: Array<() => void> = []
offs.push(
  props.manager.on('change', ({ tabs, activeId }) => {
    view.tabs = [...tabs]
    view.activeId = activeId
  }),
)

// 管理器事件转发为组件事件；刷新事件同时投递到全局刷新总线
const forwardEvents: TabManagerEventName[] = [
  'tab-add',
  'tab-close',
  'tab-activate',
  'tab-reorder',
  'tab-pin',
  'tab-overflow',
  'tab-evict',
]
const emitRaw = emit as (event: string, payload: unknown) => void
function forward<K extends TabManagerEventName>(event: K): void {
  offs.push(
    props.manager.on(event, (payload: TabManagerEventMap[K]) => {
      emitRaw(event, payload)
    }),
  )
}
forwardEvents.forEach((event) => forward(event))
offs.push(
  props.manager.on('tab-refresh', (payload) => {
    emitTabRefresh(payload.tab.id, {
      refreshKey: payload.refreshKey,
      reason: payload.reason,
    })
    emit('tab-refresh', payload)
  }),
)
onBeforeUnmount(() => offs.forEach((off) => off()))

/* ============ 拖拽排序 ============ */
const listRef = ref<HTMLElement | null>(null)

const drag = useTabDrag({
  listRef,
  enabled: toRef(props, 'draggable'),
  onCommit: (draggedId, result) => {
    props.manager.move(draggedId, result.index)
  },
  // 触屏长按松手（未横移）→ 打开右键菜单（触屏 contextmenu 事件不可靠的替代手势）
  onPressHoldRelease: (id, position) => {
    const tab = props.manager.getById(id)
    if (tab) openMenuAt(position.x, position.y, tab)
  },
})

function handlePointerDown(event: PointerEvent, tab: TabItem): void {
  drag.handlePointerDown(event, tab.id)
}

/** 拖拽中的位移样式：被拖拽项跟随指针，落点之后的兄弟项让位 */
function tabStyle(tab: TabItem, index: number): CSSProperties | undefined {
  const { draggingId, fromIndex, dropIndex, dragWidth, offsetX } = drag.state
  if (!draggingId) return undefined
  if (tab.id === draggingId) {
    return { transform: `translateX(${offsetX}px)`, zIndex: 5, transition: 'none' }
  }
  const restIndex = index > fromIndex ? index - 1 : index
  if (restIndex >= dropIndex && dropIndex !== -1) {
    return { transform: `translateX(${dragWidth}px)` }
  }
  return undefined
}

function tabClass(tab: TabItem): Record<string, boolean> {
  return {
    'is-active': tab.id === view.activeId,
    'is-pinned': Boolean(tab.pinned),
    'is-dragging': drag.state.draggingId === tab.id,
    'will-pin': drag.state.draggingId === tab.id && drag.state.dropPinned && !tab.pinned,
    'will-unpin': drag.state.draggingId === tab.id && !drag.state.dropPinned && Boolean(tab.pinned),
  }
}

/* ============ 标签操作 ============ */
function handleTabClick(tab: TabItem): void {
  // 拖拽结束时 pointerup 与 click 同源，用提交时间戳抑制误触激活切换
  if (drag.state.draggingId || Date.now() - drag.state.lastCommitAt < 250) return
  if (tab.id === view.activeId && props.tapActiveRefresh) {
    props.manager.refresh(tab.id, 'tap-active')
    return
  }
  props.manager.activate(tab.id)
}

/** 激活后将标签滚入可视区（移动端横向滚动容器下保证激活项可见） */
function scrollTabIntoView(id: string): void {
  const list = listRef.value
  if (!list) return
  const node = list.querySelector<HTMLElement>(`[data-tab-id="${CSS.escape(id)}"]`)
  node?.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' })
}

// 激活变化时自动滚入视口
offs.push(
  props.manager.on('tab-activate', ({ tab }) => {
    if (tab) requestAnimationFrame(() => scrollTabIntoView(tab.id))
  }),
)

function handleClose(tab: TabItem): void {
  void props.manager.close(tab.id)
}

/* ============ 右键菜单 ============ */
const menu = reactive<{ visible: boolean; x: number; y: number; tab: TabItem | null }>({
  visible: false,
  x: 0,
  y: 0,
  tab: null,
})

function buildDefaultItems(tab: TabItem): TabContextMenuItem[] {
  const pinned = Boolean(tab.pinned)
  const closable = !pinned && tab.closable !== false
  const onlyOne = view.tabs.length === 1
  return [
    { action: 'refresh', label: '刷新' },
    { action: 'toggle-pin', label: pinned ? '取消固定' : '固定' },
    { action: 'close', label: '关闭', disabled: !closable || onlyOne, divided: true },
    { action: 'close-others', label: '关闭其他', disabled: view.tabs.length <= 1 },
    { action: 'close-all', label: '关闭全部', danger: true, disabled: view.tabs.every((t) => t.pinned) },
  ]
}

const menuItems = computed<TabContextMenuItem[]>(() => {
  if (!menu.tab) return []
  if (typeof props.contextMenu === 'function') {
    return props.contextMenu(menu.tab)
  }
  return buildDefaultItems(menu.tab)
})

/** 在指定视口坐标打开菜单（自动做视口边界修正；桌面右键与触屏长按共用） */
function openMenuAt(x: number, y: number, tab: TabItem): void {
  if (!props.contextMenu) return
  menu.tab = tab
  menu.visible = true
  // 视口边界修正，防止菜单溢出（菜单项高度与 CSS 断点保持一致）
  const menuWidth = 148
  const itemHeight = window.innerWidth <= 720 ? 44 : 32
  const menuHeight = menuItems.value.length * itemHeight + 8
  menu.x = Math.max(8, Math.min(x, window.innerWidth - menuWidth - 8))
  menu.y = Math.max(8, Math.min(y, window.innerHeight - menuHeight - 8))
}

function handleContextMenu(event: MouseEvent, tab: TabItem): void {
  if (!props.contextMenu) return
  event.preventDefault()
  openMenuAt(event.clientX, event.clientY, tab)
}

function closeMenu(): void {
  menu.visible = false
  menu.tab = null
}

async function handleMenuSelect(item: TabContextMenuItem): Promise<void> {
  const tab = menu.tab
  if (!tab || item.disabled) return
  closeMenu()
  emit('context-action', { action: item.action, tab })
  switch (item.action) {
    case 'refresh':
      props.manager.refresh(tab.id, 'context-menu')
      break
    case 'toggle-pin':
      props.manager.setPinned(tab.id, !tab.pinned)
      break
    case 'close':
      await props.manager.close(tab.id)
      break
    case 'close-others':
      await props.manager.closeOthers(tab.id)
      break
    case 'close-all':
      await props.manager.closeAll()
      break
  }
}

// 点击其他区域 / 滚动 / ESC 关闭菜单
function onGlobalPointerDown(): void {
  if (menu.visible) closeMenu()
}
function onGlobalKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && menu.visible) closeMenu()
}
window.addEventListener('pointerdown', onGlobalPointerDown, { capture: true })
window.addEventListener('keydown', onGlobalKeydown)
onBeforeUnmount(() => {
  window.removeEventListener('pointerdown', onGlobalPointerDown, { capture: true })
  window.removeEventListener('keydown', onGlobalKeydown)
})
</script>

<template>
  <div class="qlx-tabs" :class="{ 'is-drag-active': drag.state.draggingId !== null }">
    <div ref="listRef" class="qlx-tabs__list" role="tablist" :aria-label="'页面标签'">
      <template v-for="(tab, index) in view.tabs" :key="tab.id">
        <!-- 固定区与普通区分隔线 -->
        <span
          v-if="index > 0 && view.tabs[index - 1].pinned && !tab.pinned"
          class="qlx-tabs__zone-divider"
          aria-hidden="true"
        />
        <div
          class="qlx-tabs__item"
          :class="tabClass(tab)"
          :style="tabStyle(tab, index)"
          role="tab"
          :aria-selected="tab.id === view.activeId"
          :data-tab-id="tab.id"
          :data-tab-pinned="Boolean(tab.pinned)"
          :title="tab.title"
          @click="handleTabClick(tab)"
          @pointerdown="handlePointerDown($event, tab)"
          @contextmenu="handleContextMenu($event, tab)"
        >
          <!-- 完全自定义标签渲染 -->
          <slot
            name="tab"
            :tab="tab"
            :active="tab.id === view.activeId"
            :index="index"
            :dragging="drag.state.draggingId === tab.id"
          >
            <span v-if="tab.pinned" class="qlx-tabs__pin" aria-label="已固定">
              <svg viewBox="0 0 16 16" width="11" height="11" fill="currentColor">
                <path
                  d="M9.6 1.4a1 1 0 0 0-1.5.2L6.6 3.7 4 5.3a1 1 0 0 0-.3 1.5l.9.9-2 2H2v1.4l3 3H6.4v-.6l2-2 .9.9a1 1 0 0 0 1.5-.3l1.6-2.7 2-1.5a1 1 0 0 0 .2-1.5l-5-5ZM8 9.9 6.1 8l3.2-3.2L11.2 6.7 8 9.9Z"
                />
              </svg>
            </span>
            <slot name="label" :tab="tab" :active="tab.id === view.activeId">
              <span class="qlx-tabs__title">{{ tab.title }}</span>
            </slot>
            <button
              v-if="!tab.pinned && tab.closable !== false"
              type="button"
              class="qlx-tabs__close"
              :aria-label="`关闭 ${tab.title}`"
              @click.stop="handleClose(tab)"
              @pointerdown.stop
            >
              <svg viewBox="0 0 16 16" width="10" height="10" fill="none" stroke="currentColor" stroke-width="1.6">
                <path d="M3 3l10 10M13 3L3 13" />
              </svg>
            </button>
          </slot>
        </div>
      </template>

      <p v-if="view.tabs.length === 0" class="qlx-tabs__empty">
        <slot name="empty">{{ emptyText }}</slot>
      </p>
    </div>

    <button
      v-if="showAdd"
      type="button"
      class="qlx-tabs__add"
      aria-label="新增标签页"
      @click="emit('add-request')"
    >
      <slot name="add">
        <svg viewBox="0 0 16 16" width="12" height="12" fill="none" stroke="currentColor" stroke-width="1.6">
          <path d="M8 3v10M3 8h10" />
        </svg>
      </slot>
    </button>

    <!-- 右键菜单（slot 可整体替换；默认实现走 Teleport 避免容器裁剪） -->
    <Teleport to="body">
      <div
        v-if="menu.visible && menu.tab"
        class="qlx-tabs-menu"
        :style="{ left: `${menu.x}px`, top: `${menu.y}px` }"
        role="menu"
        @pointerdown.stop
      >
        <slot
          name="context-menu"
          :tab="menu.tab"
          :items="menuItems"
          :select="handleMenuSelect"
          :close="closeMenu"
        >
          <template v-for="item in menuItems" :key="item.action">
            <div v-if="item.divided" class="qlx-tabs-menu__divider" />
            <button
              type="button"
              class="qlx-tabs-menu__item"
              :class="{ 'is-danger': item.danger }"
              :disabled="item.disabled"
              role="menuitem"
              @click="handleMenuSelect(item)"
            >
              {{ item.label }}
            </button>
          </template>
        </slot>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
/* ============ 容器 ============ */
.qlx-tabs {
  display: flex;
  height: var(--qlx-tabbar-height, 38px);
  align-items: stretch;
  border-bottom: 1px solid var(--qlx-line, #dfe6ee);
  background: var(--qlx-surface, #fff);
}

.qlx-tabs__list {
  display: flex;
  min-width: 0;
  flex: 1;
  align-items: flex-end;
  gap: 2px;
  overflow-x: auto;
  padding: 0 var(--qlx-space-2, 8px);
  scrollbar-width: thin;
  -webkit-overflow-scrolling: touch;
  overscroll-behavior-x: contain;
}

.qlx-tabs__list::-webkit-scrollbar {
  height: 3px;
}

.qlx-tabs__list::-webkit-scrollbar-thumb {
  background: var(--qlx-ink-200, #cfd7e1);
  border-radius: 999px;
}

.qlx-tabs__zone-divider {
  width: 1px;
  height: 18px;
  flex: 0 0 auto;
  align-self: center;
  margin: 0 4px;
  background: var(--qlx-ink-200, #cfd7e1);
}

/* ============ 标签项 ============ */
.qlx-tabs__item {
  position: relative;
  display: flex;
  height: 30px;
  min-width: 0;
  max-width: 180px;
  flex: 0 0 auto;
  align-items: center;
  gap: 6px;
  padding: 0 10px;
  border: 1px solid transparent;
  border-bottom: none;
  border-radius: var(--qlx-radius-sm, 4px) var(--qlx-radius-sm, 4px) 0 0;
  color: var(--qlx-ink-600, #4c596b);
  cursor: pointer;
  font-size: var(--qlx-text-sm, 13px);
  line-height: 1;
  user-select: none;
  touch-action: pan-x pan-y;
  transition:
    background var(--qlx-duration-fast, 140ms) var(--qlx-ease-out),
    color var(--qlx-duration-fast, 140ms) var(--qlx-ease-out),
    transform var(--qlx-duration-fast, 140ms) var(--qlx-ease-out);
}

.qlx-tabs__item:hover {
  background: var(--qlx-brand-50, #f0f5fa);
  color: var(--qlx-brand-700, #0e4478);
}

.qlx-tabs__item.is-active {
  border-color: var(--qlx-line, #dfe6ee);
  background: var(--qlx-canvas, #f4f6f9);
  color: var(--qlx-brand-800, #0b3868);
  font-weight: 500;
}

/* 激活指示条（政务风：顶部鎏金细线） */
.qlx-tabs__item.is-active::before {
  position: absolute;
  top: 0;
  right: 0;
  left: 0;
  height: 2px;
  background: var(--qlx-gold-500, #bf9b30);
  content: '';
}

.qlx-tabs__item.is-pinned {
  padding: 0 8px;
}

.qlx-tabs__item.is-dragging {
  border-color: var(--qlx-brand-300, #7ba6cf);
  background: var(--qlx-brand-50, #f0f5fa);
  box-shadow: var(--qlx-shadow-md, 0 8px 24px rgb(20 27 38 / 10%));
  cursor: grabbing;
  opacity: 0.92;
  touch-action: none;
}

/* 跨区域拖拽实时反馈：拖入固定区 / 拖出固定区 */
.qlx-tabs__item.will-pin {
  outline: 1px dashed var(--qlx-gold-500, #bf9b30);
  outline-offset: -3px;
}

.qlx-tabs__item.will-unpin {
  outline: 1px dashed var(--qlx-ink-300, #b3bcc9);
  outline-offset: -3px;
}

.qlx-tabs__pin {
  display: inline-flex;
  flex: 0 0 auto;
  color: var(--qlx-gold-600, #9c7a1c);
}

.qlx-tabs__title {
  overflow: hidden;
  min-width: 0;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.qlx-tabs__close {
  display: inline-flex;
  width: 16px;
  height: 16px;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  border: 0;
  border-radius: var(--qlx-radius-xs, 2px);
  background: transparent;
  color: var(--qlx-ink-400, #929dae);
  cursor: pointer;
  padding: 0;
}

.qlx-tabs__close:hover {
  background: var(--qlx-danger-soft, #fbe9e7);
  color: var(--qlx-danger, #b3261e);
}

/* ============ 新增按钮 ============ */
.qlx-tabs__add {
  display: inline-flex;
  width: 28px;
  flex: 0 0 auto;
  align-items: center;
  align-self: center;
  justify-content: center;
  height: 28px;
  margin-right: var(--qlx-space-2, 8px);
  border: 1px solid var(--qlx-line, #dfe6ee);
  border-radius: var(--qlx-radius-xs, 2px);
  background: var(--qlx-surface, #fff);
  color: var(--qlx-ink-500, #6b7789);
  cursor: pointer;
}

.qlx-tabs__add:hover {
  border-color: var(--qlx-brand-300, #7ba6cf);
  color: var(--qlx-brand-700, #0e4478);
}

/* ============ 空态 ============ */
.qlx-tabs__empty {
  display: flex;
  flex: 1;
  align-items: center;
  justify-content: center;
  color: var(--qlx-ink-400, #929dae);
  font-size: var(--qlx-text-xs, 12px);
}

/* ============ 移动端适配（≤720px） ============ */
@media (max-width: 720px) {
  .qlx-tabs {
    /* 刘海屏安全区 */
    padding-right: env(safe-area-inset-right, 0);
    padding-left: env(safe-area-inset-left, 0);
  }

  .qlx-tabs__list {
    /* 移动端隐藏滚动条，保持横向滑动 */
    scrollbar-width: none;
  }

  .qlx-tabs__list::-webkit-scrollbar {
    display: none;
  }

  .qlx-tabs__item {
    height: 34px;
    max-width: 132px;
    padding: 0 8px;
    font-size: var(--qlx-text-xs, 12px);
    /* 扩大触控命中区 */
    min-height: 34px;
  }

  .qlx-tabs__item.is-pinned {
    padding: 0 7px;
  }

  /* 关闭按钮：触控命中区扩至 28px（视觉尺寸不变，伪元素扩热区） */
  .qlx-tabs__close {
    position: relative;
    width: 18px;
    height: 18px;
  }

  .qlx-tabs__close::after {
    position: absolute;
    inset: -6px;
    content: '';
  }

  /* 新增按钮：加大触控面积（位于滚动容器之外，天然保持可见） */
  .qlx-tabs__add {
    width: 32px;
    height: 32px;
    min-height: 32px;
    margin-left: 2px;
  }
}

</style>

<!-- 右键菜单 Teleport 到 body，样式需全局生效 -->
<style>
.qlx-tabs-menu {
  position: fixed;
  z-index: var(--qlx-z-overlay, 900);
  min-width: 132px;
  padding: 4px;
  border: 1px solid var(--qlx-line, #dfe6ee);
  border-radius: var(--qlx-radius-sm, 4px);
  background: var(--qlx-surface, #fff);
  box-shadow: var(--qlx-shadow-md, 0 8px 24px rgb(20 27 38 / 10%));
}

.qlx-tabs-menu__item {
  display: block;
  width: 100%;
  padding: 7px 12px;
  border: 0;
  border-radius: var(--qlx-radius-xs, 2px);
  background: transparent;
  color: var(--qlx-ink-700, #354050);
  cursor: pointer;
  font-size: var(--qlx-text-sm, 13px);
  text-align: left;
}

.qlx-tabs-menu__item:hover:not(:disabled) {
  background: var(--qlx-brand-50, #f0f5fa);
  color: var(--qlx-brand-700, #0e4478);
}

.qlx-tabs-menu__item:disabled {
  color: var(--qlx-ink-300, #b3bcc9);
  cursor: not-allowed;
}

.qlx-tabs-menu__item.is-danger:hover:not(:disabled) {
  background: var(--qlx-danger-soft, #fbe9e7);
  color: var(--qlx-danger, #b3261e);
}

.qlx-tabs-menu__divider {
  height: 1px;
  margin: 4px 8px;
  background: var(--qlx-line, #dfe6ee);
}

/* 移动端：菜单项触控命中区加大（≥40px） */
@media (max-width: 720px) {
  .qlx-tabs-menu__item {
    padding: 11px 14px;
    font-size: var(--qlx-text-base, 14px);
  }
}
</style>
