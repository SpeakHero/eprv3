/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：标签拖拽几何计算——纯函数，与 DOM 解耦（node 环境可单测）。
 *       负责落点索引计算、边界自动修正、固定区/普通区跨区域判定。
 */

import type { DragDropResult, DragItemRect } from './types'

/** 将任意索引钳制到 [0, max] 区间 */
export function clampIndex(index: number, max: number): number {
  if (max < 0) return 0
  return Math.max(0, Math.min(index, max))
}

/**
 * 计算拖拽落点索引（基于目标项中线判定，与拖拽物宽度无关）。
 *
 * 规则：指针位于某标签中线左侧 → 插入其前；右侧 → 插入其后；
 * 越过最后一项右缘 → 列表末尾；越出左缘 → 首位。
 *
 * @param pointerX 指针相对拖拽容器左缘的 x 坐标
 * @param rects    各标签的矩形描述（顺序即当前视觉顺序，含被拖拽项）
 * @returns 插入索引（0..rects.length，语义为「插入到该位置之前」，含被拖拽项原位）
 */
export function computeDropIndex(pointerX: number, rects: readonly DragItemRect[]): number {
  if (rects.length === 0) return 0
  for (let i = 0; i < rects.length; i += 1) {
    const mid = rects[i].left + rects[i].width / 2
    if (pointerX < mid) {
      return i
    }
  }
  return rects.length
}

/**
 * 计算最终拖拽落点：边界自动修正 + 跨区域（固定区 ↔ 普通区）判定。
 *
 * 修正规则：
 * 1. 指针越出容器左右边界时，分别落到列表首/尾；
 * 2. 被拖拽项从结果数组中剔除后再映射到 manager.move 的语义索引
 *    （move 先移除原项再插入，因此落点在拖拽项之后时索引需 -1）；
 * 3. 落点位于固定区数量之内 → 目标为固定标签，否则为非固定标签
 *    （跨区域拖拽即自动切换固定状态）。
 *
 * @param pointerX       指针相对容器左缘坐标
 * @param rects          全部标签矩形（含被拖拽项）
 * @param draggedId      被拖拽标签 id
 * @param containerWidth 容器可视宽度（越界修正用；传 Infinity 表示不限制）
 */
export function resolveDrop(
  pointerX: number,
  rects: readonly DragItemRect[],
  draggedId: string,
  containerWidth = Infinity,
): DragDropResult {
  const fromIndex = rects.findIndex((rect) => rect.id === draggedId)
  if (fromIndex === -1 || rects.length === 0) {
    return { index: 0, pinned: false }
  }

  // 1. 越界修正：指针越出容器时贴边
  const correctedX = clampIndex(Math.round(pointerX), Math.max(0, containerWidth))

  const rawIndex = computeDropIndex(correctedX, rects)

  // 2. 剔除拖拽项自身，换算为「移除后数组」的插入索引（manager.move 语义）
  let index = rawIndex
  if (rawIndex > fromIndex) {
    index = rawIndex - 1
  }
  index = clampIndex(index, rects.length - 1)

  // 3. 跨区域判定：固定区 = 当前 pinned 项数量（与 manager.move 的判定式严格一致：
  //    落点索引 < 固定区长度 → 目标为固定标签，反之为非固定标签）
  const pinnedTotal = rects.filter((rect) => rect.pinned).length
  const pinned = index < pinnedTotal

  return { index, pinned }
}
