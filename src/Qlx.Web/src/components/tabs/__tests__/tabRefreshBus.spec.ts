/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：标签刷新事件总线测试——按 id 定向投递 / 通配订阅 / 退订 / 清理
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  clearTabRefreshHandlers,
  emitTabRefresh,
  onTabRefresh,
  TAB_REFRESH_ALL,
} from '@/components/tabs/tabRefreshBus'

afterEach(() => {
  clearTabRefreshHandlers()
})

describe('tabRefreshBus 定向投递', () => {
  it('仅命中对应 id 的订阅者，其他标签不受影响', () => {
    const onA = vi.fn()
    const onB = vi.fn()
    onTabRefresh('a', onA)
    onTabRefresh('b', onB)

    emitTabRefresh('a', { refreshKey: 1, reason: 'context-menu' })

    expect(onA).toHaveBeenCalledWith({ id: 'a', refreshKey: 1, reason: 'context-menu' })
    expect(onB).not.toHaveBeenCalled()
  })

  it('同一 id 支持多个订阅者', () => {
    const h1 = vi.fn()
    const h2 = vi.fn()
    onTabRefresh('a', h1)
    onTabRefresh('a', h2)

    emitTabRefresh('a')
    expect(h1).toHaveBeenCalledOnce()
    expect(h2).toHaveBeenCalledOnce()
  })

  it('通配订阅者接收所有标签的刷新事件', () => {
    const onAll = vi.fn()
    onTabRefresh(TAB_REFRESH_ALL, onAll)

    emitTabRefresh('a', { refreshKey: 3 })
    emitTabRefresh('b')

    expect(onAll).toHaveBeenCalledTimes(2)
    expect(onAll).toHaveBeenNthCalledWith(1, { id: 'a', refreshKey: 3 })
  })
})

describe('tabRefreshBus 退订与清理', () => {
  it('返回的取消订阅函数生效', () => {
    const handler = vi.fn()
    const off = onTabRefresh('a', handler)

    emitTabRefresh('a')
    expect(handler).toHaveBeenCalledTimes(1)

    off()
    emitTabRefresh('a')
    expect(handler).toHaveBeenCalledTimes(1)
  })

  it('clearTabRefreshHandlers(id) 仅清理指定标签', () => {
    const onA = vi.fn()
    const onB = vi.fn()
    onTabRefresh('a', onA)
    onTabRefresh('b', onB)

    clearTabRefreshHandlers('a')
    emitTabRefresh('a')
    emitTabRefresh('b')

    expect(onA).not.toHaveBeenCalled()
    expect(onB).toHaveBeenCalledOnce()
  })

  it('clearTabRefreshHandlers() 清空全部订阅', () => {
    const onAll = vi.fn()
    const onA = vi.fn()
    onTabRefresh(TAB_REFRESH_ALL, onAll)
    onTabRefresh('a', onA)

    clearTabRefreshHandlers()
    emitTabRefresh('a')

    expect(onAll).not.toHaveBeenCalled()
    expect(onA).not.toHaveBeenCalled()
  })
})
