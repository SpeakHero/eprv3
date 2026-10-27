<!--
  所属公司：四川淇利馨集团有限公司
  描述：供应商主数据——分页列表 + 新建 / 编辑 + 评级变更 + 准入申请 + 详情抽屉（资质 / 银行 / 开票 / 评级历史）
  说明：
    1. 编码由后端生成，只读展示，表单无编码输入框（N-1）；枚举一律数值对接（B-4）；
    2. 供应商无启停端点（后端契约），列表"状态"为合作状态，准入 / 评级经审批申请挂起；
    3. 导入导出模板编码经后端清单校验（bas.supplier.import / .export）。
-->
<script setup lang="ts">
import { computed, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import QlxSearchBar from '@/components/crud/QlxSearchBar.vue'
import type { SearchField } from '@/components/crud/QlxSearchBar.vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import QlxFormDialog from '@/components/crud/QlxFormDialog.vue'
import type { FormField, FormValue } from '@/components/crud/QlxFormDialog.vue'
import MasterImportDialog from '@/components/master/MasterImportDialog.vue'
import { IMPORT_TEMPLATE_CODES, supplierApi } from '@/api/basMaster'
import { useCrudTable } from '@/composables/useCrudTable'
import { useMasterTransfer } from '@/composables/useMasterTransfer'
import type {
  PartnerType,
  SaveSupplierBankPayload,
  SaveSupplierCertPayload,
  SaveSupplierInvoicePayload,
  SaveSupplierPayload,
  SupplierBankRow,
  SupplierCertRow,
  SupplierDetailRow,
  SupplierGrade,
  SupplierInvoiceRow,
  SupplierRow,
} from '@/types/bas-master'
import { ENUM_OPTION_MAPS } from '@/utils/bas-master-mappers'
import {
  approvalRequestStatusLabel,
  certStatusLabel,
  certStatusTone,
  formatDateOnly,
  invoiceTypeLabel,
  partnerStatusLabel,
  partnerStatusTone,
  partnerTypeLabel,
  supplierGradeLabel,
  supplierGradeTone,
  toNumberOptions,
  toStringOptions,
} from '@/utils/bas-master-mappers'
import { formatDateTime } from '@/utils/format'

const searchFields: SearchField[] = [
  {
    prop: 'grade',
    label: '评级',
    type: 'select',
    options: toStringOptions(ENUM_OPTION_MAPS.supplierGrade),
  },
  {
    prop: 'status',
    label: '合作状态',
    type: 'select',
    options: toStringOptions(ENUM_OPTION_MAPS.partnerStatus),
  },
  { prop: 'keyword', label: '关键词', type: 'input', placeholder: '名称 / 编码 / 信用代码', width: 240 },
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
} = useCrudTable<SupplierRow>((params) =>
  supplierApi.list({
    page: params.page,
    pageSize: params.pageSize,
    grade: typeof params.grade === 'string' && params.grade ? params.grade : undefined,
    status: typeof params.status === 'string' && params.status ? params.status : undefined,
    keyword: typeof params.keyword === 'string' && params.keyword ? params.keyword : undefined,
  }),
)

const columns: Array<CrudColumn<SupplierRow>> = [
  { prop: 'code', label: '供应商编码', type: 'code', width: 150, fixed: 'left' },
  { prop: 'name', label: '供应商名称', minWidth: 200 },
  { prop: 'uscc', label: '统一社会信用代码', type: 'code', width: 190 },
  { prop: 'supplierType', label: '供应商类型', width: 120 },
  {
    prop: 'grade',
    label: '评级',
    type: 'tag',
    width: 90,
    align: 'center',
    tagText: (row) => supplierGradeLabel(row.grade),
    tagTone: (row) => supplierGradeTone(row.grade),
  },
  {
    prop: 'status',
    label: '合作状态',
    type: 'tag',
    width: 100,
    align: 'center',
    tagText: (row) => partnerStatusLabel(row.status),
    tagTone: (row) => partnerStatusTone(row.status),
  },
  { prop: 'contact', label: '联系人', width: 110 },
  { prop: 'phone', label: '联系电话', width: 130 },
  { prop: 'updatedAt', label: '更新时间', type: 'time', width: 160 },
]

/** ============ 新建 / 编辑（编码只读，N-1） ============ */

const formOpen = ref(false)
const submitting = ref(false)
const editing = ref<SupplierRow | null>(null)

const formFields = computed<FormField[]>(() => [
  {
    prop: 'name',
    label: '供应商名称',
    type: 'input',
    required: true,
    maxlength: 80,
    full: true,
    defaultValue: editing.value?.name ?? '',
    hint: editing.value
      ? `编码 ${editing.value.code} 由系统生成，不可修改`
      : '编码由系统按编码规则自动生成，无需填写',
  },
  {
    prop: 'uscc',
    label: '信用代码',
    type: 'input',
    required: true,
    maxlength: 18,
    defaultValue: editing.value?.uscc ?? '',
    pattern: /^[0-9A-Za-z]{18}$/,
    patternMessage: '统一社会信用代码为 18 位数字或大写字母',
  },
  {
    prop: 'supplierType',
    label: '供应商类型',
    type: 'select',
    required: true,
    options: toNumberOptions(ENUM_OPTION_MAPS.partnerType).map((option) => ({
      label: option.label,
      value: option.value,
    })),
    defaultValue: editing.value?.supplierType ?? 1,
  },
  {
    prop: 'contact',
    label: '联系人',
    type: 'input',
    maxlength: 32,
    defaultValue: editing.value?.contact ?? '',
  },
  {
    prop: 'phone',
    label: '联系电话',
    type: 'input',
    maxlength: 20,
    defaultValue: editing.value?.phone ?? '',
    pattern: /^[0-9+\-\s]{6,20}$/,
    patternMessage: '联系电话格式不正确',
  },
])

function openCreate(): void {
  editing.value = null
  formOpen.value = true
}

function openEdit(row: SupplierRow): void {
  editing.value = row
  formOpen.value = true
}

async function onSubmit(values: Record<string, FormValue>): Promise<void> {
  submitting.value = true
  try {
    const payload: SaveSupplierPayload = {
      name: String(values.name ?? '').trim(),
      uscc: String(values.uscc ?? '').trim().toUpperCase(),
      supplierType: Number(values.supplierType ?? 1) as PartnerType,
      contact: String(values.contact ?? '').trim() || null,
      phone: String(values.phone ?? '').trim() || null,
    }
    if (editing.value) {
      await supplierApi.update(editing.value.id, payload)
      ElMessage.success('供应商信息已保存')
    } else {
      await supplierApi.create(payload)
      ElMessage.success('供应商已创建')
    }
    formOpen.value = false
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存供应商失败')
  } finally {
    submitting.value = false
  }
}

/** ============ 评级变更 / 准入申请 ============ */

const gradeOpen = ref(false)
const gradeSubmitting = ref(false)
const gradeTarget = ref<SupplierRow | null>(null)

const gradeFields = computed<FormField[]>(() => [
  {
    prop: 'grade',
    label: '新评级',
    type: 'select',
    required: true,
    options: toNumberOptions(ENUM_OPTION_MAPS.supplierGrade),
    defaultValue: gradeTarget.value?.grade ?? 2,
  },
  {
    prop: 'reason',
    label: '变更原因',
    type: 'textarea',
    full: true,
    maxlength: 200,
    defaultValue: '',
  },
])

function openGrade(row: SupplierRow): void {
  gradeTarget.value = row
  gradeOpen.value = true
}

async function onGradeSubmit(values: Record<string, FormValue>): Promise<void> {
  const target = gradeTarget.value
  if (!target) {
    return
  }
  gradeSubmitting.value = true
  try {
    await supplierApi.changeGrade(target.id, {
      grade: Number(values.grade ?? target.grade) as SupplierGrade,
      reason: String(values.reason ?? '').trim() || null,
    })
    gradeOpen.value = false
    await refresh()
    ElMessage.success('供应商评级已变更')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '变更评级失败')
  } finally {
    gradeSubmitting.value = false
  }
}

