<!--
  所属公司：四川淇利馨集团有限公司
  描述：人员主数据——分页列表（脱敏展示）+ 新建 / 编辑 + 离职 / 再入职 + 明文查看（字段级权限）+ 详情抽屉
  说明：
    1. 编码由后端生成，只读展示，表单无编码输入框（N-1）；枚举一律数值对接（B-4）；
    2. 列表敏感字段由后端脱敏，明文查看须字段级权限 bas.person.field.plaintext 且后端记录审计；
    3. 导入导出模板编码经后端清单校验（bas.person.import / .export）。
-->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import QlxSearchBar from '@/components/crud/QlxSearchBar.vue'
import type { SearchField } from '@/components/crud/QlxSearchBar.vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import QlxFormDialog from '@/components/crud/QlxFormDialog.vue'
import type { FormField, FormValue } from '@/components/crud/QlxFormDialog.vue'
import MasterImportDialog from '@/components/master/MasterImportDialog.vue'
import { IMPORT_TEMPLATE_CODES, orgMasterApi, personApi, positionApi } from '@/api/basMaster'
import { buildOrgOptions, buildOrgTree } from '@/components/org-admin/org-admin-utils'
import { useCrudTable } from '@/composables/useCrudTable'
import { useMasterTransfer } from '@/composables/useMasterTransfer'
import type {
  BasPositionRow,
  EmployStatus,
  IdType,
  PersonAssignmentRow,
  PersonCertRow,
  PersonDetailRow,
  PersonPlaintextRow,
  PersonRow,
  PersonType,
  RehirePersonPayload,
  ResignPersonPayload,
  SavePersonCertPayload,
  SavePersonPayload,
  UpdatePersonPayload,
} from '@/types/bas-master'
import { ENUM_OPTION_MAPS } from '@/utils/bas-master-mappers'
import {
  employStatusLabel,
  employStatusTone,
  formatDateOnly,
  idTypeLabel,
  personTypeLabel,
  toNumberOptions,
  toStringOptions,
} from '@/utils/bas-master-mappers'
import { formatDateTime } from '@/utils/format'

/** ============ 组织 / 岗位候选（展示名与表单下拉共用） ============ */

const orgOptions = ref<Array<{ label: string; value: string }>>([])
const positionRows = ref<BasPositionRow[]>([])

const orgNameOf = computed(() => {
  const map = new Map<string, string>()
  for (const option of orgOptions.value) {
    map.set(option.value, option.label.replace(/^[　\s]*└\s*/, ''))
  }
  return map
})

const positionOptions = computed(() =>
  positionRows.value.map((row) => ({ label: row.name, value: row.id })),
)

const positionNameOf = computed(() => {
  const map = new Map<string, string>()
  for (const row of positionRows.value) {
    map.set(row.id, row.name)
  }
  return map
})

async function loadCandidates(): Promise<void> {
  try {
    const [orgs, positions] = await Promise.all([
      orgMasterApi.list(),
      positionApi.list({ page: 1, pageSize: 500 }),
    ])
    orgOptions.value = buildOrgOptions(buildOrgTree(orgs))
    positionRows.value = positions.items
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载组织 / 岗位候选失败')
  }
}

onMounted(() => {
  void loadCandidates()
})

/** ============ 列表 ============ */

const searchFields = computed<SearchField[]>(() => [
  {
    prop: 'personType',
    label: '人员类型',
    type: 'select',
    options: toStringOptions(ENUM_OPTION_MAPS.personType),
  },
  {
    prop: 'employStatus',
    label: '在职状态',
    type: 'select',
    options: toStringOptions(ENUM_OPTION_MAPS.employStatus),
  },
  { prop: 'orgId', label: '所属组织', type: 'select', options: orgOptions.value },
  { prop: 'keyword', label: '关键词', type: 'input', placeholder: '姓名 / 编码 / 手机号', width: 220 },
])

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
} = useCrudTable<PersonRow>((params) =>
  personApi.list({
    page: params.page,
    pageSize: params.pageSize,
    personType: typeof params.personType === 'string' && params.personType ? params.personType : undefined,
    employStatus:
      typeof params.employStatus === 'string' && params.employStatus ? params.employStatus : undefined,
    orgId: typeof params.orgId === 'string' && params.orgId ? params.orgId : undefined,
    keyword: typeof params.keyword === 'string' && params.keyword ? params.keyword : undefined,
  }),
)

