<!--
  所属公司：四川淇利馨集团有限公司
  描述：租户自管理 · 组织机构（Bas 主数据版）——OrgTreePanel + OrgDetailCard + OrgFormDialog + PositionTable 组装
  说明：
    1. 页面只做组装 + 接线 + 状态编排；建树 / 上级排除 / 层级缩进 / 四级类型映射在 org-admin-utils 纯函数（B-4）；
    2. 负责人改为人员主数据选择（GET /api/bas/persons），值 leaderPersonId；
    3. 版本历史抽屉 / 调整审批状态复用 components/approval：审批状态由「我发起的」审批列表一次拉取后按 bizId 归并，刷新页面仍可见；
    4. <768px 组织树折叠为「下拉选择 + 展开面板」（方案 §9）。
-->
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import QlxSearchBar from '@/components/crud/QlxSearchBar.vue'
import type { SearchField } from '@/components/crud/QlxSearchBar.vue'
import OrgTreePanel from '@/components/org-admin/OrgTreePanel.vue'
import OrgDetailCard from '@/components/org-admin/OrgDetailCard.vue'
import OrgFormDialog from '@/components/org-admin/OrgFormDialog.vue'
import OrgHistoryDrawer from '@/components/org-admin/OrgHistoryDrawer.vue'
import PositionTable from '@/components/org-admin/PositionTable.vue'
import ApprovalTimeline from '@/components/approval/ApprovalTimeline.vue'
import { APPROVAL_STATUS_LABELS } from '@/components/approval/types'
import type { ApprovalInstanceInfo } from '@/components/approval/types'
import { buildOrgOptions, buildOrgTree, findOrgNode } from '@/components/org-admin/org-admin-utils'
import type { BasOrgTreeNode } from '@/components/org-admin/org-admin-utils'
import { useCrudTable } from '@/composables/useCrudTable'
import { orgMasterApi, personApi, positionApi } from '@/api/basMaster'
import { fetchApprovalInitiated, fetchApprovalInstance } from '@/api/support'
import { convertInstance } from '@/api/support-approval-adapter'
import type {
  BasOrgRow,
  BasPositionRow,
  OrgHistoryRow,
  PersonOption,
  SaveBasOrgPayload,
  SaveBasPositionPayload,
} from '@/types/bas-master'
import { approvalRequestStatusLabel, mapOrgAdjustApprovals } from '@/utils/bas-master-mappers'
import type { ApprovalInstanceLite } from '@/utils/bas-master-mappers'

const activeTab = ref<'org' | 'positions'>('org')

/** ============ 组织架构 ============ */

const flatOrgs = ref<BasOrgRow[]>([])
const orgLoading = ref(true)
const selectedOrgId = ref('')
const leaderOptions = ref<PersonOption[]>([])

const orgTree = computed<BasOrgTreeNode[]>(() => buildOrgTree(flatOrgs.value))
const orgOptions = computed(() => buildOrgOptions(orgTree.value))

const selectedOrg = computed<BasOrgTreeNode | null>(() =>
  selectedOrgId.value ? findOrgNode(orgTree.value, selectedOrgId.value) : null,
)

const parentOrgName = computed<string>(() => {
  const org = selectedOrg.value
  if (!org) {
    return '—'
  }
  if (!org.parentId) {
    return '无（集团根节点）'
  }
  return findOrgNode(orgTree.value, org.parentId)?.name ?? '—'
})

/** 负责人显示名（按人员候选清单解析 leaderPersonId）。 */
const leaderName = computed<string>(() => {
  const id = selectedOrg.value?.leaderPersonId
  if (!id) {
    return ''
  }
  return leaderOptions.value.find((person) => person.id === id)?.name ?? ''
})

function onSelectOrg(id: string): void {
  selectedOrgId.value = id
}

async function loadOrgTree(selectId?: string): Promise<void> {
  orgLoading.value = true
  try {
    flatOrgs.value = await orgMasterApi.list()
    const target = selectId ?? selectedOrgId.value
    selectedOrgId.value = findOrgNode(orgTree.value, target)?.id ?? (orgTree.value[0]?.id ?? '')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载组织架构失败')
  } finally {
    orgLoading.value = false
  }
}

