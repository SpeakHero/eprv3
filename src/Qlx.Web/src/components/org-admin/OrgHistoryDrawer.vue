<!--
  所属公司：四川淇利馨集团有限公司
  描述：组织版本历史抽屉——按版本展示生效时间 / 变更人 / 快照关键字段（GET /orgs/{id}/history）
  说明：受控展示组件；快照 JSON 只解析展示关键字段（名称 / 类型 / 状态 / 上级 / 版本），不整体渲染；
        类型与状态中文映射取自 org-admin-utils 与 bas-master-mappers（B-4）
-->
<script setup lang="ts">
import { computed } from 'vue'
import { formatDateTime } from '@/utils/format'
import { summarizeOrgSnapshot } from '@/utils/bas-master-mappers'
import type { OrgHistoryRow } from '@/types/bas-master'
import { basOrgStatusLabel, basOrgTypeLabel } from './org-admin-utils'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    items: OrgHistoryRow[]
    loading?: boolean
    /** 抽屉标题（默认含组织名称）。 */
    title?: string
  }>(),
  { loading: false, title: '组织版本历史' },
)

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
}>()

const visible = computed({
  get: () => props.modelValue,
  set: (open) => emit('update:modelValue', open),
})

/** 历史行 + 快照摘要（解析失败时摘要为 null，仅展示版本与变更人）。 */
const rows = computed(() =>
  props.items.map((row) => ({
    ...row,
    summary: summarizeOrgSnapshot(row.snapshotJson),
  })),
)
</script>

<template>
  <el-drawer v-model="visible" :title="title" size="520px" append-to-body>
    <div v-loading="loading" class="org-history">
      <el-empty v-if="!loading && rows.length === 0" description="暂无版本历史" :image-size="72" />
      <el-timeline v-else>
        <el-timeline-item
          v-for="row in rows"
          :key="row.id"
          :timestamp="formatDateTime(row.effectiveFrom)"
          placement="top"
          type="primary"
        >
          <div class="org-history__card">
            <header class="org-history__head">
              <span class="org-history__version qlx-numeric">v{{ row.version }}</span>
              <span class="org-history__changed">变更人：{{ row.changedBy || '—' }}</span>
            </header>
            <dl v-if="row.summary" class="org-history__meta">
              <div>
                <dt>名称</dt>
                <dd>{{ row.summary.name || '—' }}</dd>
              </div>
              <div>
                <dt>类型</dt>
                <dd>{{ basOrgTypeLabel(row.summary.type) }}</dd>
              </div>
              <div>
                <dt>状态</dt>
                <dd>{{ basOrgStatusLabel(row.summary.status) }}</dd>
              </div>
              <div>
                <dt>上级</dt>
                <dd class="qlx-numeric">{{ row.summary.parentId || '无（根节点）' }}</dd>
              </div>
            </dl>
            <p v-else class="org-history__raw">快照无法解析，已保留原始记录</p>
          </div>
        </el-timeline-item>
      </el-timeline>
    </div>
  </el-drawer>
</template>

<style scoped>
.org-history {
  min-height: 200px;
}

.org-history__card {
  padding: var(--qlx-space-3);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
}

.org-history__head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--qlx-space-2);
}

.org-history__version {
  color: var(--qlx-brand-700);
  font-size: var(--qlx-text-sm);
  font-weight: 600;
}

.org-history__changed {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.org-history__meta {
  display: grid;
  margin: var(--qlx-space-2) 0 0;
  gap: var(--qlx-space-1);
  grid-template-columns: repeat(2, minmax(0, 1fr));
}

.org-history__meta div {
  display: flex;
  gap: 6px;
  font-size: var(--qlx-text-xs);
}

.org-history__meta dt {
  color: var(--qlx-ink-400);
}

.org-history__meta dd {
  margin: 0;
  color: var(--qlx-ink-700);
}

.org-history__raw {
  margin-top: var(--qlx-space-2);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}
</style>
