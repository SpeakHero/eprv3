/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：TabManager 核心逻辑回归测试——增删 / 激活 / 去重 / 上限 / 固定 / 排序 / 刷新 / 关闭校验
 */

import { describe, expect, it, vi } from 'vitest'
import { createTabManager } from '@/components/tabs/tabManager'
import type { TabManagerEventMap } from '@/components/tabs/types'

function seed(count: number) {
  const manager = createTabManager()
  for (let i = 1; i <= count; i += 1) {
    manager.open({ id: `t${i}`, title: `标签${i}` })
  }
  return manager
}

describe('TabManager 新增与去重', () => {
  it('open 新增标签并自动激活', () => {
    const manager = createTabManager()
    const { tab, created } = manager.open({ id: 'a', title: 'A' })
    expect(created).toBe(true)
    expect(manager.tabs.map((t) => t.id)).toEqual(['a'])
    expect(manager.activeId).toBe('a')
    expect(tab.refreshKey).toBe(0)
  })

  it('同 id 重复 open 去重：不新增，仅激活已有标签', () => {
    const manager = createTabManager()
    manager.open({ id: 'a', title: 'A' })
    manager.open({ id: 'b', title: 'B' })
    const addSpy = vi.fn()
    manager.on('tab-add', addSpy)

    const { tab, created } = manager.open({ id: 'a', title: 'A 改了标题' })
    expect(created).toBe(false)
    expect(manager.tabs).toHaveLength(2)
    expect(manager.activeId).toBe('a')
    expect(tab.title).toBe('A') // 原有标签数据不被覆盖
    expect(addSpy).not.toHaveBeenCalled()
  })

  it('dedupe=false 时允许重复 id', () => {
    const manager = createTabManager({ dedupe: false })
    manager.open({ id: 'a', title: 'A1' })
    manager.open({ id: 'a', title: 'A2' })
    expect(manager.tabs).toHaveLength(2)
  })
})

describe('TabManager 数量上限', () => {
  it('reject 策略：达到上限拒绝新增并触发 overflow 事件', () => {
    const manager = createTabManager({ maxTabs: 2, overflowStrategy: 'reject' })
    manager.open({ id: 'a', title: 'A' })
    manager.open({ id: 'b', title: 'B' })
    const overflowSpy = vi.fn()
    manager.on('tab-overflow', overflowSpy)

    const { created } = manager.open({ id: 'c', title: 'C' })
    expect(created).toBe(false)
    expect(manager.tabs).toHaveLength(2)
    expect(overflowSpy).toHaveBeenCalledOnce()
  })

  it('evict-oldest 策略：自动关闭最旧非固定标签', () => {
    const manager = createTabManager({ maxTabs: 2, overflowStrategy: 'evict-oldest' })
    manager.open({ id: 'a', title: 'A' })
    manager.open({ id: 'b', title: 'B' })
    const evictSpy = vi.fn()
    manager.on('tab-evict', evictSpy)

    manager.open({ id: 'c', title: 'C' })
    expect(manager.tabs.map((t) => t.id)).toEqual(['b', 'c'])
    expect(evictSpy).toHaveBeenCalledWith({ tab: expect.objectContaining({ id: 'a' }) })
    expect(manager.activeId).toBe('c')
  })

  it('evict-oldest 策略：固定标签不被驱逐；全部固定时拒绝新增', () => {
    const manager = createTabManager({ maxTabs: 2, overflowStrategy: 'evict-oldest' })
    manager.open({ id: 'p1', title: 'P1', pinned: true })
    manager.open({ id: 'p2', title: 'P2', pinned: true })
    const overflowSpy = vi.fn()
    manager.on('tab-overflow', overflowSpy)

    const { created } = manager.open({ id: 'c', title: 'C' })
    expect(created).toBe(false)
    expect(manager.tabs.map((t) => t.id)).toEqual(['p1', 'p2'])
    expect(overflowSpy).toHaveBeenCalledOnce()
  })

  it('驱逐的标签正是激活标签时，新标签按 open 语义接管激活态', () => {
    const manager = createTabManager({ maxTabs: 2, overflowStrategy: 'evict-oldest' })
    manager.open({ id: 'a', title: 'A' })
    manager.open({ id: 'b', title: 'B' })
    manager.activate('a') // 激活最旧的 a
    manager.open({ id: 'c', title: 'C' }) // 驱逐 a，随后新标签 c 激活
    expect(manager.activeId).toBe('c')
  })
})