const columns: Array<CrudColumn<PersonRow>> = [
  { prop: 'code', label: '人员编码', type: 'code', width: 140, fixed: 'left' },
  { prop: 'name', label: '姓名', minWidth: 120 },
  { prop: 'personType', label: '人员类型', width: 110 },
  {
    prop: 'employStatus',
    label: '在职状态',
    type: 'tag',
    width: 100,
    align: 'center',
    tagText: (row) => employStatusLabel(row.employStatus),
    tagTone: (row) => employStatusTone(row.employStatus),
  },
  { prop: 'idNoMasked', label: '证件号码（脱敏）', width: 180 },
  { prop: 'mobileMasked', label: '手机号（脱敏）', width: 150 },
  { prop: 'orgId', label: '所属组织', minWidth: 160 },
  { prop: 'positionId', label: '岗位', width: 140 },
  { prop: 'entryDate', label: '入职日期', width: 120 },
]

/** ============ 新建 / 编辑 ============ */

const formOpen = ref(false)
const submitting = ref(false)
const editing = ref<PersonRow | null>(null)

const createFields = computed<FormField[]>(() => [
  {
    prop: 'name',
    label: '姓名',
    type: 'input',
    required: true,
    maxlength: 32,
    defaultValue: '',
    hint: '编码由系统按编码规则自动生成，无需填写',
  },
  {
    prop: 'personType',
    label: '人员类型',
    type: 'select',
    required: true,
    options: toNumberOptions(ENUM_OPTION_MAPS.personType),
    defaultValue: 1,
  },
  {
    prop: 'idType',
    label: '证件类型',
    type: 'select',
    required: true,
    options: toNumberOptions(ENUM_OPTION_MAPS.idType),
    defaultValue: 1,
  },
  {
    prop: 'idNo',
    label: '证件号码',
    type: 'input',
    required: true,
    maxlength: 18,
    pattern: /^[0-9A-Za-z]{6,18}$/,
    patternMessage: '证件号码为 6~18 位数字或字母',
    hint: '明文提交，后端加密落库',
  },
  {
    prop: 'mobile',
    label: '手机号',
    type: 'input',
    required: true,
    maxlength: 11,
    pattern: /^1\d{10}$/,
    patternMessage: '请输入 11 位手机号',
  },
  { prop: 'bankCardNo', label: '银行卡号', type: 'input', maxlength: 32 },
  { prop: 'emergencyContact', label: '紧急联系人', type: 'input', maxlength: 32 },
  {
    prop: 'emergencyPhone',
    label: '紧急联系电话',
    type: 'input',
    maxlength: 20,
    pattern: /^[0-9+\-\s]{6,20}$/,
    patternMessage: '联系电话格式不正确',
  },
  {
    prop: 'orgId',
    label: '所属组织',
    type: 'select',
    requiredWhen: (values) => Number(values.personType ?? 1) === 1,
    options: orgOptions.value,
    hint: '员工必须指定所属组织与岗位',
  },
  {
    prop: 'positionId',
    label: '岗位',
    type: 'select',
    requiredWhen: (values) => Number(values.personType ?? 1) === 1,
    options: positionOptions.value,
    hint: '员工必须指定岗位',
  },
  {
    prop: 'employStatus',
    label: '在职状态',
    type: 'select',
    required: true,
    options: toNumberOptions(ENUM_OPTION_MAPS.employStatus),
    defaultValue: 1,
  },
  { prop: 'entryDate', label: '入职日期', type: 'date' },
])

