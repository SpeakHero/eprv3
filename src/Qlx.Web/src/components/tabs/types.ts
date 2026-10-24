/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：多标签页组件公共类型定义——纯通用设计，零业务侵入，零框架依赖（可被任意状态管理方案包装）
 */

/** 标签页数据项（扁平原始数据，展示组装由使用方/插槽负责） */
export interface TabItem {
  /** 全局唯一标识（刷新、路由联动、去重均以此为准） */
  id: string
  /** 显示标题 */
  title: string
  /** 图标名（可选，由渲染层决定如何展示） */
  icon?: string
  /** 是否可关闭，默认 true；pinned 标签忽略此字段（固定即不可关） */
  closable?: boolean
  /** 是否固定（固定标签恒排在非固定标签之前） */
  pinned?: boolean
  /** 关联路由路径（可选，仅透传，组件内部不做路由跳转） */
  route?: string
  /** 业务自定义扩展数据 */
  meta?: Record<string, unknown>
  /** 刷新计数：每次触发刷新 +1，供内容区 KeepAlive/重渲染使用 */
  refreshKey: number
}

/** 新建标签入参（refreshKey 由管理器初始化，无需传入） */
export type TabInput = Omit<TabItem, 'refreshKey'> & { refreshKey?: number }

/** 关闭最后一个标签时的行为 */
export type TabCloseLastBehavior =
  /** 允许全部关闭，activeId 置空 */
  | 'allow-empty'
  /** 阻止关闭最后一个标签 */
  | 'prevent-close'

/** 达到标签数量上限时的策略 */
export type TabOverflowStrategy =
  /** 拒绝新增并触发 overflow 事件 */
  | 'reject'
  /** 关闭最旧的非固定标签后新增（固定标签永不被动关闭） */
  | 'evict-oldest'

/** 关闭校验钩子：返回 false 或 Promise<false> 阻止关闭 */
export type TabCloseGuard = (tab: TabItem) => boolean | Promise<boolean>

/** 管理器配置项 */
export interface TabManagerOptions {
  /** 标签数量上限（不含限制时传 Infinity），默认 20 */
  maxTabs?: number
  /** 同 id 重复打开时是否去重（激活已有标签），默认 true */
  dedupe?: boolean
  /** 关闭最后一个标签的行为，默认 'allow-empty' */
  closeLastBehavior?: TabCloseLastBehavior
  /** 达到上限策略，默认 'reject' */
  overflowStrategy?: TabOverflowStrategy
  /** 关闭前校验钩子（全局默认，单次 close 调用可覆盖） */
  beforeClose?: TabCloseGuard
}

/** 管理器事件负载 */
export interface TabManagerEventMap {
  /** 新增标签（去重命中已有标签时不触发） */
  'tab-add': { tab: TabItem }
  /** 标签被关闭 */
  'tab-close': { tab: TabItem; index: number }
  /** 激活标签切换（含去重命中导致的切换） */
  'tab-activate': { tab: TabItem | null; previousId: string | null }
  /** 标签顺序变化（拖拽/移动完成后触发一次） */
  'tab-reorder': { tabs: TabItem[]; movedId: string; fromIndex: number; toIndex: number }
  /** 固定状态变化（含跨区域拖拽自动切换） */
  'tab-pin': { tab: TabItem; pinned: boolean }
  /** 刷新触发（refreshKey 已自增） */
  'tab-refresh': { tab: TabItem; refreshKey: number; reason?: string }
  /** 达到上限新增被拒绝（仅 overflowStrategy = reject 时） */
  'tab-overflow': { input: TabInput; maxTabs: number }
  /** 上限驱逐：最旧非固定标签被自动关闭（仅 evict-oldest 时） */
  'tab-evict': { tab: TabItem }
  /** 任意状态变化（供视图层同步用，永远最后触发） */
  change: { tabs: TabItem[]; activeId: string | null }
}

export type TabManagerEventName = keyof TabManagerEventMap

export type TabManagerEventHandler<K extends TabManagerEventName> = (
  payload: TabManagerEventMap[K],
) => void

/** 拖拽落点计算输入的矩形描述（与 DOM 解耦，便于单测） */
export interface DragItemRect {
  id: string
  /** 相对拖拽容器左缘的偏移 */
  left: number
  width: number
  pinned: boolean
}

/** 拖拽落点结果 */
export interface DragDropResult {
  /** 目标插入索引（相对于完整标签数组，已做边界修正） */
  index: number
  /** 跨区域拖拽时目标区域（落入固定区则该标签应被固定） */
  pinned: boolean
}

/** 右键菜单动作标识（默认菜单使用，自定义菜单可忽略） */
export type TabContextAction =
  | 'refresh'
  | 'close'
  | 'close-others'
  | 'close-all'
  | 'toggle-pin'

/** 右键菜单项（默认菜单渲染数据结构） */
export interface TabContextMenuItem {
  action: TabContextAction
  label: string
  /** 是否禁用（如最后一个标签禁止关闭） */
  disabled?: boolean
  /** 是否危险操作（红色强调） */
  danger?: boolean
  /** 分隔线（渲染在该项之前） */
  divided?: boolean
}