/** ============ 窄屏（<768px）组织树折叠：下拉选择 + 展开面板 ============ */

const isNarrow = ref(false)
const narrowTreeOpen = ref(false)
const narrowMedia = window.matchMedia('(max-width: 767px)')

function onNarrowChange(event: MediaQueryListEvent): void {
  isNarrow.value = event.matches
}

/** ============ 组织新建 / 编辑 / 启停 ============ */

const orgDialogOpen = ref(false)
const orgSubmitting = ref(false)
const editingOrg = ref<BasOrgTreeNode | null>(null)
const defaultParent = ref<BasOrgTreeNode | null>(null)

function openCreateOrg(): void {
  editingOrg.value = null
  defaultParent.value = selectedOrg.value
  orgDialogOpen.value = true
}

function openEditOrg(): void {
  if (!selectedOrg.value) {
    return
  }
  defaultParent.value = null
  editingOrg.value = selectedOrg.value
  orgDialogOpen.value = true
}

async function onOrgSubmit(input: SaveBasOrgPayload): Promise<void> {
  const editing = editingOrg.value
  orgSubmitting.value = true
  try {
    const saved = editing
      ? await orgMasterApi.update(editing.id, input)
      : await orgMasterApi.create(input)
    orgDialogOpen.value = false
    await loadOrgTree(saved.id)
    ElMessage.success(editing ? '组织信息已保存' : '组织已创建')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存组织失败')
  } finally {
    orgSubmitting.value = false
  }
}