const editFields = computed<FormField[]>(() => [
  {
    prop: 'name',
    label: '姓名',
    type: 'input',
    required: true,
    maxlength: 32,
    defaultValue: editing.value?.name ?? '',
    hint: editing.value ? `人员编码 ${editing.value.code} 由系统生成，不可修改` : '',
  },
  {
    prop: 'personType',
    label: '人员类型',
    type: 'select',
    required: true,
    options: toNumberOptions(ENUM_OPTION_MAPS.personType),
    defaultValue: editing.value?.personType ?? 1,
  },
  {
    prop: 'emergencyContact',
    label: '紧急联系人',
    type: 'input',
    maxlength: 32,
    defaultValue: editing.value?.emergencyContact ?? '',
  },
  {
    prop: 'emergencyPhone',
    label: '紧急联系电话',
    type: 'input',
    maxlength: 20,
    pattern: /^[0-9+\-\s]{6,20}$/,
    patternMessage: '联系电话格式不正确',
    defaultValue: editing.value?.emergencyPhone ?? '',
  },
  {
    prop: 'orgId',
    label: '所属组织',
    type: 'select',
    requiredWhen: (values) => Number(values.personType ?? 1) === 1,
    options: orgOptions.value,
    defaultValue: editing.value?.orgId ?? '',
    hint: '员工必须指定所属组织与岗位',
  },
  {
    prop: 'positionId',
    label: '岗位',
    type: 'select',
    requiredWhen: (values) => Number(values.personType ?? 1) === 1,
    options: positionOptions.value,
    defaultValue: editing.value?.positionId ?? '',
    hint: '员工必须指定岗位',
  },
  { prop: 'effectiveDate', label: '生效日期', type: 'date' },
  {
    prop: 'changeReason',
    label: '变更原因',
    type: 'textarea',
    full: true,
    maxlength: 200,
    defaultValue: '',
  },
])

function openCreate(): void {
  editing.value = null
  formOpen.value = true
}

function openEdit(row: PersonRow): void {
  editing.value = row
  formOpen.value = true
}

async function onSubmit(values: Record<string, FormValue>): Promise<void> {
  submitting.value = true
  try {
    if (editing.value) {
      const payload: UpdatePersonPayload = {
        name: String(values.name ?? '').trim(),
        personType: Number(values.personType ?? 1) as PersonType,
        emergencyContact: String(values.emergencyContact ?? '').trim() || null,
        emergencyPhone: String(values.emergencyPhone ?? '').trim() || null,
        orgId: (values.orgId ? String(values.orgId) : null) || null,
        positionId: (values.positionId ? String(values.positionId) : null) || null,
        effectiveDate: values.effectiveDate ? String(values.effectiveDate) : null,
        changeReason: String(values.changeReason ?? '').trim() || null,
      }
      await personApi.update(editing.value.id, payload)
      ElMessage.success('人员信息已保存')
    } else {
      const payload: SavePersonPayload = {
        name: String(values.name ?? '').trim(),
        personType: Number(values.personType ?? 1) as PersonType,
        idType: Number(values.idType ?? 1) as IdType,
        idNo: String(values.idNo ?? '').trim(),
        mobile: String(values.mobile ?? '').trim(),
        emergencyContact: String(values.emergencyContact ?? '').trim() || null,
        emergencyPhone: String(values.emergencyPhone ?? '').trim() || null,
        orgId: (values.orgId ? String(values.orgId) : null) || null,
        positionId: (values.positionId ? String(values.positionId) : null) || null,
        employStatus: Number(values.employStatus ?? 1) as EmployStatus,
        entryDate: values.entryDate ? String(values.entryDate) : null,
        bankCardNo: String(values.bankCardNo ?? '').trim() || null,
      }
      await personApi.create(payload)
      ElMessage.success('人员已创建')
    }
    formOpen.value = false
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存人员失败')
  } finally {
    submitting.value = false
  }
}

