/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：标签拖拽交互 composable——基于 Pointer Events 的统一指针模型。
 *       桌面：鼠标左键位移超阈值即拖；
 *       移动端：长按 300ms 进入「待命」态（振动反馈 + 锁定原生滚动），
 *              待命后横移超阈值进入拖拽；待命后未横移即松手 → 触发长按回调
 *              （供视图层打开右键菜单，作为触屏 contextmenu 不可靠的替代）。
 *       仅负责交互与视觉状态，落点计算委托 dragGeometry 纯函数。
 */

import { onBeforeUnmount, reactive, type Ref } from 'vue'
import { resolveDrop } from './dragGeometry'
import type { DragDropResult, DragItemRect } from './types'

/** 移动端长按判定毫秒数 */
const LONG_PRESS_MS = 300
/** 鼠标位移超过该阈值即进入拖拽（避免点击误触） */
const MOUSE_THRESHOLD_PX = 4
/** 触屏待命后横移超过该阈值进入拖拽（低于此视为长按手势） */
const TOUCH_DRAG_THRESHOLD_PX = 8
/** 触屏待命期间纵移超过该阈值取消待命（用户意图是纵向滚动页面） */
const TOUCH_CANCEL_VERTICAL_PX = 10
/** 容器边缘自动滚动的感应区宽度（px）与速度（px/帧） */
const EDGE_SCROLL_ZONE = 32
const EDGE_SCROLL_STEP = 8

export interface UseTabDragOptions {
  /** 标签条横向滚动容器 */
  listRef: Ref<HTMLElement | null>
  /** 是否启用拖拽（可由 props 控制） */
  enabled: Ref<boolean>
  /** 拖拽结束提交（index/pinned 已由几何层修正完毕） */
  onCommit: (draggedId: string, result: DragDropResult) => void
  /** 拖拽取消回调（如按 ESC，可选） */
  onCancel?: (draggedId: string) => void
  /** 触屏长按后未横移即松手（触屏右键菜单替代手势，可选） */
  onPressHoldRelease?: (id: string, position: { x: number; y: number }) => void
}

export interface TabDragState {
  /** 正在拖拽的标签 id（未拖拽时为 null） */
  draggingId: string | null
  /** 被拖拽标签在列表中的原始索引（-1 表示未拖拽），供视图计算兄弟项位移 */
  fromIndex: number
  /** 被拖拽标签宽度（px），供视图计算兄弟项让位位移 */
  dragWidth: number
  /** 被拖拽标签相对其原位的水平偏移（px），用于跟随指针的视觉反馈 */
  offsetX: number
  /** 当前落点索引（-1 表示未在拖拽；语义为剔除拖拽项后的插入位置，与 manager.move 一致） */
  dropIndex: number
  /** 当前落点是否位于固定区（跨区域实时反馈） */
  dropPinned: boolean
  /** 最近一次拖拽提交的时间戳（视图层用于抑制拖拽结束的误触点击） */
  lastCommitAt: number
}