async function submitAdmission(row: SupplierRow): Promise<void> {
  try {
    const result = await ElMessageBox.prompt(
      `为「${row.name}」提交准入审批，审批通过后合作状态转为合作中。请填写准入原因：`,
      '提交准入申请',
      {
        confirmButtonText: '提交申请',
        cancelButtonText: '取消',
        inputPlaceholder: '如：年度供应商评审通过',
        inputValidator: (value) => (value?.trim() ? true : '请填写准入原因'),
      },
    )
    const request = await supplierApi.submitAdmission(row.id, String(result.value ?? '').trim())
    ElMessage.success(`准入申请已提交：${approvalRequestStatusLabel(request.status)}`)
    await refresh()
  } catch (error) {
    if (error instanceof Error) {
      ElMessage.error(error.message)
    }
  }
}

/** ============ 详情抽屉（主档 + 子表扁平集合） ============ */

const detailOpen = ref(false)
const detailLoading = ref(false)
const detail = ref<SupplierDetailRow | null>(null)
const detailTab = ref<'certs' | 'banks' | 'invoices' | 'gradeLogs'>('certs')

async function openDetail(row: SupplierRow): Promise<void> {
  detailOpen.value = true
  detailTab.value = 'certs'
  detailLoading.value = true
  try {
    detail.value = await supplierApi.get(row.id)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载供应商详情失败')
  } finally {
    detailLoading.value = false
  }
}

