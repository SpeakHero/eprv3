/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：标签页内部刷新逻辑注册 composable——在标签内容组件中调用，
 *       自动绑定当前标签 id，组件卸载时自动退订。
 */

import { onBeforeUnmount, toValue, type MaybeRefOrGetter } from 'vue'
import { onTabRefresh, type TabRefreshHandler } from './tabRefreshBus'

/**
 * 在当前标签页组件内注册刷新逻辑。
 * 外部（右键菜单 / 全局接口 / 管理器）触发该标签刷新时执行 handler。
 *
 * @example
 * useTabRefresh(props.tabId, async () => { await reloadList() })
 */
export function useTabRefresh(
  id: MaybeRefOrGetter<string>,
  handler: TabRefreshHandler,
): void {
  let off: (() => void) | undefined
  let boundId: string | undefined

  const bind = () => {
    const currentId = toValue(id)
    if (currentId !== boundId) {
      off?.()
      boundId = currentId
      off = onTabRefresh(currentId, handler)
    }
  }

  bind()
  onBeforeUnmount(() => off?.())
}
