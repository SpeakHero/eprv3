<!--
  所属公司：四川淇利馨集团有限公司
  描述：通用数据表格——配置驱动列（文本/枚举/时间/状态标签/可复制编码）+ 分页器 + 操作列插槽
  说明：与 useCrudTable 组合成完整 CRUD 列表页；自定义单元格用 #cell-{prop}，操作列用 #actions
-->
<script setup lang="ts" generic="T extends { id: string }">
import { ElMessage } from 'element-plus'
import { formatDateTime } from '@/utils/format'

export type TagTone = 'success' | 'warning' | 'danger' | 'info' | 'primary'

export interface CrudColumn<TRow> {
  prop: string
  label: string
  width?: number
  minWidth?: number
  align?: 'left' | 'center' | 'right'
  fixed?: 'left' | 'right'
  /** 展示类型：text 默认文本；enum 枚举映射；time 时间；tag 状态标签；code 可复制编码；check 勾选标记。 */
  type?: 'text' | 'enum' | 'time' | 'tag' | 'code' | 'check'
  /** enum 型取值映射。 */
  enumLabels?: Record<string, string>
  /** tag 型动态标签文本。 */
  tagText?: (row: TRow) => string
  /** tag 型动态色调。 */
  tagTone?: (row: TRow) => TagTone
  /** 表头灰字提示。 */
  tooltip?: string
}

withDefaults(
  defineProps<{
    items: T[]
    columns: Array<CrudColumn<T>>
    loading?: boolean
    total?: number
    page?: number
    pageSize?: number
    /** 无数据提示文案。 */
    emptyText?: string
    /** 操作列宽度（提供 #actions 插槽时展示）。 */
    actionsWidth?: number | string
  }>(),
  { loading: false, total: 0, page: 1, pageSize: 10, emptyText: '暂无数据', actionsWidth: 180 },
)

const emit = defineEmits<{
  'page-change': [page: number, pageSize: number]
}>()

/** 模板内取值辅助（避免模板中 as 断言）。 */
function valueAt(row: T, prop: string): unknown {
  return (row as Record<string, unknown>)[prop]
}

function cellText(column: CrudColumn<T>, row: T): string {
  const value = valueAt(row, column.prop)
  if (column.type === 'time') {
    return formatDateTime(value as string | null | undefined)
  }
  if (column.type === 'enum') {
    return column.enumLabels?.[String(value)] ?? String(value ?? '—')
  }
  if (value === null || value === undefined || value === '') {
    return '—'
  }
  return String(value)
}

async function copyCode(value: unknown): Promise<void> {
  const text = String(value ?? '')
  if (!text || text === '—') {
    return
  }
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success(`已复制：${text}`)
  } catch {
    ElMessage.warning('复制失败，请手动选择复制')
  }
}
</script>

<template>
  <div class="qlx-table">
    <el-table
      v-loading="loading"
      :data="items"
      row-key="id"
      table-layout="fixed"
      class="qlx-table__el"
    >
      <el-table-column
        v-for="column in columns"
        :key="column.prop"
        :prop="column.prop"
        :label="column.label"
        :width="column.width"
        :min-width="column.minWidth"
        :align="column.align ?? 'left'"
        :fixed="column.fixed"
        :show-overflow-tooltip="column.type !== 'tag'"
      >
        <template #default="{ row }">
          <slot :name="`cell-${column.prop}`" :row="row" :value="valueAt(row, column.prop)">
            <el-tag
              v-if="column.type === 'tag'"
              :type="column.tagTone?.(row) ?? 'info'"
              size="small"
              effect="light"
              class="qlx-table__tag"
            >
              {{ column.tagText?.(row) ?? cellText(column, row) }}
            </el-tag>
            <button
              v-else-if="column.type === 'code'"
              type="button"
              class="qlx-table__code qlx-numeric"
              :title="`复制 ${column.label}`"
              @click="copyCode(valueAt(row, column.prop))"
            >
              {{ cellText(column, row) }}
            </button>
            <span
              v-else-if="column.type === 'check'"
              class="qlx-table__check"
              :class="{ 'is-on': valueAt(row, column.prop) }"
              :aria-label="valueAt(row, column.prop) ? '已授权' : '未授权'"
            >
              {{ valueAt(row, column.prop) ? '✓' : '—' }}
            </span>
            <span v-else class="qlx-table__text" :class="{ 'is-strong': column.fixed === 'left' }">
              {{ cellText(column, row) }}
            </span>
          </slot>
        </template>
      </el-table-column>

      <el-table-column
        v-if="$slots.actions"
        label="操作"
        :width="actionsWidth"
        align="center"
        fixed="right"
      >
        <template #default="{ row }">
          <slot name="actions" :row="row" />
        </template>
      </el-table-column>

      <template #empty>
        <div class="qlx-table__empty">
          <p>{{ emptyText }}</p>
        </div>
      </template>
    </el-table>

    <div class="qlx-table__footer">
      <span class="qlx-table__total qlx-numeric">共 {{ total }} 条</span>
      <el-pagination
        :total="total"
        :current-page="page"
        :page-size="pageSize"
        :page-sizes="[10, 20, 50]"
        layout="sizes, prev, pager, next, jumper"
        background
        @current-change="(current: number) => emit('page-change', current, pageSize)"
        @size-change="(size: number) => emit('page-change', 1, size)"
      />
    </div>
  </div>
</template>

<style scoped>
.qlx-table {
  display: flex;
  min-height: 0;
  flex-direction: column;
}

.qlx-table__el {
  flex: 1;
}

.qlx-table__text.is-strong {
  color: var(--qlx-ink-800);
  font-weight: 500;
}

.qlx-table__tag {
  border: none;
  font-weight: 400;
}

.qlx-table__code {
  padding: 0;
  border: 0;
  background: none;
  color: var(--qlx-brand-600);
  font-size: var(--qlx-text-sm);
  cursor: copy;
}

.qlx-table__code:hover {
  text-decoration: underline;
}

.qlx-table__check {
  color: var(--qlx-ink-300);
  font-weight: 600;
}

.qlx-table__check.is-on {
  color: var(--qlx-success);
}

.qlx-table__empty {
  padding: var(--qlx-space-10) 0;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-sm);
}

.qlx-table__footer {
  display: flex;
  padding: var(--qlx-space-3) 0 0;
  align-items: center;
  justify-content: flex-end;
  gap: var(--qlx-space-4);
}

.qlx-table__total {
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
}
</style>
