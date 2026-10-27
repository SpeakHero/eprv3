<!--
  所属公司：四川淇利馨集团有限公司
  描述：客户主数据——分页列表 + 新建 / 编辑 + 准入申请 + 信用额度调整 + 详情抽屉（额度 / 账户 / 开票 / 信用历史）
  说明：
    1. 编码由后端生成，只读展示，表单无编码输入框（N-1）；枚举一律数值对接（B-4）；
    2. 金额为后端原始数值，千分位展示由前端公共层完成（B-2-2）；
    3. 导入导出模板编码经后端清单校验（bas.customer.import / .export）。
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
import { customerApi, IMPORT_TEMPLATE_CODES } from '@/api/basMaster'
import { useCrudTable } from '@/composables/useCrudTable'
import { useMasterTransfer } from '@/composables/useMasterTransfer'
import type {
  CustomerBankRow,
  CustomerCreditGrade,
  CustomerCreditViewRow,
  CustomerDetailRow,
  CustomerInvoiceRow,
  CustomerRow,
  PartnerType,
  SaveCustomerBankPayload,
  SaveCustomerInvoicePayload,
  SaveCustomerPayload,
} from '@/types/bas-master'
import {
  ENUM_OPTION_MAPS,
  approvalRequestStatusLabel,
  creditGradeLabel,
  creditGradeTone,
  formatAmount,
  invoiceTypeLabel,
  partnerStatusLabel,
  partnerStatusTone,
  partnerTypeLabel,
  toNumberOptions,
  toStringOptions,
} from '@/utils/bas-master-mappers'
import { formatDateTime } from '@/utils/format'

