<!--
  所属公司：四川淇利馨集团有限公司
  描述：仓库档案——仓库扁平列表 + 新建 / 编辑 + 启停 / 删除 + 库区库位抽屉（两 Tab CRUD）
  说明：
    1. 仓库编码由后端生成，只读展示，表单无编码输入框（N-1）；
    2. 仓库 / 库区 / 库位端点均返回扁平集合，中文映射在前端公共层（B-4）；
    3. 库位新建的库区 Id 为查询参数（后端契约），库位条码为可空字段。
-->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import QlxFormDialog from '@/components/crud/QlxFormDialog.vue'
import type { FormField, FormValue } from '@/components/crud/QlxFormDialog.vue'
import { orgMasterApi, personApi } from '@/api/basMaster'
import { warehouseApi } from '@/api/wms'
import { useLocalTable } from '@/composables/useLocalTable'
import type { BasOrgRow, PersonRow } from '@/types/bas-master'
import type {
  CreateLocationPayload,
  CreateWarehousePayload,
  LocationRow,
  UpdateLocationPayload,
  UpdateWarehousePayload,
  WarehouseRow,
  WarehouseType,
  ZoneRow,
} from '@/types/wms'
import { toNumberOptions, WMS_ENUM_OPTION_MAPS, whTypeLabel, whTypeTone, warehouseStatusLabel, warehouseStatusTone } from '@/utils/wms-enums'

/* ============================================================
 * 一、仓库列表
 * ============================================================ */

const { loading, items, total, page, pageSize, changePage, refresh } = useLocalTable<WarehouseRow>(
  () => warehouseApi.list(),
)

const orgs = ref<BasOrgRow[]>([])
const persons = ref<PersonRow[]>([])

/** 跨模块下拉加载失败（独立菜单权限）不阻断仓库页。 */
async function loadOptions(): Promise<void> {
  try {
    orgs.value = await orgMasterApi.list()
  } catch {
    orgs.value = []
  }
  try {
    const result = await personApi.list({ page: 1, pageSize: 200 })
    persons.value = result.items
  } catch {
    persons.value = []
  }
}

function orgName(id: string | null): string {
  if (!id) {
    return '—'
  }
  return orgs.value.find((org) => org.id === id)?.name ?? '—'
}

function keepterName(id: string | null): string {
  if (!id) {
    return '—'
  }
  return persons.value.find((person) => person.id === id)?.name ?? '—'
}

const columns: Array<CrudColumn<WarehouseRow>> = [
  { prop: 'code', label: '仓库编码', type: 'code', minWidth: 190, fixed: 'left' },
  { prop: 'name', label: '仓库名称', minWidth: 180 },
  {
    prop: 'whType',
    label: '仓库类型',
    type: 'tag',
    width: 110,
    align: 'center',
    tagText: (row) => whTypeLabel(row.whType),
    tagTone: (row) => whTypeTone(row.whType),
  },
  { prop: 'orgId', label: '所属组织', minWidth: 160 },
  { prop: 'address', label: '地址', minWidth: 200 },
  { prop: 'keeperId', label: '仓管员', width: 120 },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 90,
    align: 'center',
    tagText: (row) => warehouseStatusLabel(row.status),
    tagTone: (row) => warehouseStatusTone(row.status),
  },
]

/* ============================================================
 * 二、仓库新建 / 编辑（编码只读，N-1）
 * ============================================================ */

const formOpen = ref(false)
const submitting = ref(false)
const editing = ref<WarehouseRow | null>(null)

const formFields = computed<FormField[]>(() => [
  {
    prop: 'name',
    label: '仓库名称',
    type: 'input',
    required: true,
    maxlength: 60,
    full: true,
    defaultValue: editing.value?.name ?? '',
    hint: editing.value
      ? `编码 ${editing.value.code} 由系统生成，不可修改`
      : '编码由系统按规则自动生成，无需填写',
  },
  {
    prop: 'whType',
    label: '仓库类型',
    type: 'select',
    required: true,
    options: toNumberOptions(WMS_ENUM_OPTION_MAPS.whType),
    defaultValue: editing.value?.whType ?? 1,
  },
  {
    prop: 'orgId',
    label: '所属组织',
    type: 'select',
    filterable: true,
    options: [
      { label: '暂不指定', value: '' },
      ...orgs.value.map((org) => ({ label: org.name, value: org.id })),
    ],
    defaultValue: editing.value?.orgId ?? '',
  },
  {
    prop: 'address',
    label: '仓库地址',
    type: 'input',
    maxlength: 200,
    full: true,
    defaultValue: editing.value?.address ?? '',
  },
  {
    prop: 'keeperId',
    label: '仓管员',
    type: 'select',
    filterable: true,
    options: [
      { label: '暂不指定', value: '' },
      ...persons.value.map((person) => ({ label: person.name, value: person.id })),
    ],
    defaultValue: editing.value?.keeperId ?? '',
  },
  {
    prop: 'remark',
    label: '备注',
    type: 'textarea',
    full: true,
    maxlength: 200,
    defaultValue: editing.value?.remark ?? '',
  },
])

