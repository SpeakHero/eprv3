<!--
  所属公司：四川淇利馨集团有限公司
  描述：组织详情卡——描述列表 + 下级组织 chips + 编辑 / 启停操作
  说明：受控展示组件；负责人名 / 上级名经 props 注入（页面用候选清单解析），
        停用 / 启用的确认框由页面触发（组件只 emit 意图）
-->
<script setup lang="ts">
import { computed } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import QlxDescriptionList from '@/components/crud/QlxDescriptionList.vue'
import type { DescriptionItem } from '@/components/crud/QlxDescriptionList.vue'
import { entityStatusLabels, entityStatusTones, orgTypeIcons, orgTypeLabels } from '@/types/tenant'
import { formatDateTime } from '@/utils/format'
import type { OrgTreeNode } from './org-admin-utils'

const props = withDefaults(
  defineProps<{
    node: OrgTreeNode | null
    /** 上级组织显示名（根节点页面传「无（公司根节点）」）。 */
    parentName?: string
    /** 负责人显示名（页面用负责人候选清单解析 managerMemberId）。 */
    managerName?: string
  }>(),
  { parentName: '—', managerName: '' },
)

const emit = defineEmits<{
  edit: []
  toggle: []
  'select-child': [id: string]
}>()

/** 根公司启用态下不可停用（后端 409 同规则，前端先行禁用）。 */
const toggleDisabled = computed(
  () => props.node !== null && props.node.parentId === null && props.node.status === 'Enabled',
)

const items = computed<DescriptionItem[]>(() => {
  const node = props.node
  if (!node) {
    return []
  }
  return [
    { label: '组织名称', value: node.name },
    { label: '组织类型', value: orgTypeLabels[node.type] },
    { label: '上级组织', value: props.parentName },
    { label: '负责人', value: props.managerName || '未指定' },
    { label: '直属成员', value: `${node.memberCount} 人` },
    { label: '状态', value: entityStatusLabels[node.status] },
    { label: '排序号', value: String(node.sortOrder) },
    { label: '创建时间', value: formatDateTime(node.createdAt) },
  ]
})
</script>

<template>
  <section v-if="node" class="org-detail">
    <header class="org-detail__head">
      <div class="org-detail__titles">
        <h2>{{ node.name }}</h2>
        <p>{{ orgTypeLabels[node.type] }} · {{ parentName }}</p>
      </div>
      <div class="org-detail__actions">
        <el-tag :type="entityStatusTones[node.status]" size="small" effect="light">
          {{ entityStatusLabels[node.status] }}
        </el-tag>
        <el-button v-permission="'bas.admin.org.action'" size="small" @click="emit('edit')">
          编辑
        </el-button>
        <el-button
          v-permission="'bas.admin.org.action'"
          size="small"
          :type="node.status === 'Enabled' ? 'danger' : 'primary'"
          plain
          :disabled="toggleDisabled"
          :title="toggleDisabled ? '公司根节点不可停用' : undefined"
          @click="emit('toggle')"
        >
          {{ node.status === 'Enabled' ? '停用' : '启用' }}
        </el-button>
      </div>
    </header>

    <QlxDescriptionList :items="items" :column="3" />

    <div class="org-detail__children">
      <h3>下级组织（{{ node.children.length }}）</h3>
      <div v-if="node.children.length" class="org-chips">
        <button
          v-for="child in node.children"
          :key="child.id"
          type="button"
          class="org-chip"
          :class="{ 'is-off': child.status === 'Disabled' }"
          @click="emit('select-child', child.id)"
        >
          <AppIcon :name="orgTypeIcons[child.type]" :size="13" />
          {{ child.name }}
          <em class="qlx-numeric">{{ child.memberCount }}</em>
        </button>
      </div>
      <p v-else class="org-detail__empty">暂无下级组织，可通过「新增下级组织」细分团队。</p>
    </div>

    <p class="org-detail__note">
      组织停用后不删除历史数据；成员归属与历史单据保持原状，仅在选择组织中不可用。
    </p>
  </section>

  <section v-else class="org-detail org-detail--empty">
    <el-empty description="请选择左侧组织查看详情" />
  </section>
</template>

<style scoped>
.org-detail {
  padding: var(--qlx-space-4);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
}

.org-detail--empty {
  display: flex;
  min-height: 420px;
  align-items: center;
  justify-content: center;
}

.org-detail__head {
  display: flex;
  margin-bottom: var(--qlx-space-4);
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--qlx-space-4);
}

.org-detail__titles h2 {
  color: var(--qlx-brand-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-lg);
  font-weight: 600;
  letter-spacing: 0.04em;
}

.org-detail__titles p {
  margin-top: 2px;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.org-detail__actions {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  gap: var(--qlx-space-2);
}

.org-detail__children {
  margin-top: var(--qlx-space-5);
  padding-top: var(--qlx-space-4);
  border-top: 1px dashed var(--qlx-ink-200);
}

.org-detail__children h3 {
  margin-bottom: var(--qlx-space-3);
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-sm);
  font-weight: 600;
}

.org-chips {
  display: flex;
  flex-wrap: wrap;
  gap: var(--qlx-space-2);
}

.org-chip {
  display: inline-flex;
  padding: 5px 10px;
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-full);
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-700);
  font-size: var(--qlx-text-sm);
  align-items: center;
  gap: 6px;
  cursor: pointer;
  transition: border-color var(--qlx-duration-fast) var(--qlx-ease-out);
}

.org-chip:hover {
  border-color: var(--qlx-brand-300);
}

.org-chip.is-off {
  background: var(--qlx-canvas);
  color: var(--qlx-ink-400);
}

.org-chip em {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  font-style: normal;
}

.org-detail__empty,
.org-detail__note {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

.org-detail__note {
  margin-top: var(--qlx-space-4);
  padding-left: var(--qlx-space-3);
  border-left: 2px solid var(--qlx-gold-400);
}
</style>