/** ============ 离职 / 再入职 ============ */

const leaveOpen = ref(false)
const rehireOpen = ref(false)
const leaveSubmitting = ref(false)
const rehireSubmitting = ref(false)
const actionTarget = ref<PersonRow | null>(null)

const leaveFields = computed<FormField[]>(() => [
  {
    prop: 'leaveDate',
    label: '离职日期',
    type: 'date',
    hint: '留空按当天办理',
    defaultValue: '',
  },
  { prop: 'reason', label: '离职原因', type: 'textarea', full: true, maxlength: 200, defaultValue: '' },
])

const rehireFields = computed<FormField[]>(() => [
  { prop: 'entryDate', label: '再入职日期', type: 'date', hint: '留空按当天办理', defaultValue: '' },
  { prop: 'orgId', label: '所属组织', type: 'select', options: orgOptions.value },
  { prop: 'positionId', label: '岗位', type: 'select', options: positionOptions.value },
  {
    prop: 'employStatus',
    label: '在职状态',
    type: 'select',
    options: toNumberOptions(ENUM_OPTION_MAPS.employStatus),
    defaultValue: 1,
  },
])

function openLeave(row: PersonRow): void {
  actionTarget.value = row
  leaveOpen.value = true
}

function openRehire(row: PersonRow): void {
  actionTarget.value = row
  rehireOpen.value = true
}

async function onLeaveSubmit(values: Record<string, FormValue>): Promise<void> {
  const target = actionTarget.value
  if (!target) {
    return
  }
  leaveSubmitting.value = true
  try {
    const payload: ResignPersonPayload = {
      leaveDate: values.leaveDate ? String(values.leaveDate) : null,
      reason: String(values.reason ?? '').trim() || null,
    }
    await personApi.leave(target.id, payload)
    leaveOpen.value = false
    await refresh()
    ElMessage.success('离职已办理')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '办理离职失败')
  } finally {
    leaveSubmitting.value = false
  }
}

async function onRehireSubmit(values: Record<string, FormValue>): Promise<void> {
  const target = actionTarget.value
  if (!target) {
    return
  }
  rehireSubmitting.value = true
  try {
    const payload: RehirePersonPayload = {
      entryDate: values.entryDate ? String(values.entryDate) : null,
      orgId: (values.orgId ? String(values.orgId) : null) || null,
      positionId: (values.positionId ? String(values.positionId) : null) || null,
      employStatus: values.employStatus
        ? (Number(values.employStatus) as EmployStatus)
        : null,
    }
    await personApi.rehire(target.id, payload)
    rehireOpen.value = false
    await refresh()
    ElMessage.success('再入职已办理')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '办理再入职失败')
  } finally {
    rehireSubmitting.value = false
  }
}

/** ============ 明文查看（字段级权限 + 审计） ============ */

const plaintextOpen = ref(false)
const plaintextLoading = ref(false)
const plaintext = ref<PersonPlaintextRow | null>(null)

async function openPlaintext(row: PersonRow): Promise<void> {
  plaintextOpen.value = true
  plaintext.value = null
  plaintextLoading.value = true
  try {
    plaintext.value = await personApi.plaintext(row.id)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '查看明文失败（可能缺少字段级权限）')
  } finally {
    plaintextLoading.value = false
  }
}

/** ============ 详情抽屉（任职历史 / 证件档案） ============ */

const detailOpen = ref(false)
const detailLoading = ref(false)
const detail = ref<PersonDetailRow | null>(null)
const detailTab = ref<'assignments' | 'certs'>('assignments')

async function openDetail(row: PersonRow): Promise<void> {
  detailOpen.value = true
  detailTab.value = 'assignments'
  detailLoading.value = true
  try {
    detail.value = await personApi.get(row.id)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载人员详情失败')
  } finally {
    detailLoading.value = false
  }
}

