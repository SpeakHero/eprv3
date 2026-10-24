<!--
  所属公司：四川淇利馨集团有限公司
  描述：通用树形面板——标题 + 关键字过滤 + 树选中 + 底部操作插槽（组织架构等左树右详情页复用）
  说明：节点内容通过 #node 插槽自定义（图标 / 状态 / 计数），#footer 放置新增等操作
-->
<script setup lang="ts" generic="T extends { id: string }">
import { computed, nextTick, ref, watch } from 'vue'
import AppIcon from '@/components/AppIcon.vue'

/** el-tree 暴露的方法子集（避免为类型引入运行时依赖）。 */
interface TreeExpose {
  filter: (value: string) => void
  setCurrentKey: (key?: string | number) => void
}

const props = withDefaults(
  defineProps<{
    title: string
    data: T[]
    /** 当前选中节点 Id。 */
    selectedKey?: string
    /** 节点名称字段（默认 name）。 */
    labelKey?: string
    /** 子节点字段（默认 children）。 */
    childrenKey?: string
    loading?: boolean
    searchPlaceholder?: string
    emptyText?: string
  }>(),
  {
    selectedKey: '',
    labelKey: 'name',
    childrenKey: 'children',
    loading: false,
    searchPlaceholder: '搜索名称',
    emptyText: '暂无节点',
  },
)

const emit = defineEmits<{
  select: [id: string, data: T]
}>()

const keyword = ref('')
const treeRef = ref<TreeExpose>()

const treeProps = computed(() => ({
  label: props.labelKey,
  children: props.childrenKey,
}))

const total = computed(() => countNodes(props.data))

function countNodes(list: T[]): number {
  let count = 0
  for (const node of list) {
    count += 1
    const children = (node as unknown as Record<string, unknown>)[props.childrenKey]
    if (Array.isArray(children)) {
      count += countNodes(children as T[])
    }
  }
  return count
}

function labelOf(node: T): string {
  return String((node as unknown as Record<string, unknown>)[props.labelKey] ?? '')
}

function filterNode(value: string, data: T): boolean {
  if (!value) {
    return true
  }
  return labelOf(data).toLowerCase().includes(value.toLowerCase())
}

function syncCurrentKey(): void {
  // 受控：selectedKey 为空时清除高亮（如成员页「查看全部组织」）
  treeRef.value?.setCurrentKey(props.selectedKey || undefined)
}

function onNodeClick(data: T): void {
  emit('select', data.id, data)
}

watch(keyword, (value) => {
  treeRef.value?.filter(value)
})

watch(
  () => props.selectedKey,
  async () => {
    await nextTick()
    syncCurrentKey()
  },
)

watch(
  () => props.data,
  async () => {
    await nextTick()
    syncCurrentKey()
  },
)
</script>

<template>
  <section v-loading="loading" class="qlx-tree-panel">
    <header class="qlx-tree-panel__head">
      <h3>{{ title }}</h3>
      <span class="qlx-tree-panel__count qlx-numeric">{{ total }} 个节点</span>
    </header>

    <div class="qlx-tree-panel__search">
      <el-input v-model="keyword" size="small" clearable :placeholder="searchPlaceholder">
        <template #prefix>
          <AppIcon name="Search" :size="13" />
        </template>
      </el-input>
    </div>

    <div class="qlx-tree-panel__body">
      <el-tree
        v-if="data.length"
        ref="treeRef"
        :data="data"
        :props="treeProps"
        node-key="id"
        highlight-current
        default-expand-all
        :expand-on-click-node="false"
        :filter-node-method="filterNode"
        @node-click="onNodeClick"
      >
        <template #default="{ data: nodeData }">
          <slot name="node" :data="nodeData">
            <span class="qlx-tree-panel__label">{{ labelOf(nodeData) }}</span>
          </slot>
        </template>
      </el-tree>
      <p v-else class="qlx-tree-panel__empty">{{ emptyText }}</p>
    </div>

    <footer v-if="$slots.footer" class="qlx-tree-panel__footer">
      <slot name="footer" />
    </footer>
  </section>
</template>

<style scoped>
.qlx-tree-panel {
  display: flex;
  min-height: 0;
  flex-direction: column;
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
}

.qlx-tree-panel__head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--qlx-space-2);
  padding: var(--qlx-space-3) var(--qlx-space-4);
  border-bottom: 1px solid var(--qlx-line);
}

.qlx-tree-panel__head h3 {
  color: var(--qlx-brand-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-base);
  font-weight: 600;
  letter-spacing: 0.04em;
}

.qlx-tree-panel__count {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.qlx-tree-panel__search {
  padding: var(--qlx-space-3) var(--qlx-space-3) var(--qlx-space-2);
}

.qlx-tree-panel__body {
  flex: 1;
  min-height: 360px;
  max-height: calc(100vh - 300px);
  padding: 0 var(--qlx-space-2) var(--qlx-space-2);
  overflow: auto;
}

.qlx-tree-panel__body :deep(.el-tree) {
  background: transparent;
  font-size: var(--qlx-text-sm);
}

.qlx-tree-panel__body :deep(.el-tree-node__content) {
  height: 34px;
  border-radius: var(--qlx-radius-xs);
}

.qlx-tree-panel__body :deep(.el-tree-node.is-current > .el-tree-node__content) {
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-700);
}

.qlx-tree-panel__label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.qlx-tree-panel__empty {
  padding: var(--qlx-space-10) 0;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-sm);
  text-align: center;
}

.qlx-tree-panel__footer {
  padding: var(--qlx-space-3);
  border-top: 1px solid var(--qlx-line);
}

.qlx-tree-panel__footer :deep(.el-button) {
  width: 100%;
}
</style>
