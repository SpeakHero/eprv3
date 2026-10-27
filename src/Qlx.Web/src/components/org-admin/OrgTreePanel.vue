<!--
  所属公司：四川淇利馨集团有限公司
  描述：组织树面板——包装 QlxTreePanel：类型图标 / 编码 / 停用态 / 搜索
  说明：受控展示组件；nodes 为 buildOrgTree 产出的嵌套树（页面组装期构建），选择意图经 select 抛出；
        Bas 主数据用数值类型（1=集团 / 2=子公司 / 3=部门 / 4=项目部）与数值状态，
        旧租户组织契约（字符串枚举、无编码）降级展示，两套契约共用同一面板；映射函数在 org-admin-utils（B-4）
-->
<script setup lang="ts">
import QlxTreePanel from '@/components/crud/QlxTreePanel.vue'
import AppIcon from '@/components/AppIcon.vue'
import { basOrgStatusLabel, basOrgTypeIcon } from './org-admin-utils'
import type { OrgTreeSource } from './org-admin-utils'

/** 树面板节点：满足建树最小字段集；code / type / status 为可选展示字段（旧契约缺省时降级）。 */
export interface OrgTreePanelNode extends OrgTreeSource {
  children?: OrgTreePanelNode[]
  code?: string
  type?: number | string
  status?: number | string
}

withDefaults(
  defineProps<{
    nodes: OrgTreePanelNode[]
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
  select: [id: string]
}>()

/** 停用判定：Bas 数值 2 / 旧契约 'Disabled'。 */
function nodeDisabled(node: OrgTreePanelNode): boolean {
  return node.status === 2 || node.status === 'Disabled'
}

/** 类型图标：Bas 数值走四级映射，旧契约统一默认图标。 */
function nodeIcon(node: OrgTreePanelNode): string {
  return typeof node.type === 'number' ? basOrgTypeIcon(node.type) : 'OfficeBuilding'
}

/** 停用态提示文案。 */
function nodeStatusTitle(node: OrgTreePanelNode): string {
  if (typeof node.status === 'number') {
    return basOrgStatusLabel(node.status)
  }
  return nodeDisabled(node) ? '停用' : '启用'
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
    @select="(id: string) => emit('select', id)"
  >
    <template #node="{ data }">
      <span class="org-node" :class="{ 'is-off': nodeDisabled(data) }">
        <AppIcon :name="nodeIcon(data)" :size="14" class="org-node__icon" />
        <span class="org-node__name">{{ data.name }}</span>
        <span v-if="data.code" class="org-node__code qlx-numeric">{{ data.code }}</span>
        <span
          v-if="nodeDisabled(data)"
          class="org-node__dot is-off"
          :title="nodeStatusTitle(data)"
        />
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
.org-node.is-off .org-node__name,
.org-node.is-off .org-node__code {
  color: var(--qlx-ink-300);
}

.org-node__name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.org-node__code {
  margin-left: auto;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.org-node__dot {
  width: 6px;
  height: 6px;
  border-radius: var(--qlx-radius-full);
  background: var(--qlx-warning);
}

.org-node__dot.is-off {
  background: var(--qlx-ink-300);
}
</style>
