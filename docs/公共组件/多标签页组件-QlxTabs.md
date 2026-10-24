# QlxTabs 多标签页组件使用说明

> **版本**：v1.0
> **日期**：2026-10-24
> **源码位置**：`src/Qlx.Web/src/components/tabs/`
> **定位**：纯通用、零业务侵入的浏览器式多标签页组件，可直接复用于其他同类项目。

---

## 一、架构分层

```
components/tabs/
├── types.ts            # 全部 TS 类型（TabItem / Options / 事件 / 拖拽 / 菜单）
├── tabManager.ts       # 状态与操作核心（纯 TS，零框架依赖，可挂任意状态管理）
├── tabRefreshBus.ts    # 全局刷新事件总线（按标签 id 定向投递）
├── dragGeometry.ts     # 拖拽落点计算纯函数（落点索引 / 边界修正 / 跨区域判定）
├── useTabDrag.ts       # 指针拖拽交互 composable（Pointer Events，移动端长按拖拽）
├── useTabManager.ts    # 管理器的 Vue 响应式封装（change 事件 → reactive 镜像）
├── useTabRefresh.ts    # 标签内部刷新逻辑注册 composable（卸载自动退订）
├── QlxTabs.vue         # 渲染组件（插槽 / 右键菜单 / 默认样式）
├── index.ts            # 统一出口
└── __tests__/          # 单元测试（41 用例：管理器 / 几何 / 刷新总线）
```

**状态与视图解耦**：所有状态变更必须经 `TabManager` 方法发起；`QlxTabs` 只消费 `manager` 的 `change` 事件做渲染同步。因此 Pinia/Vuex 项目可以自行订阅 manager 事件写入 store，manager 本身无需任何修改。

---

## 二、快速开始

### 2.1 最小示例

```vue
<script setup lang="ts">
import { QlxTabs, useTabManager, useTabRefresh } from '@/components/tabs'

const { manager } = useTabManager({
  maxTabs: 10,
  dedupe: true,
  closeLastBehavior: 'allow-empty',
  overflowStrategy: 'reject',
})

manager.open({ id: 'home', title: '首页', pinned: true })
manager.open({ id: 'order-list', title: '订单列表', route: '/orders' })
</script>

<template>
  <QlxTabs :manager="manager" show-add @add-request="openNewTab" />
</template>
```

### 2.2 与路由联动（推荐模式）

```ts
// 路由变化 → 打开/激活标签
router.afterEach((to) => {
  if (to.meta.tab !== false) {
    manager.open({
      id: to.fullPath,
      title: (to.meta.title as string) ?? to.name?.toString() ?? to.fullPath,
      route: to.fullPath,
    })
  }
})

// 标签激活/关闭 → 路由跳转
manager.on('tab-activate', ({ tab }) => tab?.route && router.push(tab.route))
```

### 2.3 标签页内部注册刷新逻辑

```vue
<!-- 某个标签页内容组件内部 -->
<script setup lang="ts">
import { useTabRefresh } from '@/components/tabs'

const props = defineProps<{ tabId: string }>()
useTabRefresh(props.tabId, async () => {
  await reloadList() // 该标签自己的刷新逻辑
})
</script>
```

### 2.4 外部按 id 触发刷新

```ts
import { emitTabRefresh } from '@/components/tabs'

emitTabRefresh('order-list', { reason: 'order-created' })
// 或通过管理器（refreshKey 会自增，且事件会经 QlxTabs 自动转发到总线）
manager.refresh('order-list', 'manual')
```

通配监听全部标签刷新：`onTabRefresh(TAB_REFRESH_ALL, handler)`。

---

## 三、API 说明

### 3.1 QlxTabs Props

| Prop | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `manager` | `TabManager` | 必填 | `createTabManager()` / `useTabManager()` 创建的管理器实例 |
| `draggable` | `boolean` | `true` | 是否启用拖拽排序 |
| `showAdd` | `boolean` | `false` | 是否显示右侧「新增」按钮 |
| `contextMenu` | `boolean \| (tab) => TabContextMenuItem[]` | `true` | 右键菜单：`false` 关闭；函数自定义菜单项 |
| `emptyText` | `string` | `'暂无打开的标签页'` | 空态文案 |

### 3.2 QlxTabs 事件

| 事件 | 负载 | 说明 |
| --- | --- | --- |
| `tab-add` | `{ tab }` | 新增标签（去重命中不触发） |
| `tab-close` | `{ tab, index }` | 标签被关闭 |
| `tab-activate` | `{ tab, previousId }` | 激活切换 |
| `tab-reorder` | `{ tabs, movedId, fromIndex, toIndex }` | 拖拽排序完成 |
| `tab-pin` | `{ tab, pinned }` | 固定状态变化（含跨区域拖拽自动切换） |
| `tab-refresh` | `{ tab, refreshKey, reason? }` | 刷新触发（同时投递全局刷新总线） |
| `tab-overflow` | `{ input, maxTabs }` | 达到上限新增被拒绝 |
| `tab-evict` | `{ tab }` | 上限驱逐（`evict-oldest` 策略） |
| `add-request` | — | 点击新增按钮（新增什么由业务决定） |
| `context-action` | `{ action, tab }` | 右键菜单动作被选中 |

### 3.3 插槽

