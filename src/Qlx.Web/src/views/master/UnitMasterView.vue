<!--
  所属公司：四川淇利馨集团有限公司
  描述：计量单位主数据——单位组 / 计量单位 / 换算关系三 Tab（扁平列表 + 表单 + 启停）
  说明：
    1. 三个端点均返回扁平集合（无分页），页面本地维护列表并在变更后重查；
    2. 编码由后端生成，只读展示，表单无编码输入框（N-1）；
    3. 换算关系无启停端点（后端契约），仅新增 / 编辑，换算率以原始数值展示格式化。
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
import { unitApi } from '@/api/basMaster'
import type {
  SaveUnitConversionPayload,
  SaveUnitGroupPayload,
  SaveUnitPayload,
  UnitConversionRow,
  UnitGroupRow,
  UnitRow,
} from '@/types/bas-master'
import { basStatusLabel, basStatusTone, formatRate } from '@/utils/bas-master-mappers'

type DialogMode = 'group' | 'unit' | 'conversion'

const activeTab = ref<'groups' | 'units' | 'conversions'>('groups')

const groups = ref<UnitGroupRow[]>([])
const units = ref<UnitRow[]>([])
const conversions = ref<UnitConversionRow[]>([])
const loading = ref(false)

async function loadAll(): Promise<void> {
  loading.value = true
  try {
    const [groupRows, unitRows, conversionRows] = await Promise.all([
      unitApi.listGroups(),
      unitApi.list(),
      unitApi.listConversions(),
    ])
    groups.value = groupRows
    units.value = unitRows
    conversions.value = conversionRows
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载计量单位数据失败')
  } finally {
    loading.value = false
  }
}

function groupName(unitGroupId: string): string {
  return groups.value.find((group) => group.id === unitGroupId)?.name ?? '—'
}

function unitLabel(unitId: string): string {
  const unit = units.value.find((item) => item.id === unitId)
  return unit ? `${groupName(unit.unitGroupId)} / ${unit.name}` : '—'
}

/** ============ 表格列（数值枚举中文在前端字典） ============ */

