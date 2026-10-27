<!--
  所属公司：四川淇利馨集团有限公司
  描述：组织详情卡（Bas 主数据版）——编码 / 四级类型 / 负责人 / 有效期 / 版本 / 审批状态 + 下级 chips
  说明：
    1. 受控展示组件；负责人名 / 上级名经 props 注入（页面用人员候选清单解析）；
    2. 类型 / 状态一律数值，中文映射与图标取自 org-admin-utils（B-4）；
    3. 停用 / 启用、版本历史、审批进度仅 emit 意图，确认框与请求由页面处理。
-->
<script setup lang="ts">
import { computed } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import QlxDescriptionList from '@/components/crud/QlxDescriptionList.vue'
import type { DescriptionItem } from '@/components/crud/QlxDescriptionList.vue'
import type { TagTone } from '@/components/crud/QlxDataTable.vue'
import { formatDateTime } from '@/utils/format'
import { formatEffectPeriod } from '@/utils/bas-master-mappers'
import type { ApprovalInstanceLite } from '@/utils/bas-master-mappers'
import { APPROVAL_STATUS_LABELS, APPROVAL_STATUS_TYPE } from '@/types/support'
import {
  basOrgStatusLabel,
  basOrgStatusTone,
  basOrgTypeIcon,
  basOrgTypeLabel,
  isBasOrgDisabled,
} from './org-admin-utils'
import type { BasOrgTreeNode } from './org-admin-utils'

const props = withDefaults(
  defineProps<{
    node: BasOrgTreeNode | null
    /** 上级组织显示名（根节点页面传「无（集团根节点）」）。 */
    parentName?: string
    /** 负责人显示名（页面用人员候选清单解析 leaderPersonId）。 */
    leaderName?: string
    /** 最近一次组织调整审批实例（按 bizId 从发起的审批列表归并，刷新后仍可见）。 */
    approvalState?: ApprovalInstanceLite | null
  }>(),
  { parentName: '—', leaderName: '', approvalState: null },
)

const emit = defineEmits<{
  edit: []
  toggle: []
  history: []
  'select-child': [id: string]
  'submit-disable-approval': []
  'view-approval': []
}>()

/** 根节点启用态下不可停用（后端 409 同规则，前端先行禁用）。 */
const toggleDisabled = computed(
  () => props.node !== null
    && props.node.parentId === null
    && !isBasOrgDisabled(props.node.status),
)

const items = computed<DescriptionItem[]>(() => {
  const node = props.node
  if (!node) {
    return []
  }
  return [
    { label: '组织编码', value: node.code },
    { label: '组织类型', value: basOrgTypeLabel(node.type) },
    { label: '上级组织', value: props.parentName },
    { label: '负责人', value: props.leaderName || '未指定' },
    { label: '有效期', value: formatEffectPeriod(node.effectDate, node.expiryDate) },
    { label: '数据版本', value: `v${node.version}` },
    { label: '排序号', value: String(node.sortOrder) },
    { label: '更新时间', value: formatDateTime(node.updatedAt) },
  ]
})

/** 审批状态中文 / 色调（复用全局 ApprovalStatusCode 映射表，不重复造字典）。 */
const approvalStatusLabel = computed<string>(() => {
  const status = props.approvalState?.status
  if (status === undefined) {
    return ''
  }
  return (APPROVAL_STATUS_LABELS as Record<number, string>)[status] ?? '—'
})

const approvalStatusTone = computed<TagTone>(() => {
  const status = props.approvalState?.status
  if (status === undefined) {
    return 'info'
  }
  return ((APPROVAL_STATUS_TYPE as Record<number, string>)[status] ?? 'info') as TagTone
})
</script>

<template>
  <section v-if="node" class="org-detail">
    <header class="org-detail__head">
      <div class="org-detail__titles">
        <h2>{{ node.name }}</h2>
        <p>
          <AppIcon :name="basOrgTypeIcon(node.type)" :size="13" />
          {{ basOrgTypeLabel(node.type) }} · {{ parentName }}
          <span class="qlx-numeric">· {{ node.code }}</span>
        </p>
      </div>
      <div class="org-detail__actions">
        <el-tag :type="basOrgStatusTone(node.status)" size="small" effect="light">
          {{ basOrgStatusLabel(node.status) }}
        </el-tag>
        <el-button v-permission="'bas.admin.org.action'" size="small" @click="emit('edit')">
          编辑
        </el-button>
        <el-button v-permission="'bas.admin.org.action'" size="small" plain @click="emit('history')">
          版本历史
        </el-button>
        <el-button
          v-permission="'bas.admin.org.action'"
          size="small"
          plain
          @click="emit('submit-disable-approval')"
        >
          调整审批
        </el-button>
        <el-button
          v-permission="'bas.admin.org.action'"
          size="small"
          :type="node.status === 1 ? 'danger' : 'primary'"
          plain
          :disabled="toggleDisabled"
          :title="toggleDisabled ? '集团根节点不可停用' : undefined"
          @click="emit('toggle')"
        >
          {{ node.status === 1 ? '停用' : '启用' }}
        </el-button>
      </div>
    </header>

    <QlxDescriptionList :items="items" :column="3" />

    <div v-if="approvalState" class="org-detail__approval">
      <span class="org-detail__approval-label">调整审批</span>
      <el-tag :type="approvalStatusTone" size="small" effect="light">
        {{ approvalStatusLabel }}
      </el-tag>
      <span class="org-detail__approval-meta">
        申请时间 {{ formatDateTime(approvalState.startedAt) }}
      </span>
      <el-button size="small" text type="primary" @click="emit('view-approval')">
        查看审批进度
      </el-button>
    </div>

    <div class="org-detail__children">
      <h3>下级组织（{{ node.children.length }}）</h3>
      <div v-if="node.children.length" class="org-chips">
        <button
          v-for="child in node.children"
          :key="child.id"
          type="button"
          class="org-chip"
          :class="{ 'is-off': isBasOrgDisabled(child.status) }"
          @click="emit('select-child', child.id)"
        >
          <AppIcon :name="basOrgTypeIcon(child.type)" :size="13" />
          {{ child.name }}
          <em class="qlx-numeric">{{ child.code }}</em>
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
  display: flex;
  margin-top: 2px;
  align-items: center;
  gap: 4px;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.org-detail__actions {
  display: flex;
  flex: 0 0 auto;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--qlx-space-2);
}

.org-detail__approval {
  display: flex;
  margin-top: var(--qlx-space-3);
  padding: var(--qlx-space-2) var(--qlx-space-3);
  border-left: 3px solid var(--qlx-gold-500);
  background: var(--qlx-canvas);
  align-items: center;
  gap: var(--qlx-space-2);
}

.org-detail__approval-label {
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-sm);
}

.org-detail__approval-meta {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
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