async function onToggleOrg(): Promise<void> {
  const org = selectedOrg.value
  if (!org) {
    return
  }
  const disabling = org.status === 1
  try {
    await ElMessageBox.confirm(
      disabling
        ? `停用「${org.name}」后，其下级组织保留但成员在业务单据中将无法选择该组织，是否继续？`
        : `启用「${org.name}」后恢复在业务单据中的可选状态，是否继续？`,
      disabling ? '停用组织' : '启用组织',
      { confirmButtonText: disabling ? '确认停用' : '确认启用', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await orgMasterApi.toggle(org.id)
    await loadOrgTree(org.id)
    ElMessage.success(disabling ? '组织已停用' : '组织已启用')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

/** ============ 组织版本历史 ============ */

const historyOpen = ref(false)
const historyLoading = ref(false)
const historyItems = ref<OrgHistoryRow[]>([])
const historyTitle = ref('组织版本历史')

async function openHistory(): Promise<void> {
  const org = selectedOrg.value
  if (!org) {
    return
  }
  historyTitle.value = `组织版本历史 · ${org.name}`
  historyOpen.value = true
  historyLoading.value = true
  try {
    historyItems.value = await orgMasterApi.history(org.id)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载版本历史失败')
  } finally {
    historyLoading.value = false
  }
}

/** ============ 组织调整审批（复用 components/approval）============ */

/** 组织调整审批状态：一次拉取「我发起的」审批列表并按 bizId 归并（禁 N+1，刷新后仍可见）。 */
const orgApprovals = ref<Map<string, ApprovalInstanceLite>>(new Map())
const approvalDrawerOpen = ref(false)
const approvalLoading = ref(false)
const approvalInstance = ref<ApprovalInstanceInfo | null>(null)

const currentApprovalState = computed<ApprovalInstanceLite | null>(() =>
  selectedOrgId.value ? (orgApprovals.value.get(selectedOrgId.value) ?? null) : null,
)

/** 拉取审批状态；接口不可用 / 当前用户无发起记录时静默置空，不展示状态也不阻断页面。 */
async function loadOrgApprovals(): Promise<void> {
  try {
    orgApprovals.value = mapOrgAdjustApprovals(await fetchApprovalInitiated())
  } catch {
    orgApprovals.value = new Map()
  }
}

async function onSubmitAdjust(): Promise<void> {
  const org = selectedOrg.value
  if (!org) {
    return
  }
  let reason = ''
  try {
    const result = await ElMessageBox.prompt(
      `将按当前信息提交「${org.name}」的组织调整审批，审批通过后生效。请填写调整原因：`,
      '提交调整审批',
      {
        confirmButtonText: '提交申请',
        cancelButtonText: '取消',
        inputPlaceholder: '如：组织架构调整',
        inputValidator: (value) => (value?.trim() ? true : '请填写调整原因'),
      },
    )
    reason = String(result.value ?? '').trim()
  } catch {
    return
  }
  try {
    const request = await orgMasterApi.submitAdjust({
      action: 'update',
      orgId: org.id,
      name: org.name,
      parentId: org.parentId,
      type: org.type,
      leaderPersonId: org.leaderPersonId,
      sortOrder: org.sortOrder,
      reason,
    })
    ElMessage.success(`调整申请已提交：${approvalRequestStatusLabel(request.status)}`)
    // 重新拉取列表；列表尚未含该实例时按返回的审批实例 Id 定点补齐（单次请求，非 N+1）
    await loadOrgApprovals()
    if (request.approvalInstanceId && !orgApprovals.value.has(org.id)) {
      try {
        const instance = await fetchApprovalInstance(request.approvalInstanceId)
        const next = new Map(orgApprovals.value)
        next.set(org.id, instance)
        orgApprovals.value = next
      } catch {
        /* 审批实例暂不可取时不展示状态 */
      }
    }
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '提交调整申请失败')
  }
}

async function openApprovalProgress(): Promise<void> {
  const state = currentApprovalState.value
  if (!state?.instanceId) {
    return
  }
  approvalDrawerOpen.value = true
  approvalLoading.value = true
  try {
    approvalInstance.value = convertInstance(await fetchApprovalInstance(state.instanceId))
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载审批进度失败')
  } finally {
    approvalLoading.value = false
  }
}

const approvalStatusLabel = computed(() =>
  approvalInstance.value ? APPROVAL_STATUS_LABELS[approvalInstance.value.status] : '',
)

/** ============ 岗位管理 ============ */

const positionSearchFields: SearchField[] = [
  { prop: 'keyword', label: '关键词', type: 'input', placeholder: '岗位名称 / 编码 / 职责', width: 240 },
]

const {
  loading: positionLoading,
  items: positionItems,
  total: positionTotal,
  page: positionPage,
  pageSize: positionPageSize,
  query: positionQuery,
  search: searchPositions,
  reset: resetPositions,
  refresh: refreshPositions,
  changePage: changePositionPage,
} = useCrudTable<BasPositionRow>((query) =>
  positionApi.list({
    page: query.page,
    pageSize: query.pageSize,
    keyword: typeof query.keyword === 'string' && query.keyword ? query.keyword : undefined,
    orgId: selectedOrgId.value || undefined,
  }),
)

const positionTableRef = ref<{ openCreate: () => void; closeDialog: () => void }>()
const positionSubmitting = ref(false)

function openCreatePosition(): void {
  positionTableRef.value?.openCreate()
}

async function onPositionSave(input: SaveBasPositionPayload): Promise<void> {
  positionSubmitting.value = true
  try {
    await positionApi.save(input)
    positionTableRef.value?.closeDialog()
    await refreshPositions()
    ElMessage.success(input.id ? '岗位信息已保存' : '岗位已创建')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存岗位失败')
  } finally {
    positionSubmitting.value = false
  }
}

async function onPositionToggle(row: BasPositionRow): Promise<void> {
  const disabling = row.status === 1
  try {
    await ElMessageBox.confirm(
      disabling
        ? `停用岗位「${row.name}」后，业务单据将不再可选该岗位（在职成员不受影响），是否继续？`
        : `启用岗位「${row.name}」后恢复在业务单据中的可选状态，是否继续？`,
      disabling ? '停用岗位' : '启用岗位',
      { confirmButtonText: disabling ? '确认停用' : '确认启用', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await positionApi.toggle(row.id)
    await refreshPositions()
    ElMessage.success(disabling ? '岗位已停用' : '岗位已启用')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

async function onRequestOverstaff(row: BasPositionRow): Promise<void> {
  try {
    const result = await ElMessageBox.prompt(
      `岗位「${row.name}」当前编制 ${row.headcount} 人、在职 ${row.currentCount} 人。请填写申请的编制人数：`,
      '提交超编审批',
      {
        confirmButtonText: '提交申请',
        cancelButtonText: '取消',
        inputValue: String(row.headcount + 1),
        inputValidator: (value) =>
          Number(value) > row.headcount ? true : `申请编制需大于当前编制 ${row.headcount} 人`,
      },
    )
    const request = await positionApi.submitOverstaff(row.id, {
      headcount: Number(result.value),
      reason: null,
    })
    ElMessage.success(`超编申请已提交：${approvalRequestStatusLabel(request.status)}`)
  } catch (error) {
    if (error instanceof Error) {
      ElMessage.error(error.message)
    }
  }
}

watch(selectedOrgId, (value, oldValue) => {
  if (!value || value === oldValue) {
    return
  }
  if (activeTab.value === 'positions') {
    void changePositionPage(1, positionPageSize.value)
  }
})

// 切到岗位 Tab 时按当前组织范围重查（组织树选择变化不重复请求）
watch(activeTab, (tab) => {
  if (tab === 'positions') {
    void changePositionPage(1, positionPageSize.value)
  }
})

onMounted(async () => {
  isNarrow.value = narrowMedia.matches
  narrowMedia.addEventListener('change', onNarrowChange)
  try {
    const result = await personApi.list({ page: 1, pageSize: 200 })
    leaderOptions.value = result.items.map((person) => ({
      id: person.id,
      name: person.name,
      code: person.code,
    }))
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载负责人候选失败')
  }
  await loadOrgApprovals()
  await loadOrgTree()
})

onBeforeUnmount(() => {
  narrowMedia.removeEventListener('change', onNarrowChange)
})
</script>

<template>
  <QlxPagePanel
    title="组织机构"
    subtitle="集团 · 子公司 · 部门 · 项目部四级组织架构与岗位编制（规范 5.2.1）"
  >
    <template #toolbar>
      <el-button
        v-if="activeTab === 'org'"
        v-permission="'bas.admin.org.action'"
        type="primary"
        @click="openCreateOrg"
      >
        <AppIcon name="OfficeBuilding" :size="14" />
        新增下级组织
      </el-button>
      <el-button
        v-else
        v-permission="'bas.admin.org.action'"
        type="primary"
        @click="openCreatePosition"
      >
        <AppIcon name="UserFilled" :size="14" />
        新增岗位
      </el-button>
    </template>

    <el-tabs v-model="activeTab" class="org-tabs">
      <!-- ============ 组织架构 ============ -->
      <el-tab-pane label="组织架构" name="org">
        <div class="org-layout">
          <div class="org-layout__side">
            <!-- 窄屏：下拉选择 + 展开面板（方案 §9 <768px） -->
            <div v-if="isNarrow" class="org-narrow">
              <el-select v-model="selectedOrgId" filterable class="org-narrow__select">
                <el-option
                  v-for="option in orgOptions"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
              <el-button size="small" text type="primary" @click="narrowTreeOpen = !narrowTreeOpen">
                {{ narrowTreeOpen ? '收起组织树' : '展开组织树' }}
              </el-button>
            </div>

            <OrgTreePanel
              v-show="!isNarrow || narrowTreeOpen"
              :nodes="orgTree"
              :loading="orgLoading"
              :selected-key="selectedOrgId"
              @select="onSelectOrg"
            >
              <template #footer>
                <el-button
                  v-permission="'bas.admin.org.action'"
                  size="small"
                  @click="openCreateOrg"
                >
                  <AppIcon name="OfficeBuilding" :size="13" />
                  新增下级组织
                </el-button>
              </template>
            </OrgTreePanel>
          </div>

          <OrgDetailCard
            :node="selectedOrg"
            :parent-name="parentOrgName"
            :leader-name="leaderName"
            :approval-state="currentApprovalState"
            @edit="openEditOrg"
            @toggle="onToggleOrg"
            @history="openHistory"
            @select-child="onSelectOrg"
            @submit-disable-approval="onSubmitAdjust"
            @view-approval="openApprovalProgress"
          />
        </div>
      </el-tab-pane>

      <!-- ============ 岗位管理 ============ -->
      <el-tab-pane label="岗位管理" name="positions">
        <p class="position-note">
          岗位与组织关联，用于人员档案与业务单据的岗位引用；当前筛选范围为
          <strong>{{ selectedOrg?.name ?? '全部组织' }}</strong>（含下级组织）。
        </p>
        <QlxSearchBar
          :fields="positionSearchFields"
          :model-value="positionQuery"
          @search="searchPositions"
          @reset="resetPositions"
        />
        <PositionTable
          ref="positionTableRef"
          :items="positionItems"
          :loading="positionLoading"
          :total="positionTotal"
          :page="positionPage"
          :page-size="positionPageSize"
          :org-options="orgOptions"
          :default-org-id="selectedOrgId"
          :submitting="positionSubmitting"
          @save="onPositionSave"
          @toggle="onPositionToggle"
          @request-overstaff="onRequestOverstaff"
          @page-change="changePositionPage"
        />
      </el-tab-pane>
    </el-tabs>

    <OrgFormDialog
      v-model="orgDialogOpen"
      :editing="editingOrg"
      :default-parent="defaultParent"
      :nodes="orgTree"
      :leader-options="leaderOptions"
      :submitting="orgSubmitting"
      @submit="onOrgSubmit"
    />

    <OrgHistoryDrawer
      v-model="historyOpen"
      :items="historyItems"
      :loading="historyLoading"
      :title="historyTitle"
    />

    <el-drawer v-model="approvalDrawerOpen" title="组织调整审批进度" size="520px" append-to-body>
      <div v-loading="approvalLoading" class="approval-drawer">
        <div v-if="approvalInstance" class="approval-drawer__head">
          <span>{{ approvalInstance.title }}</span>
          <el-tag size="small" effect="light">{{ approvalStatusLabel }}</el-tag>
        </div>
        <ApprovalTimeline :instance="approvalInstance" />
      </div>
    </el-drawer>
  </QlxPagePanel>
</template>

<style scoped>
.org-tabs {
  margin-top: calc(var(--qlx-space-2) * -1);
}

/* 左树右详情（方案 §9：桌面 264~288px） */
.org-layout {
  display: grid;
  gap: var(--qlx-space-4);
  grid-template-columns: 288px minmax(0, 1fr);
  align-items: start;
}

.org-layout__side {
  min-width: 0;
}

.org-narrow {
  display: flex;
  margin-bottom: var(--qlx-space-3);
  align-items: center;
  gap: var(--qlx-space-2);
}

.org-narrow__select {
  flex: 1;
}

.position-note {
  margin-bottom: var(--qlx-space-3);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.position-note strong {
  color: var(--qlx-brand-700);
  font-weight: 500;
}

.approval-drawer {
  min-height: 200px;
}

.approval-drawer__head {
  display: flex;
  margin-bottom: var(--qlx-space-3);
  align-items: center;
  justify-content: space-between;
  gap: var(--qlx-space-2);
  color: var(--qlx-ink-800);
  font-size: var(--qlx-text-sm);
}

/* 平板横屏（768~1199px）：树栏收窄 220px */
@media (max-width: 1199px) {
  .org-layout {
    grid-template-columns: 220px minmax(0, 1fr);
  }
}

/* 平板竖屏 / 手机（<768px）：单栏堆叠（树折叠为下拉 + 展开面板） */
@media (max-width: 767px) {
  .org-layout {
    grid-template-columns: 1fr;
  }
}
</style>
