<!--
  所属公司：四川淇利馨集团有限公司
  描述：审批时间线——按任务序展示节点 / 审批人 / 状态 / 意见 / 时间（对齐 ApprovalTaskInfo 契约）
-->
<script setup lang="ts">
import { computed } from 'vue'
import { formatDateTime } from '@/utils/format'
import { APPROVAL_TASK_STATUS_LABELS } from './types'
import type { ApprovalInstanceInfo, ApprovalTaskStatus, ApprovalUserOption } from './types'

const props = defineProps<{
  instance: ApprovalInstanceInfo | null
  /** userId → 展示名（父级经服务解析）。 */
  users?: ApprovalUserOption[]
}>()

interface TimelineItem {
  taskId: string
  nodeName: string
  assignee: string
  status: ApprovalTaskStatus
  statusLabel: string
  createdAt: string
  completedAt: string
  comment: string
}

const items = computed<TimelineItem[]>(() => {
  if (!props.instance) {
    return []
  }
  const nameOf = (userId: string): string =>
    props.users?.find((user) => user.userId === userId)?.userName ?? userId
  return props.instance.tasks.map((task) => ({
    taskId: task.taskId,
    nodeName: task.nodeName,
    assignee: nameOf(task.assigneeUserId),
    status: task.status,
    statusLabel: APPROVAL_TASK_STATUS_LABELS[task.status],
    createdAt: formatDateTime(task.createdAt),
    completedAt: formatDateTime(task.completedAt ?? null, ''),
    comment: task.comment ?? '',
  }))
})

const iconType = (status: ApprovalTaskStatus): 'success' | 'danger' | 'warning' | 'info' | 'primary' => {
  switch (status) {
    case 'Approved':
      return 'success'
    case 'Rejected':
      return 'danger'
    case 'Pending':
      return 'warning'
    case 'Canceled':
      return 'info'
    default:
      return 'primary'
  }
}
</script>

<template>
  <div class="approval-timeline">
    <el-empty v-if="items.length === 0" description="暂无审批记录" :image-size="64" />
    <el-timeline v-else>
      <el-timeline-item
        v-for="item in items"
        :key="item.taskId"
        :type="iconType(item.status)"
        :hollow="item.status === 'Pending'"
        :timestamp="item.completedAt || item.createdAt"
        placement="top"
      >
        <div class="approval-timeline__card">
          <div class="approval-timeline__row">
            <span class="approval-timeline__node">{{ item.nodeName }}</span>
            <el-tag size="small" :type="iconType(item.status)">{{ item.statusLabel }}</el-tag>
          </div>
          <div class="approval-timeline__meta">
            审批人：{{ item.assignee }} · 发起：{{ item.createdAt }}
            <template v-if="item.completedAt"> · 完成：{{ item.completedAt }}</template>
          </div>
          <p v-if="item.comment" class="approval-timeline__comment">意见：{{ item.comment }}</p>
        </div>
      </el-timeline-item>
    </el-timeline>
  </div>
</template>

<style scoped>
.approval-timeline {
  padding: var(--qlx-space-2) 0;
}

.approval-timeline__card {
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
  padding: var(--qlx-space-3);
  display: grid;
  gap: var(--qlx-space-1);
}

.approval-timeline__row {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
}

.approval-timeline__node {
  font-weight: 600;
  font-size: var(--qlx-text-sm);
  color: var(--qlx-ink-800);
}

.approval-timeline__meta {
  font-size: var(--qlx-text-xs);
  color: var(--qlx-ink-400);
}

.approval-timeline__comment {
  margin: 0;
  font-size: var(--qlx-text-sm);
  color: var(--qlx-ink-700);
  background: var(--qlx-canvas);
  border-radius: var(--qlx-radius-sm);
  padding: var(--qlx-space-1) var(--qlx-space-2);
}
</style>