async function refreshDetail(): Promise<void> {
  if (!detail.value) {
    return
  }
  detail.value = await supplierApi.get(detail.value.master.id)
}

const certColumns: Array<CrudColumn<SupplierCertRow>> = [
  { prop: 'certType', label: '证照类型', width: 130 },
  { prop: 'certNo', label: '证照编号', minWidth: 160 },
  { prop: 'issueOrg', label: '发证机关', minWidth: 140 },
  { prop: 'effectDate', label: '生效日期', width: 120 },
  { prop: 'expiryDate', label: '失效日期', width: 120 },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 90,
    align: 'center',
    tagText: (row) => certStatusLabel(row.status),
    tagTone: (row) => certStatusTone(row.status),
  },
]

const bankColumns: Array<CrudColumn<SupplierBankRow>> = [
  { prop: 'bankName', label: '开户银行', minWidth: 160 },
  { prop: 'accountName', label: '账户名称', minWidth: 160 },
  { prop: 'accountNo', label: '银行账号', type: 'code', minWidth: 190 },
  {
    prop: 'isDefault',
    label: '默认账户',
    type: 'check',
    width: 100,
    align: 'center',
  },
]

const invoiceColumns: Array<CrudColumn<SupplierInvoiceRow>> = [
  { prop: 'title', label: '开票抬头', minWidth: 200 },
  { prop: 'taxNo', label: '税号', type: 'code', minWidth: 180 },
  { prop: 'invoiceType', label: '开票类型', width: 160 },
  { prop: 'bankInfo', label: '开户行及账号', minWidth: 200 },
  { prop: 'addrPhone', label: '地址电话', minWidth: 180 },
]

/** ============ 子表登记弹窗 ============ */

const certOpen = ref(false)
const bankOpen = ref(false)
const invoiceOpen = ref(false)
const certSubmitting = ref(false)
const bankSubmitting = ref(false)
const invoiceSubmitting = ref(false)

const certFields: FormField[] = [
  { prop: 'certType', label: '证照类型', type: 'input', required: true, maxlength: 32 },
  { prop: 'certNo', label: '证照编号', type: 'input', required: true, maxlength: 64 },
  { prop: 'issueOrg', label: '发证机关', type: 'input', maxlength: 64 },
  { prop: 'effectDate', label: '生效日期', type: 'date' },
  { prop: 'expiryDate', label: '失效日期', type: 'date' },
]

const bankFields: FormField[] = [
  { prop: 'bankName', label: '开户银行', type: 'input', required: true, maxlength: 64 },
  { prop: 'accountName', label: '账户名称', type: 'input', required: true, maxlength: 64 },
  { prop: 'accountNo', label: '银行账号', type: 'input', required: true, maxlength: 32, full: true },
  { prop: 'isDefault', label: '默认账户', type: 'switch' },
]

const invoiceFields: FormField[] = [
  { prop: 'title', label: '开票抬头', type: 'input', required: true, maxlength: 80, full: true },
  { prop: 'taxNo', label: '税号', type: 'input', maxlength: 20 },
  {
    prop: 'invoiceType',
    label: '开票类型',
    type: 'select',
    required: true,
    options: toNumberOptions(ENUM_OPTION_MAPS.invoiceType),
    defaultValue: 1,
  },
  { prop: 'bankInfo', label: '开户行及账号', type: 'input', maxlength: 80, full: true },
  { prop: 'addrPhone', label: '地址电话', type: 'input', maxlength: 80, full: true },
]

async function onCertSubmit(values: Record<string, FormValue>): Promise<void> {
  const master = detail.value?.master
  if (!master) {
    return
  }
  certSubmitting.value = true
  try {
    const payload: SaveSupplierCertPayload = {
      certType: String(values.certType ?? '').trim(),
      certNo: String(values.certNo ?? '').trim(),
      issueOrg: String(values.issueOrg ?? '').trim() || null,
      effectDate: values.effectDate ? String(values.effectDate) : null,
      expiryDate: values.expiryDate ? String(values.expiryDate) : null,
      fileId: null,
    }
    await supplierApi.saveCert(master.id, payload)
    certOpen.value = false
    await refreshDetail()
    ElMessage.success('资质证照已登记')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '登记资质证照失败')
  } finally {
    certSubmitting.value = false
  }
}