const searchFields: SearchField[] = [
  {
    prop: 'creditGrade',
    label: '信用等级',
    type: 'select',
    options: toStringOptions(ENUM_OPTION_MAPS.creditGrade),
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
} = useCrudTable<CustomerRow>((params) =>
  customerApi.list({
    page: params.page,
    pageSize: params.pageSize,
    creditGrade:
      typeof params.creditGrade === 'string' && params.creditGrade ? params.creditGrade : undefined,
    status: typeof params.status === 'string' && params.status ? params.status : undefined,
    keyword: typeof params.keyword === 'string' && params.keyword ? params.keyword : undefined,
  }),
)

const columns: Array<CrudColumn<CustomerRow>> = [
  { prop: 'code', label: '客户编码', type: 'code', width: 150, fixed: 'left' },
  { prop: 'name', label: '客户名称', minWidth: 200 },
  { prop: 'uscc', label: '统一社会信用代码', type: 'code', width: 190 },
  { prop: 'customerType', label: '客户类型', width: 120 },
  {
    prop: 'creditGrade',
    label: '信用等级',
    type: 'tag',
    width: 100,
    align: 'center',
    tagText: (row) => creditGradeLabel(row.creditGrade),
    tagTone: (row) => creditGradeTone(row.creditGrade),
  },
  { prop: 'creditLimit', label: '信用额度', width: 130, align: 'right' },
  { prop: 'creditUsed', label: '已用额度', width: 130, align: 'right' },
  {
    prop: 'status',
    label: '合作状态',
    type: 'tag',
    width: 100,
    align: 'center',
    tagText: (row) => partnerStatusLabel(row.status),
    tagTone: (row) => partnerStatusTone(row.status),
  },
  { prop: 'updatedAt', label: '更新时间', type: 'time', width: 160 },
]

/** ============ 新建 / 编辑（编码只读，N-1） ============ */

const formOpen = ref(false)
const submitting = ref(false)
const editing = ref<CustomerRow | null>(null)

const formFields = computed<FormField[]>(() => [
  {
    prop: 'name',
    label: '客户名称',
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
    prop: 'customerType',
    label: '客户类型',
    type: 'select',
    required: true,
    options: toNumberOptions(ENUM_OPTION_MAPS.partnerType),
    defaultValue: editing.value?.customerType ?? 1,
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
  {
    prop: 'paymentTerms',
    label: '付款条件',
    type: 'input',
    maxlength: 64,
    full: true,
    defaultValue: editing.value?.paymentTerms ?? '',
    hint: '如：月结 30 天',
  },
])

function openCreate(): void {
  editing.value = null
  formOpen.value = true
}

function openEdit(row: CustomerRow): void {
  editing.value = row
  formOpen.value = true
}

async function onSubmit(values: Record<string, FormValue>): Promise<void> {
  submitting.value = true
  try {
    const payload: SaveCustomerPayload = {
      name: String(values.name ?? '').trim(),
      uscc: String(values.uscc ?? '').trim().toUpperCase(),
      customerType: Number(values.customerType ?? 1) as PartnerType,
      contact: String(values.contact ?? '').trim() || null,
      phone: String(values.phone ?? '').trim() || null,
      paymentTerms: String(values.paymentTerms ?? '').trim() || null,
    }
    if (editing.value) {
      await customerApi.update(editing.value.id, payload)
      ElMessage.success('客户信息已保存')
    } else {
      await customerApi.create(payload)
      ElMessage.success('客户已创建')
    }
    formOpen.value = false
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存客户失败')
  } finally {
    submitting.value = false
  }
}

async function submitAdmission(row: CustomerRow): Promise<void> {
  try {
    const result = await ElMessageBox.prompt(
      `为「${row.name}」提交准入审批，审批通过后合作状态转为合作中。请填写准入原因：`,
      '提交准入申请',
      {
        confirmButtonText: '提交申请',
        cancelButtonText: '取消',
        inputPlaceholder: '如：年度客户信用评审通过',
        inputValidator: (value) => (value?.trim() ? true : '请填写准入原因'),
      },
    )
    const request = await customerApi.submitAdmission(row.id, String(result.value ?? '').trim())
    ElMessage.success(`准入申请已提交：${approvalRequestStatusLabel(request.status)}`)
    await refresh()
  } catch (error) {
    if (error instanceof Error) {
      ElMessage.error(error.message)
    }
  }
}

/** ============ 详情抽屉 ============ */

const detailOpen = ref(false)
const detailLoading = ref(false)
const detailTab = ref<'credit' | 'banks' | 'invoices' | 'creditLogs'>('credit')
const detail = ref<CustomerDetailRow | null>(null)
const credit = ref<CustomerCreditViewRow | null>(null)

async function openDetail(row: CustomerRow): Promise<void> {
  detailOpen.value = true
  detailTab.value = 'credit'
  detailLoading.value = true
  try {
    const [detailRow, creditRow] = await Promise.all([
      customerApi.get(row.id),
      customerApi.getCredit(row.id),
    ])
    detail.value = detailRow
    credit.value = creditRow
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载客户详情失败')
  } finally {
    detailLoading.value = false
  }
}

async function refreshDetail(): Promise<void> {
  if (!detail.value) {
    return
  }
  const master = detail.value.master.id
  const [detailRow, creditRow] = await Promise.all([
    customerApi.get(master),
    customerApi.getCredit(master),
  ])
  detail.value = detailRow
  credit.value = creditRow
}

const detailItems = computed(() => {
  const master = detail.value?.master
  if (!master) {
    return []
  }
  return [
    { label: '客户编码', value: master.code },
    { label: '客户名称', value: master.name },
    { label: '统一社会信用代码', value: master.uscc },
    { label: '客户类型', value: partnerTypeLabel(master.customerType) },
    { label: '信用等级', value: creditGradeLabel(master.creditGrade) },
    { label: '合作状态', value: partnerStatusLabel(master.status) },
    { label: '联系人', value: master.contact ?? '—' },
    { label: '联系电话', value: master.phone ?? '—' },
    { label: '付款条件', value: master.paymentTerms ?? '—' },
    {
      label: '信用额度',
      value: `${formatAmount(master.creditLimit)} / 已用 ${formatAmount(master.creditUsed)}`,
    },
    { label: '更新时间', value: formatDateTime(master.updatedAt) },
  ]
})

const bankColumns: Array<CrudColumn<CustomerBankRow>> = [
  { prop: 'bankName', label: '开户银行', minWidth: 160 },
  { prop: 'accountName', label: '账户名称', minWidth: 160 },
  { prop: 'accountNo', label: '银行账号', type: 'code', minWidth: 190 },
  { prop: 'isDefault', label: '默认账户', type: 'check', width: 100, align: 'center' },
]

const invoiceColumns: Array<CrudColumn<CustomerInvoiceRow>> = [
  { prop: 'title', label: '开票抬头', minWidth: 200 },
  { prop: 'taxNo', label: '税号', type: 'code', minWidth: 180 },
  { prop: 'invoiceType', label: '开票类型', width: 160 },
  { prop: 'bankInfo', label: '开户行及账号', minWidth: 200 },
  { prop: 'addrPhone', label: '地址电话', minWidth: 180 },
]

/** ============ 子表登记 ============ */

const bankOpen = ref(false)
const invoiceOpen = ref(false)
const bankSubmitting = ref(false)
const invoiceSubmitting = ref(false)

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

async function onBankSubmit(values: Record<string, FormValue>): Promise<void> {
  const master = detail.value?.master
  if (!master) {
    return
  }
  bankSubmitting.value = true
  try {
    const payload: SaveCustomerBankPayload = {
      bankName: String(values.bankName ?? '').trim(),
      accountName: String(values.accountName ?? '').trim(),
      accountNo: String(values.accountNo ?? '').trim(),
      isDefault: Boolean(values.isDefault),
    }
    await customerApi.saveBank(master.id, payload)
    bankOpen.value = false
    await refreshDetail()
    ElMessage.success('回款账户已登记')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '登记回款账户失败')
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
    const payload: SaveCustomerInvoicePayload = {
      title: String(values.title ?? '').trim(),
      taxNo: String(values.taxNo ?? '').trim() || null,
      bankInfo: String(values.bankInfo ?? '').trim() || null,
      addrPhone: String(values.addrPhone ?? '').trim() || null,
      invoiceType: Number(values.invoiceType ?? 1) as 1 | 2 | 3,
    }
    await customerApi.saveInvoice(master.id, payload)
    invoiceOpen.value = false
    await refreshDetail()
    ElMessage.success('开票信息已登记')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '登记开票信息失败')
  } finally {
    invoiceSubmitting.value = false
  }
}