describe('TabManager 关闭', () => {
  it('关闭激活标签后激活右侧邻居，无右侧则激活左侧', async () => {
    const manager = seed(3) // active = t3
    manager.activate('t2')
    await manager.close('t2')
    expect(manager.activeId).toBe('t3')

    manager.activate('t3')
    await manager.close('t3')
    expect(manager.activeId).toBe('t1')
  })

  it('allow-empty（默认）：可关闭最后一个标签', async () => {
    const manager = createTabManager()
    manager.open({ id: 'a', title: 'A' })
    expect(await manager.close('a')).toBe(true)
    expect(manager.tabs).toHaveLength(0)
    expect(manager.activeId).toBeNull()
  })

  it('prevent-close：阻止关闭最后一个标签', async () => {
    const manager = createTabManager({ closeLastBehavior: 'prevent-close' })
    manager.open({ id: 'a', title: 'A' })
    expect(await manager.close('a')).toBe(false)
    expect(manager.tabs).toHaveLength(1)
  })

  it('关闭校验钩子：返回 false 阻止关闭；单次调用可覆盖全局钩子', async () => {
    const manager = createTabManager({ beforeClose: (tab) => tab.id !== 'locked' })
    manager.open({ id: 'locked', title: '锁定' })
    manager.open({ id: 'free', title: '自由' })

    expect(await manager.close('locked')).toBe(false)
    expect(await manager.close('free')).toBe(true)

    // 单次调用覆盖：允许强制关闭
    expect(await manager.close('locked', () => true)).toBe(true)
  })

  it('异步关闭校验：Promise<false> 同样阻止关闭', async () => {
    const manager = createTabManager({ beforeClose: async () => false })
    manager.open({ id: 'a', title: 'A' })
    expect(await manager.close('a')).toBe(false)
    expect(manager.tabs).toHaveLength(1)
  })

  it('固定标签与 closable=false 标签不可关闭', async () => {
    const manager = createTabManager()
    manager.open({ id: 'p', title: 'P', pinned: true })
    manager.open({ id: 'n', title: 'N', closable: false })
    expect(await manager.close('p')).toBe(false)
    expect(await manager.close('n')).toBe(false)
  })

  it('closeOthers：仅关闭其他可关闭标签，保留固定与不可关闭标签', async () => {
    const manager = createTabManager()
    manager.open({ id: 'p', title: 'P', pinned: true })
    manager.open({ id: 'a', title: 'A' })
    manager.open({ id: 'b', title: 'B' })
    manager.open({ id: 'lock', title: 'L', closable: false })

    const closed = await manager.closeOthers('b')
    expect(closed).toEqual(['a'])
    expect(manager.tabs.map((t) => t.id)).toEqual(['p', 'b', 'lock'])
    expect(manager.activeId).toBe('b')
  })

  it('closeAll：关闭全部可关闭标签，保留固定标签', async () => {
    const manager = createTabManager()
    manager.open({ id: 'p', title: 'P', pinned: true })
    manager.open({ id: 'a', title: 'A' })
    manager.open({ id: 'b', title: 'B' })

    const closed = await manager.closeAll()
    expect(closed).toEqual(['a', 'b'])
    expect(manager.tabs.map((t) => t.id)).toEqual(['p'])
    expect(manager.activeId).toBe('p')
  })
})