async function onBankSubmit(values: Record<string, FormValue>): Promise<void> {
  const master = detail.value?.master
  if (!master) {
    return
  }
  bankSubmitting.value = true
  try {
    const payload: SaveSupplierBankPayload = {
      bankName: String(values.bankName ?? '').trim(),
      accountName: String(values.accountName ?? '').trim(),
      accountNo: String(values.accountNo ?? '').trim(),
      isDefault: Boolean(values.isDefault),
    }
    await supplierApi.saveBank(master.id, payload)
    bankOpen.value = false
    await refreshDetail()
    ElMessage.success('银行账户已登记')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '登记银行账户失败')
  } finally {
    bankSubmitting.value = false
  }
}

async function onInvoiceSubmit(values: Record<string, FormValue>): Promise<void> {
  const master = detail.value?.master
  if (!master) {
    return
  }
  invoiceSubmitting.value = true
  try {
    const payload: SaveSupplierInvoicePayload = {
      title: String(values.title ?? '').trim(),
      taxNo: String(values.taxNo ?? '').trim() || null,
      bankInfo: String(values.bankInfo ?? '').trim() || null,
      addrPhone: String(values.addrPhone ?? '').trim() || null,
      invoiceType: Number(values.invoiceType ?? 1) as 1 | 2 | 3,
    }
    await supplierApi.saveInvoice(master.id, payload)
    invoiceOpen.value = false
    await refreshDetail()
    ElMessage.success('开票信息已登记')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '登记开票信息失败')
  } finally {
    invoiceSubmitting.value = false
  }
}

