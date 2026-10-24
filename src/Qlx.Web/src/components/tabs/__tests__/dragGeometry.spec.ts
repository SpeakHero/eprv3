/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：拖拽几何纯函数测试——落点索引 / 边界修正 / 跨区域（固定区↔普通区）判定，
 *       并与 manager.move 的落点语义做一致性回归。
 */

import { describe, expect, it } from 'vitest'
import { clampIndex, computeDropIndex, resolveDrop } from '@/components/tabs/dragGeometry'
import { createTabManager } from '@/components/tabs/tabManager'
import type { DragItemRect } from '@/components/tabs/types'

/** 构造等宽 100px、从 0 开始紧密排列的矩形列表 */
function makeRects(specs: Array<{ id: string; pinned?: boolean }>): DragItemRect[] {
  return specs.map((spec, index) => ({
    id: spec.id,
    left: index * 100,
    width: 100,
    pinned: Boolean(spec.pinned),
  }))
}

describe('clampIndex 边界钳制', () => {
  it('索引钳制到 [0, max]', () => {
    expect(clampIndex(-3, 5)).toBe(0)
    expect(clampIndex(3, 5)).toBe(3)
    expect(clampIndex(99, 5)).toBe(5)
    expect(clampIndex(1, -1)).toBe(0)
  })
})

describe('computeDropIndex 落点索引', () => {
  const rects = makeRects([{ id: 'a' }, { id: 'b' }, { id: 'c' }])

  it('指针在某项中线左侧 → 插入其前', () => {
    expect(computeDropIndex(10, rects)).toBe(0) // a 前半
    expect(computeDropIndex(120, rects)).toBe(1) // b 前半
    expect(computeDropIndex(220, rects)).toBe(2) // c 前半
  })

  it('指针在某项中线右侧 → 插入其后', () => {
    expect(computeDropIndex(60, rects)).toBe(1) // a 后半
    expect(computeDropIndex(160, rects)).toBe(2) // b 后半
  })

  it('越过最后一项右缘 → 列表末尾；空列表 → 0', () => {
    expect(computeDropIndex(999, rects)).toBe(3)
    expect(computeDropIndex(0, [])).toBe(0)
  })
})

describe('resolveDrop 边界修正与跨区域判定', () => {
  it('拖拽项之后的落点索引自动 -1（对齐 manager.move 的移除后语义）', () => {
    const rects = makeRects([{ id: 'a' }, { id: 'b' }, { id: 'c' }])
    // 把 a 拖到 c 之后：原始落点 3 → 剔除 a 后应为 2
    const result = resolveDrop(250, rects, 'a')
    expect(result.index).toBe(2)
  })

  it('拖拽项之前的落点索引保持不变', () => {
    const rects = makeRects([{ id: 'a' }, { id: 'b' }, { id: 'c' }])
    // 把 c 拖到最前：原始落点 0 → 剔除 c 后仍为 0
    const result = resolveDrop(10, rects, 'c')
    expect(result.index).toBe(0)
  })

  it('指针越出容器左右边界时贴边修正', () => {
    const rects = makeRects([{ id: 'a' }, { id: 'b' }])
    expect(resolveDrop(-50, rects, 'b', 200).index).toBe(0)
    expect(resolveDrop(500, rects, 'a', 200).index).toBe(1)
  })

  it('普通标签落入固定区 → pinned=true；固定标签落入普通区 → pinned=false', () => {
    const rects = makeRects([{ id: 'p', pinned: true }, { id: 'n1' }, { id: 'n2' }])
    // n2 拖到最前（固定区内）
    expect(resolveDrop(10, rects, 'n2').pinned).toBe(true)
    // p 拖到末尾（普通区）
    expect(resolveDrop(250, rects, 'p').pinned).toBe(false)
    // n2 在普通区内移动
    expect(resolveDrop(250, rects, 'n1').pinned).toBe(false)
  })

  it('落点结果与 manager.move 组合后顺序一致（几何↔管理器契约回归）', () => {
    // 场景：4 个等宽标签 [a, b, c, d]，把 b 拖到 c/d 之间（指针 x=260）
    const specs = [{ id: 'a' }, { id: 'b' }, { id: 'c' }, { id: 'd' }]
    const rects = makeRects(specs)
    const { index, pinned } = resolveDrop(260, rects, 'b')

    const manager = createTabManager()
    specs.forEach((spec) => manager.open({ id: spec.id, title: spec.id }))
    manager.move('b', index)

    expect(manager.tabs.map((t) => t.id)).toEqual(['a', 'c', 'b', 'd'])
    expect(Boolean(manager.getById('b')?.pinned)).toBe(pinned)
  })

  it('跨区域落点与 manager.move 组合后固定状态一致', () => {
    // 场景：[P(pinned), a, b]，把 b 拖到最前
    const specs = [{ id: 'P', pinned: true }, { id: 'a' }, { id: 'b' }]
    const rects = makeRects(specs)
    const { index, pinned } = resolveDrop(5, rects, 'b')
    expect(pinned).toBe(true)

    const manager = createTabManager()
    specs.forEach((spec) => manager.open({ id: spec.id, title: spec.id, pinned: spec.pinned }))
    manager.move('b', index)

    expect(manager.getById('b')?.pinned).toBe(true)
    expect(manager.tabs[0].id).toBe('b')
    expect(manager.tabs[1].id).toBe('P')
  })
})
