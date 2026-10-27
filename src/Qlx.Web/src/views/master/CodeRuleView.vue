<!--
  所属公司：四川淇利馨集团有限公司
  描述：编码规则——规则内容只读展示，仅允许按业务类型启用 / 停用
  说明：
    1. 分段模板 JSON 属展示加工，由公共层 formatSegments 统一说明（B-4）；
    2. 后端契约无规则编辑端点，页面不提供任何规则内容输入（前缀 / 分段只读）。
-->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import QlxSearchBar from '@/components/crud/QlxSearchBar.vue'
import type { SearchField } from '@/components/crud/QlxSearchBar.vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import { codeRuleApi } from '@/api/basMaster'
import { CODE_RULE_BIZ_LABELS } from '@/types/bas-master'
import type { CodeRuleRow } from '@/types/bas-master'
import {
  basStatusLabel,
  basStatusTone,
  codeRuleBizTypeLabel,
  formatSegments,
  toCodeRuleRows,
} from '@/utils/bas-master-mappers'
import { formatDateTime } from '@/utils/format'

type CodeRuleTableRow = CodeRuleRow & { id: string }

const searchFields: SearchField[] = [
  {
    prop: 'bizType',
    label: '业务类型',
    type: 'select',
    options: Object.entries(CODE_RULE_BIZ_LABELS).map(([value, label]) => ({ label, value })),
  },
  {
    prop: 'status',
    label: '状态',
    type: 'select',
    options: [
      { label: '启用', value: '1' },
      { label: '停用', value: '2' },
    ],
  },
]

const loading = ref(false)
const rows = ref<CodeRuleTableRow[]>([])
const query = ref<Record<string, string>>({})

const filteredRows = computed(() =>
  rows.value.filter((row) => {
    if (query.value.bizType && row.bizType !== query.value.bizType) {
      return false
    }
    if (query.value.status && String(row.status) !== query.value.status) {
      return false
    }
    return true
  }),
)

const columns: Array<CrudColumn<CodeRuleTableRow>> = [
  { prop: 'bizType', label: '业务类型', width: 140 },
  { prop: 'prefix', label: '编码前缀', type: 'code', width: 140 },
  { prop: 'segmentsJson', label: '规则分段（只读）', minWidth: 260 },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 90,
    align: 'center',
    tagText: (row) => basStatusLabel(row.status),
    tagTone: (row) => basStatusTone(row.status),
  },
  { prop: 'updatedAt', label: '更新时间', type: 'time', width: 160 },
]

async function load(): Promise<void> {
  loading.value = true
  try {
    rows.value = toCodeRuleRows(await codeRuleApi.list())
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载编码规则失败')
  } finally {
    loading.value = false
  }
}

async function search(nextQuery: Record<string, string>): Promise<void> {
  query.value = nextQuery
}

async function reset(): Promise<void> {
  query.value = {}
}

async function toggle(row: CodeRuleTableRow): Promise<void> {
  const disabling = row.status === 1
  try {
    await ElMessageBox.confirm(
      disabling
        ? `停用「${codeRuleBizTypeLabel(row.bizType)}」编码规则后，该业务新增将无法自动生成编码。确认停用？`
        : `确认启用「${codeRuleBizTypeLabel(row.bizType)}」编码规则？`,
      disabling ? '停用编码规则' : '启用编码规则',
      { type: 'warning', confirmButtonText: '确认', cancelButtonText: '取消' },
    )
  } catch {
    return
  }
  try {
    if (disabling) {
      await codeRuleApi.disable(row.bizType)
    } else {
      await codeRuleApi.enable(row.bizType)
    }
    ElMessage.success(disabling ? '编码规则已停用' : '编码规则已启用')
    await load()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '切换编码规则状态失败')
  }
}

onMounted(() => {
  void load()
})
</script>

<template>
  <QlxPagePanel title="编码规则" subtitle="业务编码规则只读 · 按业务类型启停（规范 5.2.5）">
    <template #toolbar>
      <el-button :loading="loading" @click="load()">
        <AppIcon name="Refresh" :size="14" />
        刷新
      </el-button>
    </template>

    <el-alert
      type="info"
      :closable="false"
      show-icon
      title="规则内容由后端配置管理"
      description="编码前缀与分段模板为只读展示，本页仅提供启用 / 停用；停用后相应业务的新建操作将无法生成编码。"
      class="code-rule__alert"
    />

    <QlxSearchBar :fields="searchFields" :model-value="query" @search="search" @reset="reset" />

    <QlxDataTable
      :items="filteredRows"
      :columns="columns"
      :loading="loading"
      :total="filteredRows.length"
      :page-size="20"
      :actions-width="120"
      empty-text="暂无编码规则"
    >
      <template #cell-bizType="{ row }">{{ codeRuleBizTypeLabel(row.bizType) }}</template>
      <template #cell-segmentsJson="{ row }">{{ formatSegments(row.segmentsJson) }}</template>
      <template #cell-updatedAt="{ row }">{{ formatDateTime(row.updatedAt) }}</template>
      <template #actions="{ row }">
        <button
          v-permission="'bas.coderule.action'"
          type="button"
          class="cell-link"
          :class="{ 'is-danger': row.status === 1 }"
          @click="toggle(row)"
        >
          {{ row.status === 1 ? '停用' : '启用' }}
        </button>
      </template>
    </QlxDataTable>
  </QlxPagePanel>
</template>

<style scoped>
.code-rule__alert {
  margin-bottom: var(--qlx-space-3);
}

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

.cell-link.is-danger {
  color: var(--qlx-danger);
}
</style>