const detailItems = computed(() => {
  const master = detail.value?.master
  if (!master) {
    return []
  }
  return [
    { label: '供应商编码', value: master.code },
    { label: '供应商名称', value: master.name },
    { label: '统一社会信用代码', value: master.uscc },
    { label: '供应商类型', value: partnerTypeLabel(master.supplierType) },
    { label: '评级', value: supplierGradeLabel(master.grade) },
    { label: '合作状态', value: partnerStatusLabel(master.status) },
    { label: '联系人', value: master.contact ?? '—' },
    { label: '联系电话', value: master.phone ?? '—' },
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
  templateCode: IMPORT_TEMPLATE_CODES.supplier,
  exportParams: () => ({
    status: query.value.status || undefined,
    keyword: query.value.keyword || undefined,
  }),
  onImported: refresh,
})
</script>

<template>
  <QlxPagePanel title="供应商管理" subtitle="准入 · 评级 · 资质证照 · 银行与开票信息（规范 5.2.2）">
    <template #toolbar>
      <el-button v-permission="'bas.supplier.action'" type="primary" @click="openCreate">
        <AppIcon name="Plus" :size="14" />
        新增供应商
      </el-button>
      <el-button :loading="downloading" @click="downloadTemplate()">
        <AppIcon name="Document" :size="14" />
        下载模板
      </el-button>
      <el-button @click="openImport()">
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
      :actions-width="220"
      empty-text="暂无供应商"
      @page-change="changePage"
    >
      <template #cell-supplierType="{ row }">{{ partnerTypeLabel(row.supplierType) }}</template>
      <template #actions="{ row }">
        <button
          v-permission="'bas.supplier.menu'"
          type="button"
          class="cell-link"
          @click="openDetail(row)"
        >
          详情
        </button>
        <el-divider direction="vertical" />
        <button
          v-permission="'bas.supplier.action'"
          type="button"
          class="cell-link"
          @click="openEdit(row)"
        >
          编辑
        </button>
        <el-divider direction="vertical" />
        <button
          v-permission="'bas.supplier.action'"
          type="button"
          class="cell-link"
          @click="openGrade(row)"
        >
          评级
        </button>
        <el-divider direction="vertical" />
        <button
          v-permission="'bas.supplier.action'"
          type="button"
          class="cell-link"
          @click="submitAdmission(row)"
        >
          准入
        </button>
      </template>
    </QlxDataTable>

    <QlxFormDialog
      v-model="formOpen"
      :title="editing ? `编辑供应商 · ${editing.name}` : '新增供应商'"
      :fields="formFields"
      :submitting="submitting"
      submit-text="保存供应商"
      :width="600"
      class="master-form-dialog"
      @submit="onSubmit"
    />

    <QlxFormDialog
      v-model="gradeOpen"
      :title="gradeTarget ? `供应商评级 · ${gradeTarget.name}` : '供应商评级'"
      :fields="gradeFields"
      :submitting="gradeSubmitting"
      submit-text="确认变更"
      :width="520"
      class="master-form-dialog"
      @submit="onGradeSubmit"
    />

    <!-- 详情抽屉 -->
    <el-drawer v-model="detailOpen" title="供应商详情" size="760px" append-to-body>
      <div v-loading="detailLoading" class="supplier-detail">
        <template v-if="detail">
          <el-descriptions :column="2" border>
            <el-descriptions-item
              v-for="item in detailItems"
              :key="item.label"
              :label="item.label"
            >
              {{ item.value }}
            </el-descriptions-item>
          </el-descriptions>

          <el-tabs v-model="detailTab" class="supplier-detail__tabs">
            <el-tab-pane label="资质证照" name="certs">
              <div class="supplier-detail__toolbar">
                <el-button
                  v-permission="'bas.supplier.action'"
                  size="small"
                  @click="certOpen = true"
                >
                  登记证照
                </el-button>
              </div>
              <QlxDataTable
                :items="detail.certs"
                :columns="certColumns"
                :total="detail.certs.length"
                :page-size="10"
                empty-text="暂无资质证照"
              >
                <template #cell-effectDate="{ row }">{{ formatDateOnly(row.effectDate) }}</template>
                <template #cell-expiryDate="{ row }">{{ formatDateOnly(row.expiryDate) }}</template>
              </QlxDataTable>
            </el-tab-pane>

            <el-tab-pane label="银行账户" name="banks">
              <div class="supplier-detail__toolbar">
                <el-button
                  v-permission="'bas.supplier.action'"
                  size="small"
                  @click="bankOpen = true"
                >
                  登记账户
                </el-button>
              </div>
              <QlxDataTable
                :items="detail.banks"
                :columns="bankColumns"
                :total="detail.banks.length"
                :page-size="10"
                empty-text="暂无银行账户"
              />
            </el-tab-pane>

            <el-tab-pane label="开票信息" name="invoices">
              <div class="supplier-detail__toolbar">
                <el-button
                  v-permission="'bas.supplier.action'"
                  size="small"
                  @click="invoiceOpen = true"
                >
                  登记开票信息
                </el-button>
              </div>
              <QlxDataTable
                :items="detail.invoices"
                :columns="invoiceColumns"
                :total="detail.invoices.length"
                :page-size="10"
                empty-text="暂无开票信息"
              >
                <template #cell-invoiceType="{ row }">{{ invoiceTypeLabel(row.invoiceType) }}</template>
              </QlxDataTable>
            </el-tab-pane>

            <el-tab-pane label="评级历史" name="gradeLogs">
              <el-table :data="detail.gradeLogs" size="small" border>
                <el-table-column label="原评级" width="110">
                  <template #default="{ row }">{{ supplierGradeLabel(row.oldGrade) }}</template>
                </el-table-column>
                <el-table-column label="新评级" width="110">
                  <template #default="{ row }">{{ supplierGradeLabel(row.newGrade) }}</template>
                </el-table-column>
                <el-table-column prop="reason" label="变更原因" min-width="180" />
                <el-table-column label="审批人" width="130">
                  <template #default="{ row }">{{ row.approvedBy ?? '—' }}</template>
                </el-table-column>
                <el-table-column label="时间" width="160">
                  <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
                </el-table-column>
              </el-table>
            </el-tab-pane>
          </el-tabs>
        </template>
      </div>
    </el-drawer>

    <QlxFormDialog
      v-model="certOpen"
      title="登记资质证照"
      :fields="certFields"
      :submitting="certSubmitting"
      submit-text="保存"
      :width="600"
      class="master-form-dialog"
      @submit="onCertSubmit"
    />

    <QlxFormDialog
      v-model="bankOpen"
      title="登记银行账户"
      :fields="bankFields"
      :submitting="bankSubmitting"
      submit-text="保存"
      :width="600"
      class="master-form-dialog"
      @submit="onBankSubmit"
    />

    <QlxFormDialog
      v-model="invoiceOpen"
      title="登记开票信息"
      :fields="invoiceFields"
      :submitting="invoiceSubmitting"
      submit-text="保存"
      :width="600"
      class="master-form-dialog"
      @submit="onInvoiceSubmit"
    />

    <MasterImportDialog
      v-model="importDialogOpen"
      title="导入供应商"
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

.supplier-detail__tabs {
  margin-top: var(--qlx-space-4);
}

.supplier-detail__toolbar {
  margin-bottom: var(--qlx-space-3);
}
</style>

<style>
@media (max-width: 767px) {
  .master-form-dialog.el-dialog {
    width: min(600px, 92vw);
  }
}
</style>