describe('TabManager 固定与排序', () => {
  it('固定标签恒排在非固定标签之前（新增时自动归位）', () => {
    const manager = createTabManager()
    manager.open({ id: 'n1', title: 'N1' })
    manager.open({ id: 'p1', title: 'P1', pinned: true })
    manager.open({ id: 'n2', title: 'N2' })
    expect(manager.tabs.map((t) => t.id)).toEqual(['p1', 'n1', 'n2'])
  })

  it('setPinned(true) 移入固定区末尾；setPinned(false) 移入普通区首位', () => {
    const manager = seed(3)
    manager.setPinned('t2', true)
    expect(manager.tabs.map((t) => t.id)).toEqual(['t2', 't1', 't3'])

    manager.setPinned('t2', false)
    expect(manager.tabs.map((t) => t.id)).toEqual(['t2', 't1', 't3'])
    // 此时 t2 已非固定，再固定 t3 应排到固定区末尾
    manager.setPinned('t3', true)
    expect(manager.tabs.map((t) => t.id)).toEqual(['t3', 't2', 't1'])
  })

  it('move 同区排序并触发 tab-reorder（from/to 语义正确）', () => {
    const manager = seed(4)
    const spy = vi.fn()
    manager.on('tab-reorder', spy)

    manager.move('t1', 2)
    expect(manager.tabs.map((t) => t.id)).toEqual(['t2', 't3', 't1', 't4'])
    const payload = spy.mock.calls[0][0] as TabManagerEventMap['tab-reorder']
    expect(payload.fromIndex).toBe(0)
    expect(payload.toIndex).toBe(2)
    expect(payload.movedId).toBe('t1')
  })

  it('move 越界索引自动钳制到合法区间', () => {
    const manager = seed(3)
    manager.move('t1', 99)
    expect(manager.tabs.map((t) => t.id)).toEqual(['t2', 't3', 't1'])

    manager.move('t1', -5)
    expect(manager.tabs.map((t) => t.id)).toEqual(['t1', 't2', 't3'])
  })

  it('move 跨区域：普通标签拖入固定区自动固定；固定标签拖出自动取消固定', () => {
    const manager = seed(4)
    manager.setPinned('t1', true) // [t1(P), t2, t3, t4]
    const pinSpy = vi.fn()
    manager.on('tab-pin', pinSpy)

    // 普通 → 固定区
    manager.move('t4', 0)
    expect(manager.getById('t4')?.pinned).toBe(true)
    expect(manager.tabs[0].id).toBe('t4')
    expect(manager.tabs[1].pinned).toBe(true)

    // 固定 → 普通区（落点在固定区之外）
    manager.move('t4', 3)
    expect(manager.getById('t4')?.pinned).toBe(false)
    expect(manager.tabs.map((t) => t.id)).toEqual(['t1', 't2', 't3', 't4'])
    expect(pinSpy).toHaveBeenCalledTimes(2)
  })

  it('move 不存在标签返回 false', () => {
    const manager = seed(2)
    expect(manager.move('ghost', 0)).toBe(false)
  })
})

describe('TabManager 刷新', () => {
  it('refresh 使 refreshKey 自增并触发 tab-refresh 事件', () => {
    const manager = seed(2)
    const spy = vi.fn()
    manager.on('tab-refresh', spy)

    expect(manager.refresh('t1', 'manual')).toBe(true)
    expect(manager.getById('t1')?.refreshKey).toBe(1)
    expect(spy).toHaveBeenCalledWith({
      tab: expect.objectContaining({ id: 't1' }),
      refreshKey: 1,
      reason: 'manual',
    })

    manager.refresh('t1')
    expect(manager.getById('t1')?.refreshKey).toBe(2)
  })

  it('refresh 不存在的标签返回 false', () => {
    const manager = seed(1)
    expect(manager.refresh('ghost')).toBe(false)
  })
})

describe('TabManager 事件订阅', () => {
  it('on 返回取消订阅函数，退订后不再接收事件', () => {
    const manager = createTabManager()
    const spy = vi.fn()
    const off = manager.on('tab-add', spy)

    manager.open({ id: 'a', title: 'A' })
    expect(spy).toHaveBeenCalledTimes(1)

    off()
    manager.open({ id: 'b', title: 'B' })
    expect(spy).toHaveBeenCalledTimes(1)
  })

  it('change 事件在任何状态变更后最后触发，携带全量快照', () => {
    const manager = createTabManager()
    const order: string[] = []
    manager.on('tab-add', () => order.push('add'))
    manager.on('tab-activate', () => order.push('activate'))
    manager.on('change', () => order.push('change'))

    manager.open({ id: 'a', title: 'A' })
    expect(order[order.length - 1]).toBe('change')
    expect(order).toContain('add')
    expect(order).toContain('activate')
  })
})