function openCreate(): void {
  editing.value = null
  formOpen.value = true
}

function openEdit(row: WarehouseRow): void {
  editing.value = row
  formOpen.value = true
}

async function onSubmit(values: Record<string, FormValue>): Promise<void> {
  submitting.value = true
  try {
    const payload: UpdateWarehousePayload | CreateWarehousePayload = {
      name: String(values.name ?? '').trim(),
      whType: Number(values.whType ?? 1) as WarehouseType,
      orgId: values.orgId ? String(values.orgId) : null,
      address: String(values.address ?? '').trim() || null,
      keeperId: values.keeperId ? String(values.keeperId) : null,
      remark: String(values.remark ?? '').trim() || null,
    }
    if (editing.value) {
      await warehouseApi.update(editing.value.id, payload)
      ElMessage.success('仓库信息已保存')
    } else {
      await warehouseApi.create(payload)
      ElMessage.success('仓库已创建')
    }
    formOpen.value = false
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存仓库失败')
  } finally {
    submitting.value = false
  }
}

async function onToggle(row: WarehouseRow): Promise<void> {
  const disabling = row.status === 1
  try {
    await ElMessageBox.confirm(
      disabling
        ? `停用仓库「${row.name}」后，其库区库位与库存不可再办理业务，是否继续？`
        : `启用仓库「${row.name}」后恢复业务办理，是否继续？`,
      disabling ? '停用仓库' : '启用仓库',
      { confirmButtonText: '确认', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    const result = await warehouseApi.toggle(row.id)
    await refresh()
    if (result.approvalRequired) {
      ElMessage.success('停用申请已提交，审批通过后自动生效')
    } else {
      ElMessage.success(disabling ? '仓库已停用' : '仓库已启用')
    }
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

async function onRemove(row: WarehouseRow): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `删除仓库「${row.name}」后不可恢复（软删，历史数据保留），是否继续？`,
      '删除仓库',
      { confirmButtonText: '确认删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await warehouseApi.remove(row.id)
    await refresh()
    ElMessage.success('仓库已删除')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '删除仓库失败')
  }
}

/* ============================================================
 * 三、库区 / 库位抽屉
 * ============================================================ */

const drawerOpen = ref(false)
const activeTab = ref<'zones' | 'locations'>('zones')
const activeWarehouse = ref<WarehouseRow | null>(null)
const zones = ref<ZoneRow[]>([])
const locations = ref<LocationRow[]>([])
const subLoading = ref(false)

async function openStructure(row: WarehouseRow): Promise<void> {
  activeWarehouse.value = row
  activeTab.value = 'zones'
  drawerOpen.value = true
  await loadStructure()
}

async function loadStructure(): Promise<void> {
  const warehouse = activeWarehouse.value
  if (!warehouse) {
    return
  }
  subLoading.value = true
  try {
    const [zoneRows, locationRows] = await Promise.all([
      warehouseApi.listZones(warehouse.id),
      warehouseApi.listLocations(warehouse.id),
    ])
    zones.value = zoneRows
    locations.value = locationRows
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载库区库位失败')
  } finally {
    subLoading.value = false
  }
}

/* ---- 库区 ---- */

const zoneColumns: Array<CrudColumn<ZoneRow>> = [
  { prop: 'code', label: '库区编码', type: 'code', width: 140 },
  { prop: 'name', label: '库区名称', minWidth: 160 },
  { prop: 'sortOrder', label: '排序', width: 80, align: 'center' },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 90,
    align: 'center',
    tagText: (row) => warehouseStatusLabel(row.status),
    tagTone: (row) => warehouseStatusTone(row.status),
  },
]

const zoneOpen = ref(false)
const zoneSubmitting = ref(false)
const editingZone = ref<ZoneRow | null>(null)

const zoneFields = computed<FormField[]>(() => {
  if (editingZone.value) {
    return [
      {
        prop: 'name',
        label: '库区名称',
        type: 'input',
        required: true,
        maxlength: 60,
        full: true,
        defaultValue: editingZone.value.name,
        hint: `编码 ${editingZone.value.code} 创建后不可修改`,
      },
      {
        prop: 'sortOrder',
        label: '排序号',
        type: 'number',
        defaultValue: editingZone.value.sortOrder,
      },
    ]
  }
  return [
    { prop: 'code', label: '库区编码', type: 'input', required: true, maxlength: 20 },
    {
      prop: 'name',
      label: '库区名称',
      type: 'input',
      required: true,
      maxlength: 60,
      full: true,
    },
    { prop: 'sortOrder', label: '排序号', type: 'number', defaultValue: 0 },
  ]
})

function openCreateZone(): void {
  editingZone.value = null
  zoneOpen.value = true
}

function openEditZone(row: ZoneRow): void {
  editingZone.value = row
  zoneOpen.value = true
}

async function onZoneSubmit(values: Record<string, FormValue>): Promise<void> {
  const warehouse = activeWarehouse.value
  if (!warehouse) {
    return
  }
  zoneSubmitting.value = true
  try {
    if (editingZone.value) {
      await warehouseApi.updateZone(editingZone.value.id, {
        name: String(values.name ?? '').trim(),
        sortOrder: Number(values.sortOrder ?? 0),
      })
      ElMessage.success('库区已保存')
    } else {
      await warehouseApi.createZone(warehouse.id, {
        code: String(values.code ?? '').trim(),
        name: String(values.name ?? '').trim(),
        sortOrder: Number(values.sortOrder ?? 0),
      })
      ElMessage.success('库区已创建')
    }
    zoneOpen.value = false
    await loadStructure()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存库区失败')
  } finally {
    zoneSubmitting.value = false
  }
}

async function onToggleZone(row: ZoneRow): Promise<void> {
  const disabling = row.status === 1
  try {
    await ElMessageBox.confirm(
      `${disabling ? '停用' : '启用'}库区「${row.name}」，是否继续？`,
      disabling ? '停用库区' : '启用库区',
      { confirmButtonText: '确认', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await warehouseApi.toggleZone(row.id)
    await loadStructure()
    ElMessage.success(disabling ? '库区已停用' : '库区已启用')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

async function onRemoveZone(row: ZoneRow): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `删除库区「${row.name}」后不可恢复（软删），是否继续？`,
      '删除库区',
      { confirmButtonText: '确认删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await warehouseApi.removeZone(row.id)
    await loadStructure()
    ElMessage.success('库区已删除')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '删除库区失败')
  }
}

/* ---- 库位 ---- */
const locationColumns: Array<CrudColumn<LocationRow>> = [
  { prop: 'code', label: '库位编码', type: 'code', width: 140 },
  { prop: 'barcode', label: '库位条码', minWidth: 160 },
  { prop: 'zoneId', label: '所属库区', minWidth: 160 },
  { prop: 'sortOrder', label: '排序', width: 80, align: 'center' },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 90,
    align: 'center',
    tagText: (row) => warehouseStatusLabel(row.status),
    tagTone: (row) => warehouseStatusTone(row.status),
  },
]

const locationOpen = ref(false)
const locationSubmitting = ref(false)
const editingLocation = ref<LocationRow | null>(null)

function zoneName(id: string): string {
  return zones.value.find((zone) => zone.id === id)?.name ?? '—'
}

const locationFields = computed<FormField[]>(() => {
  if (editingLocation.value) {
    return [
      {
        prop: 'barcode',
        label: '库位条码',
        type: 'input',
        maxlength: 64,
        full: true,
        defaultValue: editingLocation.value.barcode ?? '',
        hint: `编码 ${editingLocation.value.code} 创建后不可修改`,
      },
      {
        prop: 'sortOrder',
        label: '排序号',
        type: 'number',
        defaultValue: editingLocation.value.sortOrder,
      },
    ]
  }
  return [
    {
      prop: 'zoneId',
      label: '所属库区',
      type: 'select',
      required: true,
      filterable: true,
      full: true,
      options: zones.value.map((zone) => ({ label: `${zone.code} · ${zone.name}`, value: zone.id })),
    },
    { prop: 'code', label: '库位编码', type: 'input', required: true, maxlength: 20 },
    { prop: 'barcode', label: '库位条码', type: 'input', maxlength: 64 },
    { prop: 'sortOrder', label: '排序号', type: 'number', defaultValue: 0 },
  ]
})

function openCreateLocation(): void {
  editingLocation.value = null
  locationOpen.value = true
}

function openEditLocation(row: LocationRow): void {
  editingLocation.value = row
  locationOpen.value = true
}

async function onLocationSubmit(values: Record<string, FormValue>): Promise<void> {
  const warehouse = activeWarehouse.value
  if (!warehouse) {
    return
  }
  locationSubmitting.value = true
  try {
    if (editingLocation.value) {
      const payload: UpdateLocationPayload = {
        barcode: String(values.barcode ?? '').trim() || null,
        sortOrder: Number(values.sortOrder ?? 0),
      }
      await warehouseApi.updateLocation(editingLocation.value.id, payload)
      ElMessage.success('库位已保存')
    } else {
      const zoneId = String(values.zoneId ?? '')
      const payload: CreateLocationPayload = {
        code: String(values.code ?? '').trim(),
        barcode: String(values.barcode ?? '').trim() || null,
        sortOrder: Number(values.sortOrder ?? 0),
      }
      await warehouseApi.createLocation(warehouse.id, zoneId, payload)
      ElMessage.success('库位已创建')
    }
    locationOpen.value = false
    await loadStructure()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存库位失败')
  } finally {
    locationSubmitting.value = false
  }
}

async function onToggleLocation(row: LocationRow): Promise<void> {
  const disabling = row.status === 1
  try {
    await ElMessageBox.confirm(
      `${disabling ? '停用' : '启用'}库位「${row.code}」，是否继续？`,
      disabling ? '停用库位' : '启用库位',
      { confirmButtonText: '确认', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await warehouseApi.toggleLocation(row.id)
    await loadStructure()
    ElMessage.success(disabling ? '库位已停用' : '库位已启用')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

async function onRemoveLocation(row: LocationRow): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `删除库位「${row.code}」后不可恢复（软删），是否继续？`,
      '删除库位',
      { confirmButtonText: '确认删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await warehouseApi.removeLocation(row.id)
    await loadStructure()
    ElMessage.success('库位已删除')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '删除库位失败')
  }
}

onMounted(loadOptions)
</script>

<template>
  <QlxPagePanel title="仓库档案" subtitle="仓库 · 库区 · 库位（编码由系统生成）">
    <template #toolbar>
      <el-button v-permission="'wms.warehouse.action'" type="primary" @click="openCreate">
        <AppIcon name="Plus" :size="14" />
        新增仓库
      </el-button>
    </template>

    <QlxDataTable
      :items="items"
      :columns="columns"
      :loading="loading"
      :total="total"
      :page="page"
      :page-size="pageSize"
      :actions-width="250"
      empty-text="暂无仓库"
      @page-change="changePage"
    >
      <template #cell-orgId="{ row }">{{ orgName(row.orgId) }}</template>
      <template #cell-address="{ row }">{{ row.address ?? '—' }}</template>
      <template #cell-keeperId="{ row }">{{ keepterName(row.keeperId) }}</template>
      <template #actions="{ row }">
        <button
          v-permission="'wms.warehouse.action'"
          type="button"
          class="cell-link"
          @click="openStructure(row)"
        >
          库区库位
        </button>
        <el-divider direction="vertical" />
        <button
          v-permission="'wms.warehouse.action'"
          type="button"
          class="cell-link"
          @click="openEdit(row)"
        >
          编辑
        </button>
        <el-divider direction="vertical" />
        <button
          v-permission="'wms.warehouse.action'"
          type="button"
          class="cell-link"
          :class="{ 'is-danger': row.status === 1 }"
          @click="onToggle(row)"
        >
          {{ row.status === 1 ? '停用' : '启用' }}
        </button>
        <el-divider direction="vertical" />
        <button
          v-permission="'wms.warehouse.action'"
          type="button"
          class="cell-link is-danger"
          @click="onRemove(row)"
        >
          删除
        </button>
      </template>
    </QlxDataTable>

    <QlxFormDialog
      v-model="formOpen"
      :title="editing ? `编辑仓库 · ${editing.name}` : '新增仓库'"
      :fields="formFields"
      :submitting="submitting"
      submit-text="保存仓库"
      :width="620"
      class="wms-form-dialog"
      @submit="onSubmit"
    />

    <el-drawer
      v-model="drawerOpen"
      :title="activeWarehouse ? `库区库位 · ${activeWarehouse.name}` : '库区库位'"
      size="760px"
      append-to-body
    >
      <div v-loading="subLoading" class="warehouse-structure">
        <el-tabs v-model="activeTab">
          <el-tab-pane label="库区" name="zones">
            <div class="warehouse-structure__toolbar">
              <el-button
                v-permission="'wms.warehouse.action'"
                size="small"
                type="primary"
                @click="openCreateZone"
              >
                新增库区
              </el-button>
            </div>
            <QlxDataTable
              :items="zones"
              :columns="zoneColumns"
              :total="zones.length"
              :page-size="zones.length || 10"
              :actions-width="190"
              empty-text="暂无库区"
            >
              <template #actions="{ row }">
                <button
                  v-permission="'wms.warehouse.action'"
                  type="button"
                  class="cell-link"
                  @click="openEditZone(row)"
                >
                  编辑
                </button>
                <el-divider direction="vertical" />
                <button
                  v-permission="'wms.warehouse.action'"
                  type="button"
                  class="cell-link"
                  :class="{ 'is-danger': row.status === 1 }"
                  @click="onToggleZone(row)"
                >
                  {{ row.status === 1 ? '停用' : '启用' }}
                </button>
                <el-divider direction="vertical" />
                <button
                  v-permission="'wms.warehouse.action'"
                  type="button"
                  class="cell-link is-danger"
                  @click="onRemoveZone(row)"
                >
                  删除
                </button>
              </template>
            </QlxDataTable>
          </el-tab-pane>

          <el-tab-pane label="库位" name="locations">
            <div class="warehouse-structure__toolbar">
              <el-button
                v-permission="'wms.warehouse.action'"
                size="small"
                type="primary"
                @click="openCreateLocation"
              >
                新增库位
              </el-button>
            </div>
            <QlxDataTable
              :items="locations"
              :columns="locationColumns"
              :total="locations.length"
              :page-size="locations.length || 10"
              :actions-width="190"
              empty-text="暂无库位"
            >
              <template #cell-barcode="{ row }">{{ row.barcode ?? '—' }}</template>
              <template #cell-zoneId="{ row }">{{ zoneName(row.zoneId) }}</template>
              <template #actions="{ row }">
                <button
                  v-permission="'wms.warehouse.action'"
                  type="button"
                  class="cell-link"
                  @click="openEditLocation(row)"
                >
                  编辑
                </button>
                <el-divider direction="vertical" />
                <button
                  v-permission="'wms.warehouse.action'"
                  type="button"
                  class="cell-link"
                  :class="{ 'is-danger': row.status === 1 }"
                  @click="onToggleLocation(row)"
                >
                  {{ row.status === 1 ? '停用' : '启用' }}
                </button>
                <el-divider direction="vertical" />
                <button
                  v-permission="'wms.warehouse.action'"
                  type="button"
                  class="cell-link is-danger"
                  @click="onRemoveLocation(row)"
                >
                  删除
                </button>
              </template>
            </QlxDataTable>
          </el-tab-pane>
        </el-tabs>
      </div>
    </el-drawer>

    <QlxFormDialog
      v-model="zoneOpen"
      :title="editingZone ? `编辑库区 · ${editingZone.name}` : '新增库区'"
      :fields="zoneFields"
      :submitting="zoneSubmitting"
      submit-text="保存库区"
      :width="560"
      class="wms-form-dialog"
      @submit="onZoneSubmit"
    />

    <QlxFormDialog
      v-model="locationOpen"
      :title="editingLocation ? `编辑库位 · ${editingLocation.code}` : '新增库位'"
      :fields="locationFields"
      :submitting="locationSubmitting"
      submit-text="保存库位"
      :width="560"
      class="wms-form-dialog"
      @submit="onLocationSubmit"
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

.warehouse-structure {
  min-height: 320px;
}

.warehouse-structure__toolbar {
  margin-bottom: var(--qlx-space-3);
}
</style>

<style>
@media (max-width: 767px) {
  .wms-form-dialog.el-dialog {
    width: min(620px, 92vw);
  }
}
</style>