/** ============ 信用额度调整（审批申请） ============ */

const adjustOpen = ref(false)
const adjustSubmitting = ref(false)
const adjustTarget = ref<CustomerRow | null>(null)

const adjustFields = computed<FormField[]>(() => [
  {
    prop: 'newLimit',
    label: '新信用额度',
    type: 'number',
    required: true,
    defaultValue: credit.value?.creditLimit ?? adjustTarget.value?.creditLimit ?? 0,
    hint: '按原始金额填写，展示层自动千分位',
  },
  {
    prop: 'newCreditGrade',
    label: '新信用等级',
    type: 'select',
    required: true,
    options: toNumberOptions(ENUM_OPTION_MAPS.creditGrade),
    defaultValue: credit.value?.creditGrade ?? adjustTarget.value?.creditGrade ?? 2,
  },
  {
    prop: 'reason',
    label: '调整原因',
    type: 'textarea',
    full: true,
    maxlength: 200,
    defaultValue: '',
  },
])

function openAdjust(row: CustomerRow): void {
  adjustTarget.value = row
  adjustOpen.value = true
}

async function onAdjustSubmit(values: Record<string, FormValue>): Promise<void> {
  const target = adjustTarget.value
  if (!target) {
    return
  }
  adjustSubmitting.value = true
  try {
    const request = await customerApi.submitCreditAdjust(target.id, {
      newLimit: Number(values.newLimit ?? 0),
      newCreditGrade: Number(values.newCreditGrade ?? target.creditGrade) as CustomerCreditGrade,
      reason: String(values.reason ?? '').trim() || null,
    })
    adjustOpen.value = false
    ElMessage.success(`额度调整申请已提交：${approvalRequestStatusLabel(request.status)}`)
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '提交额度调整申请失败')
  } finally {
    adjustSubmitting.value = false
  }
}

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
  templateCode: IMPORT_TEMPLATE_CODES.customer,
  exportParams: () => ({
    status: query.value.status || undefined,
    keyword: query.value.keyword || undefined,
  }),
  onImported: refresh,
})
</script>

