<!--
  所属公司：四川淇利馨集团有限公司
  描述：组织树面板——包装 QlxTreePanel：类型图标 / memberCount 徽标 / 停用态 / 搜索
  说明：受控展示组件；nodes 为 buildOrgTree 产出的嵌套树（页面组装期构建），选择意图经 select 抛出
-->
<script setup lang="ts">
import QlxTreePanel from '@/components/crud/QlxTreePanel.vue'
import AppIcon from '@/components/AppIcon.vue'
import { orgTypeIcons } from '@/types/tenant'
import type { OrgNodeType } from '@/types/org-admin'
import type { OrgTreeNode } from './org-admin-utils'

withDefaults(
  defineProps<{
    nodes: OrgTreeNode[]
    loading?: boolean
    selectedKey?: string
    /** 面板标题（默认「组织架构」）。 */
    title?: string
    searchPlaceholder?: string
    emptyText?: string
  }>(),
  {
    loading: false,
    selectedKey: '',
    title: '组织架构',
    searchPlaceholder: '搜索组织名称',
    emptyText: '暂无组织，请先创建',
  },
)

const emit = defineEmits<{
  select: [id: string, node: OrgTreeNode]
}>()

/** 模板取值辅助（树节点插槽为宽松类型）。 */
function orgIcon(type: OrgNodeType): string {
  return orgTypeIcons[type]
}
</script>

<template>
  <QlxTreePanel
    :title="title"
    :data="nodes"
    :loading="loading"
    :selected-key="selectedKey"
    :search-placeholder="searchPlaceholder"
    :empty-text="emptyText"
    class="org-tree-panel"
    @select="(id: string, node: OrgTreeNode) => emit('select', id, node)"
  >
    <template #node="{ data }">
      <span class="org-node" :class="{ 'is-off': data.status === 'Disabled' }">
        <AppIcon :name="orgIcon(data.type)" :size="14" class="org-node__icon" />
        <span class="org-node__name">{{ data.name }}</span>
        <span class="org-node__count qlx-numeric">{{ data.memberCount }}</span>
        <span v-if="data.status === 'Disabled'" class="org-node__dot is-off" title="已停用" />
      </span>
    </template>
    <template v-if="$slots.footer" #footer>
      <slot name="footer" />
    </template>
  </QlxTreePanel>
</template>

<style scoped>
.org-tree-panel {
  min-height: 420px;
}

.org-node {
  display: inline-flex;
  min-width: 0;
  flex: 1;
  align-items: center;
  gap: 6px;
}

.org-node__icon {
  color: var(--qlx-brand-600);
}

.org-node.is-off .org-node__icon,
.org-node.is-off .org-node__name {
  color: var(--qlx-ink-300);
}

.org-node__name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.org-node__count {
  margin-left: auto;
  padding: 0 6px;
  border-radius: var(--qlx-radius-full);
  background: var(--qlx-canvas);
  color: var(--qlx-ink-400);
  font-size: 11px;
  line-height: 18px;
}

.org-node__dot {
  width: 6px;
  height: 6px;
  flex: 0 0 auto;
  border-radius: 50%;
  background: var(--qlx-success);
}

.org-node__dot.is-off {
  background: var(--qlx-ink-300);
}
</style>
