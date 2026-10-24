<!--
  所属公司：四川淇利馨集团有限公司
  描述：门户管理 · 租户管理——租户列表（搜索/新建/启停/详情），复用通用 CRUD 组件
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
import { ApiError, createTenant, fetchTenants, toggleTenantStatus } from '@/api/admin'
import {
  databaseModeLabels,
  databaseModeTones,
  dbStatusLabels,
  memberLevelLabels,
  tenantStatusLabels,
  tenantStatusTones,
} from '@/types/admin'
import type { DatabaseMode, MemberLevel, PortalTenant } from '@/types/admin'
import { ADMIN_ACCOUNT_PATTERN, CREDIT_CODE_PATTERN } from '@/utils/validators'

const searchFields: SearchField[] = [
  {
    prop: 'keyword',
    label: '关键词',
    type: 'input',
    placeholder: '租户名称 / 编码 / 统一社会信用代码',
    width: 260,
  },
  {
    prop: 'status',
    label: '状态',
    type: 'select',
    options: Object.entries(tenantStatusLabels).map(([value, label]) => ({ value, label })),
  },
  {
    prop: 'databaseMode',
    label: '数据库模式',
    type: 'select',
    options: Object.entries(databaseModeLabels).map(([value, label]) => ({ value, label })),
  },
]

const columns: Array<CrudColumn<PortalTenant>> = [
  { prop: 'name', label: '租户名称', minWidth: 220, fixed: 'left' },
  { prop: 'code', label: '租户编码', type: 'code', width: 130 },
  { prop: 'taxNo', label: '统一社会信用代码', type: 'code', width: 190 },
  { prop: 'status', label: '状态', type: 'tag', width: 100, align: 'center', tagText: (row) => tenantStatusLabels[row.status], tagTone: (row) => tenantStatusTones[row.status] },
  { prop: 'memberLevel', label: '会员等级', type: 'enum', width: 100, enumLabels: memberLevelLabels },
  { prop: 'databaseMode', label: '数据库模式', type: 'tag', width: 116, align: 'center', tagText: (row) => databaseModeLabels[row.databaseMode], tagTone: (row) => databaseModeTones[row.databaseMode] },
  { prop: 'dbStatus', label: '库状态', type: 'tag', width: 96, align: 'center', tagText: (row) => dbStatusLabels[row.dbStatus], tagTone: (row) => (row.dbStatus === 'Ready' ? 'success' : row.dbStatus === 'Provisioning' ? 'warning' : 'danger') },
  { prop: 'licensedModuleCodes', label: '已授权模块', width: 110, align: 'center' },
  { prop: 'createdAt', label: '创建时间', type: 'time', width: 150 },
  { prop: 'lastLoginAt', label: '最近登录', type: 'time', width: 150 },
]

const { loading, items, total, page, pageSize, query, search, reset, refresh, changePage } =
  useCrudTable<PortalTenant>(fetchTenants)

/** 新建租户弹窗。 */
const createOpen = ref(false)
const submitting = ref(false)
/** 服务端 422 字段级校验错误（键为字段 prop，打开弹窗或重新提交时清空）。 */
const createFieldErrors = ref<Record<string, string> | undefined>()
const createFields: FormField[] = [
  { prop: 'name', label: '租户名称', type: 'input', required: true, full: true, placeholder: '企业全称（营业执照名称）', maxlength: 128 },
  { prop: 'taxNo', label: '社会信用代码', type: 'input', required: true, placeholder: '18 位统一社会信用代码（唯一）', maxlength: 32, pattern: CREDIT_CODE_PATTERN, patternMessage: '请填写 18 位统一社会信用代码' },
  {
    prop: 'memberLevel',
    label: '会员等级',
    type: 'select',
    defaultValue: 'Trial',
    options: Object.entries(memberLevelLabels).map(([value, label]) => ({ value, label })),
  },
  {
    prop: 'databaseMode',
    label: '数据库模式',
    type: 'select',
    defaultValue: 'Shared',
    options: Object.entries(databaseModeLabels).map(([value, label]) => ({ value, label })),
    hint: '租户共享库成本最低（默认），独立库隔离性更强但资源开销更高；共享租户后续可升级为独立库，独立库创建后无法回退共享库',
  },
  {
    prop: 'adminAccount',
    label: '管理员账号',
    type: 'input',
    required: true,
    placeholder: '手机号或邮箱',
    pattern: ADMIN_ACCOUNT_PATTERN,
    patternMessage: '请填写手机号或邮箱',
    hint: '系统将自动匹配既有自然人账号；不存在则创建，并授予 TENANT_ADMIN 角色',
  },
]