async function refreshDetail(): Promise<void> {
  if (!detail.value) {
    return
  }
  detail.value = await personApi.get(detail.value.master.id)
}

const assignmentColumns: Array<CrudColumn<PersonAssignmentRow>> = [
  { prop: 'orgId', label: '所属组织', minWidth: 160 },
  { prop: 'positionId', label: '岗位', minWidth: 140 },
  { prop: 'startDate', label: '开始日期', width: 120 },
  { prop: 'endDate', label: '结束日期', width: 120 },
  { prop: 'changeReason', label: '变更原因', minWidth: 180 },
  { prop: 'isCurrent', label: '当前任职', type: 'check', width: 100, align: 'center' },
]

const certColumns: Array<CrudColumn<PersonCertRow>> = [
  { prop: 'certType', label: '证件类型', width: 140 },
  { prop: 'certNo', label: '证件编号', minWidth: 180 },
  { prop: 'issuedOn', label: '签发日期', width: 120 },
  { prop: 'expiresOn', label: '有效期至', width: 120 },
  { prop: 'remark', label: '备注', minWidth: 160 },
]

const certOpen = ref(false)
const certSubmitting = ref(false)

const certFields: FormField[] = [
  { prop: 'certType', label: '证件类型', type: 'input', required: true, maxlength: 32 },
  { prop: 'certNo', label: '证件编号', type: 'input', maxlength: 64 },
  { prop: 'issuedOn', label: '签发日期', type: 'date' },
  { prop: 'expiresOn', label: '有效期至', type: 'date' },
  { prop: 'remark', label: '备注', type: 'textarea', full: true, maxlength: 200 },
]

async function onCertSubmit(values: Record<string, FormValue>): Promise<void> {
  const master = detail.value?.master
  if (!master) {
    return
  }
  certSubmitting.value = true
  try {
    const payload: SavePersonCertPayload = {
      certType: String(values.certType ?? '').trim(),
      certNo: String(values.certNo ?? '').trim() || null,
      issuedOn: values.issuedOn ? String(values.issuedOn) : null,
      expiresOn: values.expiresOn ? String(values.expiresOn) : null,
      remark: String(values.remark ?? '').trim() || null,
    }
    await personApi.saveCert(master.id, payload)
    certOpen.value = false
    await refreshDetail()
    ElMessage.success('证件档案已登记')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '登记证件档案失败')
  } finally {
    certSubmitting.value = false
  }
}

const detailItems = computed(() => {
  const master = detail.value?.master
  if (!master) {
    return []
  }
  return [
    { label: '人员编码', value: master.code },
    { label: '姓名', value: master.name },
    { label: '人员类型', value: personTypeLabel(master.personType) },
    { label: '证件类型', value: idTypeLabel(master.idType) },
    { label: '证件号码（脱敏）', value: master.idNoMasked ?? '—' },
    { label: '手机号（脱敏）', value: master.mobileMasked ?? '—' },
    { label: '所属组织', value: master.orgId ? (orgNameOf.value.get(master.orgId) ?? master.orgId) : '—' },
    {
      label: '岗位',
      value: master.positionId
        ? (positionNameOf.value.get(master.positionId) ?? master.positionId)
        : '—',
    },
    { label: '在职状态', value: employStatusLabel(master.employStatus) },
    { label: '入职日期', value: formatDateOnly(master.entryDate) },
    { label: '离职日期', value: formatDateOnly(master.leaveDate) },
    { label: '紧急联系人', value: master.emergencyContact ?? '—' },
    { label: '紧急联系电话', value: master.emergencyPhone ?? '—' },
    { label: '更新时间', value: formatDateTime(master.updatedAt) },
  ]
})

/** ============ 导入导出 ============ */

