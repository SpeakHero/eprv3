/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：多标签页组件统一出口——类型 / 管理器 / 刷新总线 / 拖拽 / 视图组件
 */

export { default as QlxTabs } from './QlxTabs.vue'

export { createTabManager, type TabManager, type TabUnsubscribe } from './tabManager'
export { useTabManager, type UseTabManagerReturn } from './useTabManager'
export { useTabDrag, type TabDragState, type UseTabDragOptions } from './useTabDrag'
export { useTabRefresh } from './useTabRefresh'

export {
  onTabRefresh,
  emitTabRefresh,
  clearTabRefreshHandlers,
  TAB_REFRESH_ALL,
  type TabRefreshHandler,
  type TabRefreshPayload,
} from './tabRefreshBus'

export { computeDropIndex, clampIndex, resolveDrop } from './dragGeometry'

export type {
  TabItem,
  TabInput,
  TabManagerOptions,
  TabManagerEventMap,
  TabManagerEventName,
  TabManagerEventHandler,
  TabCloseGuard,
  TabCloseLastBehavior,
  TabOverflowStrategy,
  DragItemRect,
  DragDropResult,
  TabContextAction,
  TabContextMenuItem,
} from './types'