export function useTabDrag(options: UseTabDragOptions) {
  const state = reactive<TabDragState>({
    draggingId: null,
    fromIndex: -1,
    dragWidth: 0,
    offsetX: 0,
    dropIndex: -1,
    dropPinned: false,
    lastCommitAt: 0,
  })

  let startClientX = 0
  let startClientY = 0
  let grabOffsetX = 0 // 指针在标签内部的点击偏移
  let originLeft = 0 // 被拖拽标签相对容器内容左缘的原始偏移
  let pendingId: string | null = null
  let pendingPointerId = -1
  let pendingNode: HTMLElement | null = null // 触屏待命时锁定 touch-action 的节点
  let longPressTimer: ReturnType<typeof setTimeout> | undefined
  let armed = false // 触屏长按已就绪（待命态）
  let dragging = false
  let scrollRaf = 0

  function cleanupWindowListeners(): void {
    window.removeEventListener('pointermove', handlePointerMove)
    window.removeEventListener('pointerup', handlePointerUp)
    window.removeEventListener('pointercancel', handlePointerUp)
    window.removeEventListener('keydown', handleKeydown)
  }

  function reset(): void {
    if (longPressTimer !== undefined) {
      clearTimeout(longPressTimer)
      longPressTimer = undefined
    }
    if (scrollRaf) {
      cancelAnimationFrame(scrollRaf)
      scrollRaf = 0
    }
    // 恢复触屏待命期间锁定的原生滚动手势
    if (pendingNode) {
      pendingNode.style.touchAction = ''
      pendingNode = null
    }
    dragging = false
    armed = false
    pendingId = null
    pendingPointerId = -1
    state.draggingId = null
    state.fromIndex = -1
    state.dragWidth = 0
    state.offsetX = 0
    state.dropIndex = -1
    state.dropPinned = false
    cleanupWindowListeners()
  }

  /** 测量当前 DOM 中全部标签的矩形（相对容器内容左缘，含滚动偏移） */
  function measureRects(list: HTMLElement): DragItemRect[] {
    const listRect = list.getBoundingClientRect()
    const nodes = list.querySelectorAll<HTMLElement>('[data-tab-id]')
    const rects: DragItemRect[] = []
    nodes.forEach((node) => {
      const rect = node.getBoundingClientRect()
      rects.push({
        id: node.dataset.tabId ?? '',
        left: rect.left - listRect.left + list.scrollLeft,
        width: rect.width,
        pinned: node.dataset.tabPinned === 'true',
      })
    })
    return rects
  }

  function updateDrop(clientX: number): void {
    const list = options.listRef.value
    if (!list || !state.draggingId) return

    const listRect = list.getBoundingClientRect()
    const pointerX = clientX - listRect.left + list.scrollLeft

    // 视觉跟随：拖拽标签相对原位的偏移
    state.offsetX = pointerX - originLeft - grabOffsetX

    const rects = measureRects(list)
    const result = resolveDrop(pointerX, rects, state.draggingId, list.scrollWidth)
    state.dropIndex = result.index
    state.dropPinned = result.pinned

    scheduleEdgeScroll(clientX, list, listRect)
  }

  /** 容器左右边缘自动滚动（拖拽靠近边缘时） */
  function scheduleEdgeScroll(clientX: number, list: HTMLElement, listRect: DOMRect): void {
    if (scrollRaf) cancelAnimationFrame(scrollRaf)
    scrollRaf = requestAnimationFrame(() => {
      if (clientX < listRect.left + EDGE_SCROLL_ZONE) {
        list.scrollLeft -= EDGE_SCROLL_STEP
      } else if (clientX > listRect.right - EDGE_SCROLL_ZONE) {
        list.scrollLeft += EDGE_SCROLL_STEP
      }
    })
  }

  function handlePointerMove(event: PointerEvent): void {
    if (event.pointerId !== pendingPointerId) return

    if (!dragging) {
      if (event.pointerType === 'mouse') {
        // 鼠标：位移阈值判定
        if (Math.abs(event.clientX - startClientX) >= MOUSE_THRESHOLD_PX && pendingId) {
          beginDrag(pendingId)
        } else {
          return
        }
      } else {
        // 触屏：未进入待命态前的横移视为列表滚动意图，放弃本次手势
        if (!armed) {
          if (Math.abs(event.clientX - startClientX) >= TOUCH_DRAG_THRESHOLD_PX) {
            reset() // 让位给原生横向滚动
          }
          return
        }
        // 待命态：纵移过大说明用户想纵向滚动页面，取消待命
        if (Math.abs(event.clientY - startClientY) >= TOUCH_CANCEL_VERTICAL_PX) {
          reset()
          return
        }
        // 待命态横移超阈值 → 进入拖拽
        if (Math.abs(event.clientX - startClientX) >= TOUCH_DRAG_THRESHOLD_PX && pendingId) {
          beginDrag(pendingId)
        } else {
          return
        }
      }
    }
    event.preventDefault()
    updateDrop(event.clientX)
  }

  function handlePointerUp(event: PointerEvent): void {
    if (event.pointerId !== pendingPointerId) return
    const draggedId = state.draggingId
    const wasDragging = dragging
    const drop =
      wasDragging && draggedId
        ? { index: state.dropIndex, pinned: state.dropPinned }
        : null
    // 触屏长按已就绪但未进入拖拽 → 长按手势（打开右键菜单）
    const pressHold =
      !wasDragging && armed && pendingId && event.pointerType !== 'mouse'
        ? { id: pendingId, position: { x: event.clientX, y: event.clientY } }
        : null
    reset()
    if (wasDragging && draggedId && drop) {
      state.lastCommitAt = Date.now()
      options.onCommit(draggedId, drop)
    } else if (pressHold) {
      state.lastCommitAt = Date.now() // 抑制长按松手后的误触激活
      options.onPressHoldRelease?.(pressHold.id, pressHold.position)
    }
  }

  function handleKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && state.draggingId) {
      const draggedId = state.draggingId
      reset()
      options.onCancel?.(draggedId)
    }
  }

  function beginDrag(id: string): void {
    const list = options.listRef.value
    if (!list) return
    dragging = true
    state.draggingId = id
    // 记录标签原始偏移与宽度（视觉跟随基准 / 兄弟项让位宽度）
    const node = list.querySelector<HTMLElement>(`[data-tab-id="${CSS.escape(id)}"]`)
    if (node) {
      const listRect = list.getBoundingClientRect()
      originLeft = node.getBoundingClientRect().left - listRect.left + list.scrollLeft
      state.dragWidth = node.getBoundingClientRect().width
      const rects = measureRects(list)
      state.fromIndex = rects.findIndex((rect) => rect.id === id)
    }
    updateDrop(startClientX)
  }

  /**
   * 标签 pointerdown 入口（由 QlxTabs 绑定到每个标签项）。
   * 鼠标立即待命（位移阈值触发拖拽）；触屏启动长按计时器进入待命态。
   */
  function handlePointerDown(event: PointerEvent, id: string): void {
    if (!options.enabled.value) return
    // 仅响应主按键（鼠标左键 / 触屏单指）
    if (event.pointerType === 'mouse' && event.button !== 0) return
    if (state.draggingId) return

    pendingId = id
    pendingPointerId = event.pointerId
    startClientX = event.clientX
    startClientY = event.clientY

    const target = event.currentTarget as HTMLElement | null
    if (target) {
      const rect = target.getBoundingClientRect()
      grabOffsetX = event.clientX - rect.left
      pendingNode = target
    }

    window.addEventListener('pointermove', handlePointerMove, { passive: false })
    window.addEventListener('pointerup', handlePointerUp)
    window.addEventListener('pointercancel', handlePointerUp)
    window.addEventListener('keydown', handleKeydown)

    if (event.pointerType !== 'mouse') {
      // 移动端：长按进入待命态（振动反馈 + 锁定该标签的原生滚动，
      // 之后横移进入拖拽、松手触发长按回调）
      longPressTimer = setTimeout(() => {
        if (!pendingId) return
        armed = true
        if (pendingNode) pendingNode.style.touchAction = 'none'
        // 触觉反馈（支持振动 API 的设备；不支持时静默忽略）
        navigator.vibrate?.(10)
      }, LONG_PRESS_MS)
    }
  }

  onBeforeUnmount(reset)

  return { state, handlePointerDown }
}