const {
  importOpen: importDialogOpen,
  importing: importSubmitting,
  exporting,
  downloading,
  importResult,
  templateHint,
  openImport,
  downloadTemplate,
  exportFile,
  submitImportFile,
} = useMasterTransfer({
  templateCode: IMPORT_TEMPLATE_CODES.person,
  exportParams: () => ({
    personType: query.value.personType || undefined,
    employStatus: query.value.employStatus || undefined,
    keyword: query.value.keyword || undefined,
  }),
  onImported: refresh,
})
</script>

<template>
  <QlxPagePanel title="人员管理" subtitle="人员档案 · 任职记录 · 证件档案 · 敏感字段字段级授权（规范 5.2.3）">
    <template #toolbar>
      <el-button v-permission="'bas.person.action'" type="primary" @click="openCreate">
        <AppIcon name="Plus" :size="14" />
        新增人员
      </el-button>
      <el-button :loading="downloading" @click="downloadTemplate()">
        <AppIcon name="Document" :size="14" />
        下载模板
      </el-button>
      <el-button v-permission="'bas.person.action'" @click="openImport()">
        <AppIcon name="UploadFilled" :size="14" />
        导入
      </el-button>
      <el-button :loading="exporting" @click="exportFile()">
        <AppIcon name="Files" :size="14" />
        导出
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
      :actions-width="300"
      empty-text="暂无人员"
      @page-change="changePage"
    >
      <template #cell-personType="{ row }">{{ personTypeLabel(row.personType) }}</template>
      <template #cell-orgId="{ row }">
        {{ row.orgId ? (orgNameOf.get(row.orgId) ?? row.orgId) : '—' }}
      </template>
      <template #cell-positionId="{ row }">
        {{ row.positionId ? (positionNameOf.get(row.positionId) ?? row.positionId) : '—' }}
      </template>
      <template #cell-entryDate="{ row }">{{ formatDateOnly(row.entryDate) }}</template>
      <template #actions="{ row }">
        <button
          v-permission="'bas.person.menu'"
          type="button"
          class="cell-link"
          @click="openDetail(row)"
        >
          详情
        </button>
        <el-divider direction="vertical" />
        <button
          v-permission="'bas.person.action'"
          type="button"
          class="cell-link"
          @click="openEdit(row)"
        >
          编辑
        </button>
        <el-divider direction="vertical" />
        <button
          v-permission="'bas.person.field.plaintext'"
          type="button"
          class="cell-link"
          @click="openPlaintext(row)"
        >
          查看明文
        </button>
        <el-divider direction="vertical" />
        <button
          v-if="row.employStatus !== 4"
          v-permission="'bas.person.action'"
          type="button"
          class="cell-link"
          @click="openLeave(row)"
        >
          离职
        </button>
        <button
          v-else
          v-permission="'bas.person.action'"
          type="button"
          class="cell-link"
          @click="openRehire(row)"
        >
          再入职
        </button>
      </template>
    </QlxDataTable>

    <QlxFormDialog
      v-model="formOpen"
      :title="editing ? `编辑人员 · ${editing.name}` : '新增人员'"
      :fields="editing ? editFields : createFields"
      :submitting="submitting"
      submit-text="保存人员"
      :width="720"
      class="master-form-dialog"
      @submit="onSubmit"
    />

    <QlxFormDialog
      v-model="leaveOpen"
      :title="actionTarget ? `办理离职 · ${actionTarget.name}` : '办理离职'"
      :fields="leaveFields"
      :submitting="leaveSubmitting"
      submit-text="确认离职"
      :width="520"
      class="master-form-dialog"
      @submit="onLeaveSubmit"
    />

    <QlxFormDialog
      v-model="rehireOpen"
      :title="actionTarget ? `再入职 · ${actionTarget.name}` : '再入职'"
      :fields="rehireFields"
      :submitting="rehireSubmitting"
      submit-text="确认再入职"
      :width="600"
      class="master-form-dialog"
      @submit="onRehireSubmit"
    />

    <!-- 敏感字段明文 -->
    <el-dialog v-model="plaintextOpen" title="敏感字段明文" width="520" append-to-body>
      <div v-loading="plaintextLoading">
        <p class="person-plaintext__notice">
          本次查看将记录审计日志（操作类型 VIEW_PLAINTEXT），请勿截图外传。
        </p>
        <el-descriptions v-if="plaintext" :column="1" border>
          <el-descriptions-item label="证件号码">{{ plaintext.idNo || '—' }}</el-descriptions-item>
          <el-descriptions-item label="手机号">{{ plaintext.mobile || '—' }}</el-descriptions-item>
          <el-descriptions-item label="银行卡号">{{ plaintext.bankCardNo || '—' }}</el-descriptions-item>
        </el-descriptions>
        <el-empty v-else description="暂无明文数据" :image-size="64" />
      </div>
    </el-dialog>

    <!-- 详情抽屉 -->
    <el-drawer v-model="detailOpen" title="人员详情" size="820px" append-to-body>
      <div v-loading="detailLoading" class="person-detail">
        <template v-if="detail">
          <el-descriptions :column="2" border>
            <el-descriptions-item v-for="item in detailItems" :key="item.label" :label="item.label">
              {{ item.value }}
            </el-descriptions-item>
          </el-descriptions>

          <el-tabs v-model="detailTab" class="person-detail__tabs">
            <el-tab-pane label="任职历史" name="assignments">
              <QlxDataTable
                :items="detail.assignments"
                :columns="assignmentColumns"
                :total="detail.assignments.length"
                :page-size="10"
                empty-text="暂无任职记录"
              >
                <template #cell-orgId="{ row }">
                  {{ row.orgId ? (orgNameOf.get(row.orgId) ?? row.orgId) : '—' }}
                </template>
                <template #cell-positionId="{ row }">
                  {{ row.positionId ? (positionNameOf.get(row.positionId) ?? row.positionId) : '—' }}
                </template>
                <template #cell-startDate="{ row }">{{ formatDateOnly(row.startDate) }}</template>
                <template #cell-endDate="{ row }">{{ formatDateOnly(row.endDate) }}</template>
              </QlxDataTable>
            </el-tab-pane>

            <el-tab-pane label="证件档案" name="certs">
              <div class="person-detail__toolbar">
                <el-button
                  v-permission="'bas.person.action'"
                  size="small"
                  @click="certOpen = true"
                >
                  登记证件
                </el-button>
              </div>
              <QlxDataTable
                :items="detail.certs"
                :columns="certColumns"
                :total="detail.certs.length"
                :page-size="10"
                empty-text="暂无证件档案"
              >
                <template #cell-issuedOn="{ row }">{{ formatDateOnly(row.issuedOn) }}</template>
                <template #cell-expiresOn="{ row }">{{ formatDateOnly(row.expiresOn) }}</template>
              </QlxDataTable>
            </el-tab-pane>
          </el-tabs>
        </template>
      </div>
    </el-drawer>

    <QlxFormDialog
      v-model="certOpen"
      title="登记证件档案"
      :fields="certFields"
      :submitting="certSubmitting"
      submit-text="保存"
      :width="600"
      class="master-form-dialog"
      @submit="onCertSubmit"
    />

    <MasterImportDialog
      v-model="importDialogOpen"
      title="导入人员"
      :hint="templateHint"
      :result="importResult"
      :submitting="importSubmitting"
      :downloading="downloading"
      @submit-file="submitImportFile"
      @download-template="downloadTemplate"
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

.person-plaintext__notice {
  margin-bottom: var(--qlx-space-3);
  padding: var(--qlx-space-2) var(--qlx-space-3);
  border-left: 3px solid var(--qlx-danger);
  background: var(--qlx-canvas);
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

.person-detail__tabs {
  margin-top: var(--qlx-space-4);
}

.person-detail__toolbar {
  margin-bottom: var(--qlx-space-3);
}
</style>

<style>
@media (max-width: 767px) {
  .master-form-dialog.el-dialog {
    width: min(720px, 92vw);
  }
}
</style>