| 插槽 | 作用域 | 说明 |
| --- | --- | --- |
| `tab` | `{ tab, active, index, dragging }` | 完全自定义标签渲染 |
| `label` | `{ tab, active }` | 仅自定义标签文案部分 |
| `add` | — | 自定义新增按钮内容 |
| `context-menu` | `{ tab, items, select, close }` | 整体替换右键菜单 |
| `empty` | — | 空态内容 |

### 3.4 TabManager 方法

| 方法 | 签名 | 说明 |
| --- | --- | --- |
| `open` | `(input: TabInput) => { tab, created }` | 打开标签；`dedupe` 命中返回 `created: false` 并激活已有标签 |
| `close` | `(id, guard?) => Promise<boolean>` | 关闭（执行关闭校验；`guard` 可覆盖全局 `beforeClose`） |
| `activate` | `(id) => boolean` | 激活标签 |
| `setPinned` | `(id, pinned) => boolean` | 固定/取消固定（自动归位，固定恒在前） |
| `move` | `(id, toIndex) => boolean` | 拖拽排序唯一入口：越界钳制 + 跨区域自动切换固定状态 |
| `closeOthers` | `(id) => Promise<string[]>` | 关闭其他可关闭标签，返回实际关闭的 id 列表 |
| `closeAll` | `() => Promise<string[]>` | 关闭全部可关闭标签（固定标签保留） |
| `refresh` | `(id, reason?) => boolean` | `refreshKey` 自增并触发刷新事件 |
| `getById` | `(id) => TabItem \| undefined` | 按 id 查询 |
| `on` | `(event, handler) => unsubscribe` | 订阅事件 |

### 3.5 TabManagerOptions

| 配置 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `maxTabs` | `number` | `20` | 标签数量上限 |
| `dedupe` | `boolean` | `true` | 同 id 重复打开时去重（激活已有） |
| `closeLastBehavior` | `'allow-empty' \| 'prevent-close'` | `'allow-empty'` | 关闭最后一个标签的行为 |
| `overflowStrategy` | `'reject' \| 'evict-oldest'` | `'reject'` | 达到上限策略：拒绝 / 驱逐最旧非固定标签 |
| `beforeClose` | `(tab) => boolean \| Promise<boolean>` | — | 关闭校验钩子（返回 `false` 阻止关闭，支持异步） |

### 3.6 TabItem 字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | `string` | 全局唯一标识（刷新/去重/路由联动均以此为准） |
| `title` | `string` | 显示标题 |
| `icon?` | `string` | 图标（由渲染层决定展示方式） |
| `closable?` | `boolean` | 默认 `true`；固定标签忽略此字段 |
| `pinned?` | `boolean` | 固定标签恒排在非固定标签之前，且不可关闭 |
| `route?` | `string` | 关联路由（仅透传，组件不做跳转） |
| `meta?` | `Record<string, unknown>` | 业务扩展数据 |
| `refreshKey` | `number` | 刷新计数（管理器维护，每次刷新 +1，可配合 KeepAlive 重渲染） |

---

## 四、拖拽行为说明

- **桌面端**：鼠标左键按下位移超过 4px 即进入拖拽；**移动端**：长按 300ms 进入「待命」态（振动 10ms 反馈 + 锁定该标签原生滚动），此后横移超过 8px 进入拖拽，纵移超过 10px 自动取消（让位页面纵向滚动）；待命中直接松手 → 打开右键菜单（触屏 `contextmenu` 不可靠的替代手势）。
- **实时视觉反馈**：被拖拽标签跟随指针；落点之后的兄弟标签平移让位；跨区域时以虚线描边提示（金色 = 将固定，灰色 = 将取消固定）；靠近容器左右边缘自动滚动。
- **边界自动修正**：指针越出容器两端时落点贴边；落点索引钳制到合法区间；`Esc` 取消拖拽。
- **跨区域语义**：落点索引 < 固定区长度 → 自动固定；反之自动取消固定。几何计算（`dragGeometry.ts`）与 `manager.move` 的语义有测试契约保证一致。

---

## 五、移动端适配说明（≤720px 断点）

| 项 | 行为 |
| --- | --- |
| 横向滑动 | 隐藏滚动条（`scrollbar-width: none`），`-webkit-overflow-scrolling: touch` 惯性滚动，`overscroll-behavior-x: contain` 阻断滚动链 |
| 触控命中区 | 标签项高度 34px；关闭按钮视觉 18px、热区扩至 30px（伪元素 `inset: -6px`）；新增按钮 32px 且位于滚动容器之外始终可见；右键菜单项 ≥40px |
| 激活可见性 | 激活切换后自动 `scrollIntoView`（平滑滚动，移动端横滚容器内保证激活项可见） |
| 长按手势 | 长按 300ms 待命 → 松手开菜单 / 横移拖拽 / 纵移取消，三态互不冲突 |
| 振动反馈 | 进入待命态 `navigator.vibrate(10)`（不支持的设备静默忽略） |
| 刘海屏 | 容器适配 `env(safe-area-inset-left/right)` |
| 点按刷新 | `tapActiveRefresh` prop 开启后，再次点击已激活标签触发刷新（移动端常用手势，默认关闭） |

---

## 六、复用到其他项目

1. 整体复制 `components/tabs/` 目录（无任何项目内耦合，仅依赖 `vue`）。
2. 样式换肤：组件全部使用 `--qlx-*` CSS 变量并带兜底值；目标项目无此令牌时自动回退到内置色值，或直接覆盖同名变量。
3. 接入业务：仅需创建 `TabManager` 实例、挂接路由/刷新逻辑，核心代码零修改。