<template>
  <QlxPagePanel title="客户管理" subtitle="准入 · 信用等级与额度 · 回款账户与开票信息（规范 5.2.2）">
    <template #toolbar>
      <el-button v-permission="'bas.customer.action'" type="primary" @click="openCreate">
        <AppIcon name="Plus" :size="14" />
        新增客户
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
      :actions-width="240"
      empty-text="暂无客户"
      @page-change="changePage"
    >
      <template #cell-customerType="{ row }">{{ partnerTypeLabel(row.customerType) }}</template>
      <template #cell-creditLimit="{ row }">
        <span class="qlx-numeric">{{ formatAmount(row.creditLimit) }}</span>
      </template>
      <template #cell-creditUsed="{ row }">
        <span class="qlx-numeric">{{ formatAmount(row.creditUsed) }}</span>
      </template>
      <template #actions="{ row }">
        <button
          v-permission="'bas.customer.menu'"
          type="button"
          class="cell-link"
          @click="openDetail(row)"
        >
          详情
        </button>
        <el-divider direction="vertical" />
        <button
          v-permission="'bas.customer.action'"
          type="button"
          class="cell-link"
          @click="openEdit(row)"
        >
          编辑
        </button>
        <el-divider direction="vertical" />
        <button
          v-permission="'bas.customer.action'"
          type="button"
          class="cell-link"
          @click="openAdjust(row)"
        >
          额度
        </button>
        <el-divider direction="vertical" />
        <button
          v-permission="'bas.customer.action'"
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
      :title="editing ? `编辑客户 · ${editing.name}` : '新增客户'"
      :fields="formFields"
      :submitting="submitting"
      submit-text="保存客户"
      :width="600"
      class="master-form-dialog"
      @submit="onSubmit"
    />

    <QlxFormDialog
      v-model="adjustOpen"
      :title="adjustTarget ? `信用额度调整 · ${adjustTarget.name}` : '信用额度调整'"
      :fields="adjustFields"
      :submitting="adjustSubmitting"
      submit-text="提交申请"
      :width="560"
      class="master-form-dialog"
      @submit="onAdjustSubmit"
    />

    <!-- 详情抽屉 -->
    <el-drawer v-model="detailOpen" title="客户详情" size="760px" append-to-body>
      <div v-loading="detailLoading" class="customer-detail">
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

          <el-tabs v-model="detailTab" class="customer-detail__tabs">
            <el-tab-pane label="信用额度" name="credit">
              <div v-if="credit" class="credit-cards">
                <div class="credit-card">
                  <span class="credit-card__label">信用额度</span>
                  <strong class="qlx-numeric">{{ formatAmount(credit.creditLimit) }}</strong>
                </div>
                <div class="credit-card">
                  <span class="credit-card__label">已用额度</span>
                  <strong class="qlx-numeric">{{ formatAmount(credit.creditUsed) }}</strong>
                </div>
                <div class="credit-card">
                  <span class="credit-card__label">可用额度</span>
                  <strong class="qlx-numeric">{{ formatAmount(credit.available) }}</strong>
                </div>
                <div class="credit-card">
                  <span class="credit-card__label">信用等级</span>
                  <el-tag :type="creditGradeTone(credit.creditGrade)" size="small" effect="light">
                    {{ creditGradeLabel(credit.creditGrade) }}
                  </el-tag>
                </div>
              </div>
              <p class="customer-detail__note">
                额度调整经审批通过后生效；可用额度由后端按业务规则计算，前端仅展示。
              </p>
            </el-tab-pane>

            <el-tab-pane label="回款账户" name="banks">
              <div class="customer-detail__toolbar">
                <el-button
                  v-permission="'bas.customer.action'"
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
                empty-text="暂无回款账户"
              />
            </el-tab-pane>

            <el-tab-pane label="开票信息" name="invoices">
              <div class="customer-detail__toolbar">
                <el-button
                  v-permission="'bas.customer.action'"
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

            <el-tab-pane label="信用历史" name="creditLogs">
              <el-table :data="detail.creditLogs" size="small" border>
                <el-table-column label="原等级" width="100">
                  <template #default="{ row }">{{ creditGradeLabel(row.oldCreditGrade) }}</template>
                </el-table-column>
                <el-table-column label="新等级" width="100">
                  <template #default="{ row }">{{ creditGradeLabel(row.newCreditGrade) }}</template>
                </el-table-column>
                <el-table-column label="原额度" width="120" align="right">
                  <template #default="{ row }">
                    <span class="qlx-numeric">{{ formatAmount(row.oldLimit) }}</span>
                  </template>
                </el-table-column>
                <el-table-column label="新额度" width="120" align="right">
                  <template #default="{ row }">
                    <span class="qlx-numeric">{{ formatAmount(row.newLimit) }}</span>
                  </template>
                </el-table-column>
                <el-table-column prop="reason" label="调整原因" min-width="160" />
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
      v-model="bankOpen"
      title="登记回款账户"
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
      title="导入客户"
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

.customer-detail__tabs {
  margin-top: var(--qlx-space-4);
}

.customer-detail__toolbar {
  margin-bottom: var(--qlx-space-3);
}

.customer-detail__note {
  margin-top: var(--qlx-space-3);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.credit-cards {
  display: grid;
  gap: var(--qlx-space-3);
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
}

.credit-card {
  display: grid;
  padding: var(--qlx-space-3);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-canvas);
  gap: 4px;
}

.credit-card__label {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.credit-card strong {
  color: var(--qlx-brand-900);
  font-size: var(--qlx-text-base);
  font-weight: 600;
}
</style>

<style>
@media (max-width: 767px) {
  .master-form-dialog.el-dialog {
    width: min(600px, 92vw);
  }
}
</style>
