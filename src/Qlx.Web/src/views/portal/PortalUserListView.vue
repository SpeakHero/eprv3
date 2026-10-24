<!--
  所属公司：四川淇利馨集团有限公司
  描述：门户管理 · 用户管理——自然人全局身份（PortalUser）列表：搜索/全局启停/重置密码/详情
-->
<script setup lang="ts">
import { ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import QlxFormDialog from '@/components/crud/QlxFormDialog.vue'
import type { FormField, FormValue } from '@/components/crud/QlxFormDialog.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import QlxSearchBar from '@/components/crud/QlxSearchBar.vue'
import type { SearchField } from '@/components/crud/QlxSearchBar.vue'
import { useCrudTable } from '@/composables/useCrudTable'
import { fetchUsers, resetUserPassword, toggleUserStatus } from '@/api/admin'
import { accountStatusLabels } from '@/types/admin'
import type { PortalUser } from '@/types/admin'
import { yesNo } from '@/utils/format'

const searchFields: SearchField[] = [
  {
    prop: 'keyword',
    label: '关键词',
    type: 'input',
    placeholder: '登录账号 / 显示名 / 手机号 / 邮箱',
    width: 260,
  },
  {
    prop: 'status',
    label: '状态',
    type: 'select',
    options: Object.entries(accountStatusLabels).map(([value, label]) => ({ value, label })),
  },
]

const columns: Array<CrudColumn<PortalUser>> = [
  { prop: 'loginAccount', label: '登录账号', type: 'code', width: 150, fixed: 'left' },
  { prop: 'displayName', label: '显示名', width: 130 },
  { prop: 'phone', label: '手机号', type: 'code', width: 140 },
  { prop: 'email', label: '邮箱', minWidth: 180 },
  { prop: 'status', label: '状态', type: 'tag', width: 90, align: 'center', tagText: (row) => accountStatusLabels[row.status], tagTone: (row) => (row.status === 'Enabled' ? 'success' : 'danger') },
  { prop: 'isPortalAdmin', label: '门户管理员', width: 100, align: 'center' },
  { prop: 'realNameVerified', label: '已实名', width: 80, align: 'center' },
  { prop: 'lastLoginAt', label: '最近登录', type: 'time', width: 150 },
  { prop: 'createdAt', label: '创建时间', type: 'time', width: 150 },
]

const { loading, items, total, page, pageSize, query, search, reset, refresh, changePage } =
  useCrudTable<PortalUser>(fetchUsers)

async function onToggleStatus(row: PortalUser): Promise<void> {
  const disabling = row.status === 'Enabled'
  try {
    await ElMessageBox.confirm(
      disabling
        ? `全局停用「${row.displayName}（${row.loginAccount}）」后，该自然人将无法登录任何租户上下文，是否继续？`
        : `启用「${row.displayName}（${row.loginAccount}）」后可恢复登录，是否继续？`,
      disabling ? '全局停用自然人' : '启用自然人',
      { confirmButtonText: disabling ? '确认停用' : '确认启用', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await toggleUserStatus(row.id)
    ElMessage.success(disabling ? '已全局停用（依据 portal_users.status 全局生效）' : '已启用')
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

/** 重置密码弹窗（按行记忆目标用户）。 */
const resetOpen = ref(false)
const resetSubmitting = ref(false)
const resetTarget = ref<PortalUser | null>(null)
const resetFields: FormField[] = [
  {
    prop: 'newPassword',
    label: '新密码',
    type: 'input',
    required: true,
    full: true,
    maxlength: 64,
    pattern: /^(?=.*[A-Za-z])(?=.*\d).{8,}$/,
    patternMessage: '密码至少 8 位，需同时包含字母与数字',
    hint: '重置后统一生效；该自然人所有旧会话随 token_version 递增全部失效',
  },
]

function openReset(row: PortalUser): void {
  resetTarget.value = row
  resetOpen.value = true
}

async function onResetPassword(payload: Record<string, FormValue>): Promise<void> {
  if (!resetTarget.value) {
    return
  }
  resetSubmitting.value = true
  try {
    await resetUserPassword(resetTarget.value.id, { newPassword: String(payload.newPassword) })
    resetOpen.value = false
    ElMessage.success(`已重置「${resetTarget.value.displayName}」的统一密码`)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '重置密码失败')
  } finally {
    resetSubmitting.value = false
  }
}
</script>

<template>
  <QlxPagePanel
    title="用户管理"
    subtitle="自然人全局身份（PortalUser）管理：全局启停、重置统一密码（规范 4.2.2）"
  >
    <QlxSearchBar :fields="searchFields" :model-value="query" @search="search" @reset="reset" />

    <QlxDataTable
      :items="items"
      :columns="columns"
      :loading="loading"
      :total="total"
      :page="page"
      :page-size="pageSize"
      :actions-width="220"
      empty-text="暂无符合条件的用户"
      @page-change="changePage"
    >
      <template #cell-isPortalAdmin="{ value }">
        <el-tag v-if="value" type="warning" size="small" effect="light">平台管理员</el-tag>
        <span v-else class="plain">{{ yesNo(value as boolean) }}</span>
      </template>
      <template #cell-realNameVerified="{ value }">
        <span class="plain">{{ yesNo(value as boolean) }}</span>
      </template>
      <template #actions="{ row }">
        <a class="cell-link" :href="`/portal/users/${row.id}`" target="_blank" rel="noopener">详情</a>
        <el-divider direction="vertical" />
        <button type="button" class="cell-link" @click="openReset(row)">重置密码</button>
        <el-divider direction="vertical" />
        <button
          type="button"
          class="cell-link"
          :class="{ 'is-danger': row.status === 'Enabled' }"
          :disabled="row.isPortalAdmin"
          :title="row.isPortalAdmin ? '门户管理员账号不允许在此停用' : undefined"
          @click="onToggleStatus(row)"
        >
          {{ row.status === 'Enabled' ? '停用' : '启用' }}
        </button>
      </template>
    </QlxDataTable>

    <QlxFormDialog
      v-model="resetOpen"
      :title="`重置密码${resetTarget ? ` · ${resetTarget.displayName}` : ''}`"
      :fields="resetFields"
      :submitting="resetSubmitting"
      submit-text="确认重置"
      :width="480"
      @submit="onResetPassword"
    />
  </QlxPagePanel>
</template>

<style scoped>
.plain {
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

.cell-link:disabled {
  color: var(--qlx-ink-300);
  cursor: not-allowed;
  text-decoration: none;
}
</style>
