<!--
  所属公司：四川淇利馨集团有限公司
  描述：成员列表——QlxDataTable 封装，列 / 操作按行状态收敛（availableMemberActions 纯函数）
  说明：受控展示组件；组织名 / 角色名经解析函数注入（B-2：后端只下发 orgId / roleCodes 原始字段），
        详情 / 启停 / 重置 / 移除 / 重发邀请意图经 emits 抛出（确认框由页面触发）
-->
<script setup lang="ts">
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import type { MemberItem } from '@/types/org-admin'
import { memberRoleLabels } from '@/types/admin'
import { entityStatusLabels, entityStatusTones } from '@/types/tenant'
import { availableMemberActions } from './member-admin-utils'
import type { MemberAction } from './member-admin-utils'

withDefaults(
  defineProps<{
    items: MemberItem[]
    loading?: boolean
    total?: number
    page?: number
    pageSize?: number
    /** 组织 Id → 显示名。 */
    resolveOrgName?: (orgId: string | null) => string
    /** 角色码 → 显示名。 */
    resolveRoleName?: (code: string) => string
  }>(),
  {
    loading: false,
    total: 0,
    page: 1,
    pageSize: 10,
    resolveOrgName: (orgId: string | null) => orgId ?? '—',
    resolveRoleName: (code: string) => code,
  },
)

const emit = defineEmits<{
  detail: [row: MemberItem]
  toggle: [row: MemberItem]
  reset: [row: MemberItem]
  remove: [row: MemberItem]
  resend: [row: MemberItem]
  'page-change': [page: number, pageSize: number]
}>()

const columns: Array<CrudColumn<MemberItem>> = [
  { prop: 'displayName', label: '成员', width: 140, fixed: 'left' },
  { prop: 'loginAccount', label: '登录账号', type: 'code', width: 180 },
  { prop: 'orgId', label: '所属组织', minWidth: 150 },
  { prop: 'roleCodes', label: '角色', minWidth: 200 },
  {
    prop: 'memberRole',
    label: '成员角色',
    type: 'enum',
    enumLabels: memberRoleLabels,
    width: 110,
  },
  {
    prop: 'memberStatus',
    label: '状态',
    type: 'tag',
    width: 90,
    align: 'center',
    tagText: (row) => entityStatusLabels[row.memberStatus],
    tagTone: (row) => entityStatusTones[row.memberStatus],
  },
  { prop: 'joinAt', label: '加入时间', type: 'time', width: 150 },
  { prop: 'lastLoginAt', label: '最近登录', type: 'time', width: 150 },
]

/** 操作可用性（按 availableMemberActions 纯函数收敛）。 */
function has(row: MemberItem, action: MemberAction): boolean {
  return availableMemberActions(row).includes(action)
}

function toggleTitle(row: MemberItem): string | undefined {
  if (row.memberRole === 'TenantAdmin') {
    return '企业管理员不可在租户内停用'
  }
  return undefined
}

function removeTitle(row: MemberItem): string | undefined {
  if (row.memberRole === 'TenantAdmin') {
    return '企业管理员不可移除'
  }
  return undefined
}
</script>

<template>
  <QlxDataTable
    :items="items"
    :columns="columns"
    :loading="loading"
    :total="total"
    :page="page"
    :page-size="pageSize"
    :actions-width="250"
    empty-text="暂无符合条件的成员"
    @page-change="(p: number, s: number) => emit('page-change', p, s)"
  >
    <template #cell-orgId="{ row }">
      {{ resolveOrgName(row.orgId) }}
    </template>

    <template #cell-roleCodes="{ row }">
      <span class="role-tags">
        <el-tag
          v-for="code in row.roleCodes"
          :key="code"
          size="small"
          effect="plain"
          class="role-tags__item"
        >
          {{ resolveRoleName(code) }}
        </el-tag>
        <span v-if="!row.roleCodes.length" class="role-tags__plain">—</span>
      </span>
    </template>

    <template #actions="{ row }">
      <button type="button" class="cell-link" @click="emit('detail', row)">详情</button>
      <template v-if="has(row, 'resend')">
        <el-divider direction="vertical" />
        <button
          v-permission="'bas.admin.member.action'"
          type="button"
          class="cell-link"
          @click="emit('resend', row)"
        >
          重发邀请
        </button>
      </template>
      <template v-if="has(row, 'toggle')">
        <el-divider direction="vertical" />
        <button
          v-permission="'bas.admin.member.action'"
          type="button"
          class="cell-link"
          :class="{ 'is-danger': row.memberStatus === 'Enabled' }"
          :title="toggleTitle(row)"
          @click="emit('toggle', row)"
        >
          {{ row.memberStatus === 'Enabled' ? '停用' : '启用' }}
        </button>
      </template>
      <template v-if="has(row, 'reset')">
        <el-divider direction="vertical" />
        <button
          v-permission="'bas.admin.member.action'"
          type="button"
          class="cell-link"
          @click="emit('reset', row)"
        >
          重置密码
        </button>
      </template>
      <template v-if="has(row, 'remove')">
        <el-divider direction="vertical" />
        <button
          v-permission="'bas.admin.member.action'"
          type="button"
          class="cell-link is-danger"
          :title="removeTitle(row)"
          @click="emit('remove', row)"
        >
          移除
        </button>
      </template>
    </template>
  </QlxDataTable>
</template>

<style scoped>
.role-tags {
  display: inline-flex;
  flex-wrap: wrap;
  gap: 4px;
}

.role-tags__item {
  border-color: var(--qlx-brand-100);
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-700);
}

.role-tags__plain {
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-sm);
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