async function onCreate(payload: Record<string, FormValue>): Promise<void> {
  submitting.value = true
  createFieldErrors.value = undefined
  try {
    const mode = (payload.databaseMode as DatabaseMode | undefined) ?? 'Shared'
    const tenant = await createTenant({
      name: String(payload.name),
      taxNo: String(payload.taxNo),
      memberLevel: payload.memberLevel as MemberLevel,
      adminAccount: String(payload.adminAccount),
      databaseMode: mode,
    })
    createOpen.value = false
    const tail = mode === 'Isolated' ? '，正在初始化独立数据库' : ''
    ElMessage.success(`租户「${tenant.name}」创建成功${tail}`)
    await refresh()
  } catch (error) {
    // 422 校验失败：字段级错误透出到表单对应字段旁，toast 仅保留总提示
    if (error instanceof ApiError && error.fieldErrors?.length) {
      const map: Record<string, string> = {}
      for (const item of error.fieldErrors) {
        map[item.fieldName] = item.errorMessage
      }
      createFieldErrors.value = map
    }
    ElMessage.error(error instanceof Error ? error.message : '创建租户失败')
  } finally {
    submitting.value = false
  }
}

async function onToggleStatus(row: PortalTenant): Promise<void> {
  const disabling = row.status === 'Enabled'
  try {
    await ElMessageBox.confirm(
      disabling
        ? `停用后「${row.name}」下所有用户将无法登录，是否继续？`
        : `启用「${row.name}」后，租户下用户可恢复正常登录，是否继续？`,
      disabling ? '停用租户' : '启用租户',
      { confirmButtonText: disabling ? '确认停用' : '确认启用', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    const updated = await toggleTenantStatus(row.id)
    ElMessage.success(`租户「${updated.name}」已${tenantStatusLabels[updated.status] ?? '更新'}`)
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}
</script>

<template>
  <QlxPagePanel
    title="租户管理"
    subtitle="租户的注册、开通、停用与模块许可（门户全局管控职责，规范 4.2.1）"
  >
    <template #toolbar>
      <el-button type="primary" @click="createFieldErrors = undefined; createOpen = true">新建租户</el-button>
    </template>

    <QlxSearchBar :fields="searchFields" :model-value="query" @search="search" @reset="reset" />

    <QlxDataTable
      :items="items"
      :columns="columns"
      :loading="loading"
      :total="total"
      :page="page"
      :page-size="pageSize"
      :actions-width="200"
      empty-text="暂无符合条件的租户"
      @page-change="changePage"
    >
      <template #cell-licensedModuleCodes="{ value }">
        <span class="qlx-numeric">{{ (value as string[]).length }} 个</span>
      </template>
      <template #actions="{ row }">
        <a class="cell-link" :href="`/portal/tenants/${row.id}`" target="_blank" rel="noopener">详情</a>
        <!-- 仅 Active/Disabled 支持启停切换；预配中/预配失败等状态不暴露无效操作 -->
        <template v-if="row.status === 'Enabled' || row.status === 'Disabled'">
          <el-divider direction="vertical" />
          <button
            type="button"
            class="cell-link"
            :class="{ 'is-danger': row.status === 'Enabled' }"
            @click="onToggleStatus(row)"
          >
            {{ row.status === 'Enabled' ? '停用' : '启用' }}
          </button>
        </template>
      </template>
    </QlxDataTable>

    <QlxFormDialog
      v-model="createOpen"
      title="新建租户"
      :fields="createFields"
      :submitting="submitting"
      :field-errors="createFieldErrors"
      submit-text="创建租户"
      :width="640"
      @submit="onCreate"
    />
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

.cell-link.is-danger {
  color: var(--qlx-danger);
}
</style>
