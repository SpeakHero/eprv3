<!--
  所属公司：四川淇利馨集团有限公司
  描述：租户自管理 · 组织机构——OrgTreePanel + OrgDetailCard + OrgFormDialog + PositionTable 组装（方案 §6.2）
  说明：页面只做组装 + 接线 + 状态编排；建树 / 上级排除 / 层级缩进已迁入 org-admin-utils 纯函数；
        组织树节点显示 memberCount（B-4）；<768px 组织树折叠为「下拉选择 + 展开面板」（方案 §9）
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
import PositionTable from '@/components/org-admin/PositionTable.vue'
import { buildOrgOptions, buildOrgTree, findOrgNode } from '@/components/org-admin/org-admin-utils'
import type { OrgTreeNode } from '@/components/org-admin/org-admin-utils'
import { useCrudTable } from '@/composables/useCrudTable'
import { createTenantOrgAdminAdapter } from '@/api/adapters/orgAdmin'
import { createTenantRoleAdminAdapter } from '@/api/adapters/roleAdmin'
import type {
  MemberOption,
  OrgNode,
  PositionItem,
  SaveOrgInput,
  SavePositionInput,
} from '@/types/org-admin'

const orgAdapter = createTenantOrgAdminAdapter()
// 负责人候选复用角色域「成员候选」契约（types/org-admin.ts：负责人/角色成员分配等场景共用）
const memberOptionsAdapter = createTenantRoleAdminAdapter()

const activeTab = ref<'org' | 'positions'>('org')

/** ============ 组织架构 ============ */

const flatOrgs = ref<OrgNode[]>([])
const orgLoading = ref(true)
const selectedOrgId = ref('')
const managerOptions = ref<MemberOption[]>([])

const orgTree = computed<OrgTreeNode[]>(() => buildOrgTree(flatOrgs.value))
const orgOptions = computed(() => buildOrgOptions(orgTree.value))

const selectedOrg = computed<OrgTreeNode | null>(() =>
  selectedOrgId.value ? findOrgNode(orgTree.value, selectedOrgId.value) : null,
)

const parentOrgName = computed<string>(() => {
  const org = selectedOrg.value
  if (!org) {
    return '—'
  }
  if (!org.parentId) {
    return '无（公司根节点）'
  }
  return findOrgNode(orgTree.value, org.parentId)?.name ?? '—'
})

/** 负责人显示名（按候选清单解析 managerMemberId）。 */
const managerName = computed<string>(() => {
  const id = selectedOrg.value?.managerMemberId
  if (!id) {
    return ''
  }
  return managerOptions.value.find((member) => member.id === id)?.name ?? ''
})

function onSelectOrg(id: string): void {
  selectedOrgId.value = id
}

async function loadOrgTree(selectId?: string): Promise<void> {
  orgLoading.value = true
  try {
    flatOrgs.value = await orgAdapter.listOrgNodes()
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
const editingOrg = ref<OrgTreeNode | null>(null)
const defaultParent = ref<OrgTreeNode | null>(null)

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

async function onOrgSubmit(input: SaveOrgInput): Promise<void> {
  const editing = editingOrg.value
  orgSubmitting.value = true
  try {
    const saved = await orgAdapter.saveOrg(editing?.id ?? null, input)
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
  const disabling = org.status === 'Enabled'
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
    await orgAdapter.toggleOrg(org.id)
    await loadOrgTree(org.id)
    ElMessage.success(disabling ? '组织已停用' : '组织已启用')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

/** ============ 岗位管理 ============ */

const positionSearchFields: SearchField[] = [
  { prop: 'keyword', label: '关键词', type: 'input', placeholder: '岗位名称 / 编码 / 说明', width: 240 },
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
} = useCrudTable<PositionItem>((query) =>
  orgAdapter.fetchPositions({
    page: query.page,
    pageSize: query.pageSize,
    keyword: typeof query.keyword === 'string' ? query.keyword : undefined,
    orgId: selectedOrgId.value || undefined,
  }),
)

const positionTableRef = ref<{ openCreate: () => void; closeDialog: () => void }>()
const positionSubmitting = ref(false)

function openCreatePosition(): void {
  positionTableRef.value?.openCreate()
}

async function onPositionSave(input: SavePositionInput): Promise<void> {
  positionSubmitting.value = true
  try {
    await orgAdapter.savePosition(input)
    positionTableRef.value?.closeDialog()
    await refreshPositions()
    ElMessage.success(input.id ? '岗位信息已保存' : '岗位已创建')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存岗位失败')
  } finally {
    positionSubmitting.value = false
  }
}

async function onPositionToggle(row: PositionItem): Promise<void> {
  const disabling = row.status === 'Enabled'
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
    await orgAdapter.togglePosition(row.id)
    await refreshPositions()
    ElMessage.success(disabling ? '岗位已停用' : '岗位已启用')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

watch(selectedOrgId, (value, oldValue) => {
  if (!value || value === oldValue) {
    return
  }
  void changePositionPage(1, positionPageSize.value)
})

onMounted(async () => {
  isNarrow.value = narrowMedia.matches
  narrowMedia.addEventListener('change', onNarrowChange)
  try {
    managerOptions.value = await memberOptionsAdapter.fetchMemberOptions()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载负责人候选失败')
  }
  await loadOrgTree()
})

onBeforeUnmount(() => {
  narrowMedia.removeEventListener('change', onNarrowChange)
})
</script>

<template>
  <QlxPagePanel
    title="组织机构"
    subtitle="公司 · 部门 · 团队三级组织架构与岗位编制（规范 5.2.1）"
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
            :manager-name="managerName"
            @edit="openEditOrg"
            @toggle="onToggleOrg"
            @select-child="onSelectOrg"
          />
        </div>
      </el-tab-pane>

      <!-- ============ 岗位管理 ============ -->
      <el-tab-pane label="岗位管理" name="positions">
        <p class="position-note">
          岗位与部门关联，用于人员档案与业务单据的岗位引用；当前筛选范围为
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
          @page-change="changePositionPage"
        />
      </el-tab-pane>
    </el-tabs>

    <OrgFormDialog
      v-model="orgDialogOpen"
      :editing="editingOrg"
      :default-parent="defaultParent"
      :nodes="orgTree"
      :manager-options="managerOptions"
      :submitting="orgSubmitting"
      @submit="onOrgSubmit"
    />
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