const groupColumns: Array<CrudColumn<UnitGroupRow>> = [
  { prop: 'code', label: '单位组编码', type: 'code', width: 160 },
  { prop: 'name', label: '单位组名称', minWidth: 180 },
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

const unitColumns: Array<CrudColumn<UnitRow>> = [
  { prop: 'code', label: '单位编码', type: 'code', width: 150 },
  { prop: 'name', label: '单位名称', minWidth: 140 },
  { prop: 'unitGroupId', label: '所属单位组', minWidth: 160 },
  { prop: 'precision', label: '数量精度', width: 100, align: 'center' },
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

const conversionColumns: Array<CrudColumn<UnitConversionRow>> = [
  { prop: 'unitGroupId', label: '所属单位组', minWidth: 160 },
  { prop: 'fromUnitId', label: '源单位', minWidth: 160 },
  { prop: 'toUnitId', label: '目标单位', minWidth: 160 },
  { prop: 'rate', label: '换算率', width: 130, align: 'right' },
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

/** ============ 新建 / 编辑弹窗（编码只读，N-1） ============ */

const dialogOpen = ref(false)
const submitting = ref(false)
const mode = ref<DialogMode>('group')
const editingGroup = ref<UnitGroupRow | null>(null)
const editingUnit = ref<UnitRow | null>(null)
const editingConversion = ref<UnitConversionRow | null>(null)

const dialogTitle = computed(() => {
  if (mode.value === 'group') {
    return editingGroup.value ? `编辑单位组 · ${editingGroup.value.name}` : '新增单位组'
  }
  if (mode.value === 'unit') {
    return editingUnit.value ? `编辑计量单位 · ${editingUnit.value.name}` : '新增计量单位'
  }
  return editingConversion.value ? '编辑换算关系' : '新增换算关系'
})

const fields = computed<FormField[]>(() => {
  if (mode.value === 'group') {
    return [
      {
        prop: 'name',
        label: '单位组名称',
        type: 'input',
        required: true,
        maxlength: 32,
        defaultValue: editingGroup.value?.name ?? '',
        hint: editingGroup.value
          ? `编码 ${editingGroup.value.code} 由系统生成，不可修改`
          : '编码由系统按规则自动生成，无需填写',
      },
    ]
  }
  if (mode.value === 'unit') {
    return [
      {
        prop: 'name',
        label: '单位名称',
        type: 'input',
        required: true,
        maxlength: 20,
        defaultValue: editingUnit.value?.name ?? '',
        hint: editingUnit.value
          ? `编码 ${editingUnit.value.code} 由系统生成，不可修改`
          : '编码由系统按规则自动生成，无需填写',
      },
      {
        prop: 'unitGroupId',
        label: '所属单位组',
        type: 'select',
        required: true,
        filterable: true,
        options: groups.value.map((group) => ({ label: group.name, value: group.id })),
        defaultValue: editingUnit.value?.unitGroupId ?? '',
      },
      {
        prop: 'precision',
        label: '数量精度',
        type: 'number',
        defaultValue: editingUnit.value?.precision ?? 0,
        hint: '小数位数 0~8',
      },
    ]
  }
  return [
    {
      prop: 'unitGroupId',
      label: '所属单位组',
      type: 'select',
      required: true,
      filterable: true,
      options: groups.value.map((group) => ({ label: group.name, value: group.id })),
      defaultValue: editingConversion.value?.unitGroupId ?? '',
    },
    {
      prop: 'fromUnitId',
      label: '源单位',
      type: 'select',
      required: true,
      filterable: true,
      options: units.value.map((unit) => ({ label: unitLabel(unit.id), value: unit.id })),
      defaultValue: editingConversion.value?.fromUnitId ?? '',
    },
    {
      prop: 'toUnitId',
      label: '目标单位',
      type: 'select',
      required: true,
      filterable: true,
      options: units.value.map((unit) => ({ label: unitLabel(unit.id), value: unit.id })),
      defaultValue: editingConversion.value?.toUnitId ?? '',
    },
    {
      prop: 'rate',
      label: '换算率',
      type: 'number',
      full: true,
      defaultValue: editingConversion.value?.rate ?? 1,
      hint: '1 源单位 = 换算率 × 目标单位（支持小数）',
    },
  ]
})

function openCreate(): void {
  editingGroup.value = null
  editingUnit.value = null
  editingConversion.value = null
  if (activeTab.value === 'groups') {
    mode.value = 'group'
  } else if (activeTab.value === 'units') {
    mode.value = 'unit'
  } else {
    mode.value = 'conversion'
  }
  dialogOpen.value = true
}

function openEditGroup(row: UnitGroupRow): void {
  mode.value = 'group'
  editingGroup.value = row
  dialogOpen.value = true
}

function openEditUnit(row: UnitRow): void {
  mode.value = 'unit'
  editingUnit.value = row
  dialogOpen.value = true
}

function openEditConversion(row: UnitConversionRow): void {
  mode.value = 'conversion'
  editingConversion.value = row
  dialogOpen.value = true
}

async function onSubmit(values: Record<string, FormValue>): Promise<void> {
  submitting.value = true
  try {
    if (mode.value === 'group') {
      const payload: SaveUnitGroupPayload = {
        id: editingGroup.value?.id ?? null,
        name: String(values.name ?? '').trim(),
      }
      await unitApi.saveGroup(payload)
      ElMessage.success(editingGroup.value ? '单位组已保存' : '单位组已创建')
    } else if (mode.value === 'unit') {
      const payload: SaveUnitPayload = {
        id: editingUnit.value?.id ?? null,
        name: String(values.name ?? '').trim(),
        unitGroupId: String(values.unitGroupId ?? ''),
        precision: Number(values.precision ?? 0),
      }
      await unitApi.save(payload)
      ElMessage.success(editingUnit.value ? '计量单位已保存' : '计量单位已创建')
    } else {
      const payload: SaveUnitConversionPayload = {
        id: editingConversion.value?.id ?? null,
        unitGroupId: String(values.unitGroupId ?? ''),
        fromUnitId: String(values.fromUnitId ?? ''),
        toUnitId: String(values.toUnitId ?? ''),
        rate: Number(values.rate ?? 0),
      }
      await unitApi.saveConversion(payload)
      ElMessage.success('换算关系已保存')
    }
    dialogOpen.value = false
    await loadAll()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    submitting.value = false
  }
}

async function onToggleGroup(row: UnitGroupRow): Promise<void> {
  const disabling = row.status === 1
  try {
    await ElMessageBox.confirm(
      disabling
        ? `停用单位组「${row.name}」后，其下单位在业务单据中不可选，是否继续？`
        : `启用单位组「${row.name}」后恢复可选状态，是否继续？`,
      disabling ? '停用单位组' : '启用单位组',
      { confirmButtonText: '确认', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await unitApi.toggleGroup(row.id)
    await loadAll()
    ElMessage.success(disabling ? '单位组已停用' : '单位组已启用')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

async function onToggleUnit(row: UnitRow): Promise<void> {
  const disabling = row.status === 1
  try {
    await ElMessageBox.confirm(
      disabling
        ? `停用计量单位「${row.name}」后，业务单据中不可选该单位，是否继续？`
        : `启用计量单位「${row.name}」后恢复可选状态，是否继续？`,
      disabling ? '停用计量单位' : '启用计量单位',
      { confirmButtonText: '确认', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await unitApi.toggle(row.id)
    await loadAll()
    ElMessage.success(disabling ? '计量单位已停用' : '计量单位已启用')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

const createText = computed(() => {
  if (activeTab.value === 'groups') {
    return '新增单位组'
  }
  return activeTab.value === 'units' ? '新增计量单位' : '新增换算关系'
})

onMounted(loadAll)
</script>

<template>
  <QlxPagePanel title="计量单位" subtitle="单位组 · 计量单位 · 换算关系（规范 5.2.1）">
    <template #toolbar>
      <el-button v-permission="'bas.unit.action'" type="primary" @click="openCreate">
        <AppIcon name="Plus" :size="14" />
        {{ createText }}
      </el-button>
    </template>

    <el-tabs v-model="activeTab" class="unit-tabs">
      <el-tab-pane label="单位组" name="groups">
        <QlxDataTable
          :items="groups"
          :columns="groupColumns"
          :loading="loading"
          :total="groups.length"
          :page="1"
          :page-size="groups.length || 10"
          :actions-width="140"
          empty-text="暂无单位组"
        >
          <template #actions="{ row }">
            <button
              v-permission="'bas.unit.action'"
              type="button"
              class="cell-link"
              @click="openEditGroup(row)"
            >
              编辑
            </button>
            <el-divider direction="vertical" />
            <button
              v-permission="'bas.unit.action'"
              type="button"
              class="cell-link"
              :class="{ 'is-danger': row.status === 1 }"
              @click="onToggleGroup(row)"
            >
              {{ row.status === 1 ? '停用' : '启用' }}
            </button>
          </template>
        </QlxDataTable>
      </el-tab-pane>

      <el-tab-pane label="计量单位" name="units">
        <QlxDataTable
          :items="units"
          :columns="unitColumns"
          :loading="loading"
          :total="units.length"
          :page="1"
          :page-size="units.length || 10"
          :actions-width="140"
          empty-text="暂无计量单位"
        >
          <template #cell-unitGroupId="{ row }">{{ groupName(row.unitGroupId) }}</template>
          <template #cell-precision="{ row }">
            <span class="qlx-numeric">{{ row.precision }}</span>
          </template>
          <template #actions="{ row }">
            <button
              v-permission="'bas.unit.action'"
              type="button"
              class="cell-link"
              @click="openEditUnit(row)"
            >
              编辑
            </button>
            <el-divider direction="vertical" />
            <button
              v-permission="'bas.unit.action'"
              type="button"
              class="cell-link"
              :class="{ 'is-danger': row.status === 1 }"
              @click="onToggleUnit(row)"
            >
              {{ row.status === 1 ? '停用' : '启用' }}
            </button>
          </template>
        </QlxDataTable>
      </el-tab-pane>

      <el-tab-pane label="换算关系" name="conversions">
        <p class="unit-note">
          换算率以原始数值由后端下发，前端按最多 6 位小数展示；换算关系暂无启停端点，仅支持新增 / 编辑。
        </p>
        <QlxDataTable
          :items="conversions"
          :columns="conversionColumns"
          :loading="loading"
          :total="conversions.length"
          :page="1"
          :page-size="conversions.length || 10"
          :actions-width="100"
          empty-text="暂无换算关系"
        >
          <template #cell-unitGroupId="{ row }">{{ groupName(row.unitGroupId) }}</template>
          <template #cell-fromUnitId="{ row }">{{ unitLabel(row.fromUnitId) }}</template>
          <template #cell-toUnitId="{ row }">{{ unitLabel(row.toUnitId) }}</template>
          <template #cell-rate="{ row }">
            <span class="qlx-numeric">{{ formatRate(row.rate) }}</span>
          </template>
          <template #actions="{ row }">
            <button
              v-permission="'bas.unit.action'"
              type="button"
              class="cell-link"
              @click="openEditConversion(row)"
            >
              编辑
            </button>
          </template>
        </QlxDataTable>
      </el-tab-pane>
    </el-tabs>

    <QlxFormDialog
      v-model="dialogOpen"
      :title="dialogTitle"
      :fields="fields"
      :submitting="submitting"
      submit-text="保存"
      :width="600"
      class="unit-form-dialog"
      @submit="onSubmit"
    />
  </QlxPagePanel>
</template>

<style scoped>
.unit-tabs {
  margin-top: calc(var(--qlx-space-2) * -1);
}

.unit-note {
  margin-bottom: var(--qlx-space-3);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
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

<style>
@media (max-width: 767px) {
  .unit-form-dialog.el-dialog {
    width: min(600px, 92vw);
  }
}
</style>
