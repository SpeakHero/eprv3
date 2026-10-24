/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：标签管理器的 Vue 响应式封装——将纯 TS 的 TabManager 包装为响应式状态，
 *       供视图组件直接使用；使用 Pinia/Vuex 的项目亦可自行订阅 manager 事件同步状态。
 */

import { onBeforeUnmount, reactive, type UnwrapNestedRefs } from 'vue'
import { createTabManager, type TabManager } from './tabManager'
import type { TabItem, TabManagerOptions } from './types'

export interface UseTabManagerReturn {
  /** 纯逻辑管理器（增删改查、事件订阅的唯一入口） */
  manager: TabManager
  /** 响应式状态镜像（由 manager 的 change 事件驱动同步） */
  state: UnwrapNestedRefs<{ tabs: TabItem[]; activeId: string | null }>
}

/**
 * 创建响应式标签管理器。
 * 状态与视图解耦：所有状态变更必须经 manager 方法发起，state 只读消费。
 */
export function useTabManager(options: TabManagerOptions = {}): UseTabManagerReturn {
  const manager = createTabManager(options)

  const state = reactive<{ tabs: TabItem[]; activeId: string | null }>({
    tabs: [],
    activeId: null,
  })

  const off = manager.on('change', ({ tabs, activeId }) => {
    state.tabs = tabs
    state.activeId = activeId
  })

  onBeforeUnmount(off)

  return { manager, state }
}
