<!--
  所属公司：四川淇利馨集团有限公司
  描述：变更日志——按对象类型 / 操作人 / 时间区间筛选的分页审计流水
  说明：
    1. 快照 JSON（oldValues / newValues）为展示加工，统一由公共层 prettyJson 美化（B-4）；
    2. 日志只读，页面不提供任何写操作。
-->
<script setup lang="ts">
import { computed, ref } from 'vue'
import { ElMessage } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import QlxSearchBar from '@/components/crud/QlxSearchBar.vue'
import type { SearchField } from '@/components/crud/QlxSearchBar.vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import { changeLogApi } from '@/api/basMaster'
import { useCrudTable } from '@/composables/useCrudTable'
import type { ChangeLogRow } from '@/types/bas-master'
import {
  CHANGE_LOG_ENTITY_LABELS,
  changeLogEntityLabel,
  changeLogOperationLabel,
  prettyJson,
  toChangeLogRows,
} from '@/utils/bas-master-mappers'
import { formatDateTime } from '@/utils/format'

type ChangeLogTableRow = ChangeLogRow & { id: string }

const searchFields: SearchField[] = [
  {
    prop: 'entityType',
    label: '对象类型',
    type: 'select',
    options: Object.entries(CHANGE_LOG_ENTITY_LABELS).map(([value, label]) => ({ label, value })),
    width: 160,
  },
  { prop: 'changedBy', label: '操作人', type: 'input', placeholder: '用户 Id / 姓名', width: 180 },
  { prop: 'from', label: '开始日期', type: 'date', width: 150 },
  { prop: 'to', label: '结束日期', type: 'date', width: 150 },
]

const {
  loading,
  items,
  total,
  page,
  pageSize,
  query,
  search,
  reset,
  refresh,
  changePage,
} = useCrudTable<ChangeLogTableRow>(async (params) => {
  const result = await changeLogApi.list({
    page: params.page,
    pageSize: params.pageSize,
    entityType:
      typeof params.entityType === 'string' && params.entityType ? params.entityType : undefined,
    changedBy:
      typeof params.changedBy === 'string' && params.changedBy ? params.changedBy : undefined,
    from: typeof params.from === 'string' && params.from ? params.from : undefined,
    to: typeof params.to === 'string' && params.to ? params.to : undefined,
  })
  return { ...result, items: toChangeLogRows(result.items) }
})

const columns: Array<CrudColumn<ChangeLogTableRow>> = [
  { prop: 'changedAt', label: '变更时间', type: 'time', width: 170, fixed: 'left' },
  { prop: 'entityType', label: '对象类型', width: 130 },
  { prop: 'operation', label: '操作', width: 120 },
  { prop: 'entityId', label: '对象 Id', type: 'code', width: 170 },
  { prop: 'changedBy', label: '操作人', width: 150 },
]

/** ============ 快照详情 ============ */

const detailOpen = ref(false)
const detailRow = ref<ChangeLogTableRow | null>(null)

const oldValuesText = computed(() => prettyJson(detailRow.value?.oldValues))
const newValuesText = computed(() => prettyJson(detailRow.value?.newValues))

function openDetail(row: ChangeLogTableRow): void {
  detailRow.value = row
  detailOpen.value = true
}

function onRefresh(): void {
  void refresh()
  ElMessage.success('已刷新')
}
</script>

<template>
  <QlxPagePanel title="变更日志" subtitle="主数据审计流水 · 对象类型 / 操作人 / 时间区间筛选（规范 5.2.6）">
    <template #toolbar>
      <el-button :loading="loading" @click="onRefresh">
        <AppIcon name="Refresh" :size="14" />
        刷新
      </el-button>
    </template>

    <QlxSearchBar :fields="searchFields" :model-value="query" @search="search" @reset="reset" />

    <QlxDataTable
      :items="items"
      :columns="columns"
      :loading="loading"
      :total="total"
      :page="page"
      :page-size="pageSize"
      :actions-width="110"
      empty-text="暂无变更日志"
      @page-change="changePage"
    >
      <template #cell-changedAt="{ row }">{{ formatDateTime(row.changedAt) }}</template>
      <template #cell-entityType="{ row }">{{ changeLogEntityLabel(row.entityType) }}</template>
      <template #cell-operation="{ row }">{{ changeLogOperationLabel(row.operation) }}</template>
      <template #actions="{ row }">
        <button type="button" class="cell-link" @click="openDetail(row)">变更详情</button>
      </template>
    </QlxDataTable>

    <el-dialog v-model="detailOpen" title="变更详情" width="760" append-to-body>
      <template v-if="detailRow">
        <el-descriptions :column="2" border class="change-log__head">
          <el-descriptions-item label="变更时间">
            {{ formatDateTime(detailRow.changedAt) }}
          </el-descriptions-item>
          <el-descriptions-item label="操作人">{{ detailRow.changedBy }}</el-descriptions-item>
          <el-descriptions-item label="对象类型">
            {{ changeLogEntityLabel(detailRow.entityType) }}
          </el-descriptions-item>
          <el-descriptions-item label="操作">
            {{ changeLogOperationLabel(detailRow.operation) }}
          </el-descriptions-item>
          <el-descriptions-item label="对象 Id" :span="2">
            <span class="qlx-numeric">{{ detailRow.entityId }}</span>
          </el-descriptions-item>
        </el-descriptions>

        <div class="change-log__snapshots">
          <section>
            <h4>变更前</h4>
            <pre class="change-log__json">{{ oldValuesText }}</pre>
          </section>
          <section>
            <h4>变更后</h4>
            <pre class="change-log__json">{{ newValuesText }}</pre>
          </section>
        </div>
      </template>
    </el-dialog>
  </QlxPagePanel>
</template>

<style scoped>
.cell-link {
  padding: 0;
  border: 0;
  background: none;
  color: var(--qlx-brand-600);
  font-size: var(--qlx-text-sm);
  cursor: pointer;
}

.cell-link:hover {
  text-decoration: underline;
}

.change-log__head {
  margin-bottom: var(--qlx-space-4);
}

.change-log__snapshots {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: var(--qlx-space-4);
}

.change-log__snapshots h4 {
  margin-bottom: var(--qlx-space-2);
  color: var(--qlx-ink-700);
  font-size: var(--qlx-text-sm);
  font-weight: 600;
}

.change-log__json {
  max-height: 360px;
  margin: 0;
  padding: var(--qlx-space-3);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-sm);
  background: var(--qlx-canvas);
  color: var(--qlx-ink-700);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
  overflow: auto;
  white-space: pre-wrap;
  word-break: break-all;
}

@media (max-width: 767px) {
  .change-log__snapshots {
    grid-template-columns: 1fr;
  }
}
</style>
