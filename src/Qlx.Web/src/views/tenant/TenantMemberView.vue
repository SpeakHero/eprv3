<!--
  所属公司：四川淇利馨集团有限公司
  描述：租户自管理 · 成员管理——MemberTable + MemberInviteDialog + MemberImportDialog + MemberDetailDrawer 组装（方案 §6.2）
  说明：页面只做组装 + 接线 + 状态编排；操作集收敛 / 导入行映射已迁入 member-admin-utils 纯函数；
        重发邀请 / 批量导入 / 登录日志均为真实端点（B-6 / B-7 / B-8，mock 占位已全部移除）；
        危险操作确认框由页面触发，错误消息透传后端 message（方案 §6.4）
-->
<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import QlxSearchBar from '@/components/crud/QlxSearchBar.vue'
import type { SearchField } from '@/components/crud/QlxSearchBar.vue'
import OrgTreePanel from '@/components/org-admin/OrgTreePanel.vue'
import { buildOrgOptions, buildOrgTree, findOrgNode } from '@/components/org-admin/org-admin-utils'
import MemberTable from '@/components/member-admin/MemberTable.vue'
import MemberInviteDialog from '@/components/member-admin/MemberInviteDialog.vue'
import MemberImportDialog from '@/components/member-admin/MemberImportDialog.vue'
import MemberDetailDrawer from '@/components/member-admin/MemberDetailDrawer.vue'
import { useCrudTable } from '@/composables/useCrudTable'
import { createTenantMemberAdminAdapter } from '@/api/adapters/memberAdmin'
import { createTenantOrgAdminAdapter } from '@/api/adapters/orgAdmin'
import { createTenantRoleAdminAdapter } from '@/api/adapters/roleAdmin'
import { memberRoleLabels } from '@/types/admin'
import { entityStatusLabels } from '@/types/tenant'
import type {
  EntityStatus,
  ImportResult,
  ImportRow,
  InviteMemberInput,
  MemberItem,
  MemberLoginLogItem,
  OrgNode,
  RoleItem,
  TenantMemberRole,
} from '@/types/org-admin'
import type { PagedResult } from '@/types/admin'

const memberAdapter = createTenantMemberAdminAdapter()
const orgAdapter = createTenantOrgAdminAdapter()
const roleAdapter = createTenantRoleAdminAdapter()

/** ============ 组织树与角色（筛选与名称解析数据源） ============ */

const flatOrgs = ref<OrgNode[]>([])
const orgLoading = ref(true)
const selectedOrgId = ref('')
const roles = ref<RoleItem[]>([])

const orgTree = computed(() => buildOrgTree(flatOrgs.value))
const orgOptions = computed(() => buildOrgOptions(orgTree.value))
const roleOptions = computed(() => roles.value.map((role) => ({ label: role.name, value: role.code })))

const selectedOrgName = computed(() => {
  if (!selectedOrgId.value) {
    return '全部组织'
  }
  return findOrgNode(orgTree.value, selectedOrgId.value)?.name ?? '全部组织'
})

function resolveOrgName(orgId: string | null): string {
  if (!orgId) {
    return '—'
  }
  return findOrgNode(orgTree.value, orgId)?.name ?? '—'
}

function resolveRoleName(code: string): string {
  return roles.value.find((role) => role.code === code)?.name ?? code
}

function onSelectOrg(id: string): void {
  selectedOrgId.value = id
}

/** ============ 成员列表 ============ */

const searchFields = computed<SearchField[]>(() => [
  { prop: 'keyword', label: '关键词', type: 'input', placeholder: '姓名 / 登录账号 / 手机号 / 邮箱', width: 260 },
  {
    prop: 'memberRole',
    label: '成员角色',
    type: 'select',
    width: 160,
    options: Object.entries(memberRoleLabels).map(([value, label]) => ({ label, value })),
  },
  {
    prop: 'memberStatus',
    label: '成员状态',
    type: 'select',
    width: 140,
    options: Object.entries(entityStatusLabels).map(([value, label]) => ({ label, value })),
  },
])

const { loading, items, total, page, pageSize, query, search, reset, refresh, changePage } =
  useCrudTable<MemberItem>((nextQuery) =>
    memberAdapter.fetchMembers({
      page: nextQuery.page,
      pageSize: nextQuery.pageSize,
      keyword: typeof nextQuery.keyword === 'string' ? nextQuery.keyword : undefined,
      memberRole: (nextQuery.memberRole || undefined) as TenantMemberRole | undefined,
      memberStatus: (nextQuery.memberStatus || undefined) as EntityStatus | undefined,
      orgId: selectedOrgId.value || undefined,
    }),
  )

watch(selectedOrgId, (value, oldValue) => {
  if (value === oldValue) {
    return
  }
  void changePage(1, pageSize.value)
})

onMounted(async () => {
  try {
    const [orgs, roleList] = await Promise.all([orgAdapter.listOrgNodes(), roleAdapter.listRoles()])
    flatOrgs.value = orgs
    roles.value = roleList
    // 默认「全部组织」：无组织归属的成员（如企业管理员）不能被根组织筛选隐藏
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载组织与角色失败')
  } finally {
    orgLoading.value = false
  }
})

/** ============ 邀请成员 ============ */

const inviteOpen = ref(false)
const inviteSubmitting = ref(false)

async function onInviteSubmit(input: InviteMemberInput): Promise<void> {
  inviteSubmitting.value = true
  try {
    const result = await memberAdapter.inviteMember(input)
    inviteOpen.value = false
    if (!result) {
      ElMessage.error('该账号已是本企业成员，请勿重复邀请')
      return
    }
    ElMessage.success(`已向「${input.displayName}」发送邀请，状态为待激活`)
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '邀请成员失败')
  } finally {
    inviteSubmitting.value = false
  }
}

/** ============ 批量导入 ============ */

const importOpen = ref(false)
const importSubmitting = ref(false)
const importResult = ref<ImportResult | null>(null)

watch(importOpen, (open) => {
  if (open) {
    importResult.value = null
  }
})

async function onImportSubmit(rows: ImportRow[]): Promise<void> {
  importSubmitting.value = true
  try {
    importResult.value = await memberAdapter.importMembers(rows)
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '批量导入失败')
  } finally {
    importSubmitting.value = false
  }
}

/** ============ 成员详情抽屉（登录日志 fetcher 注入） ============ */

const detailOpen = ref(false)
const detailLoading = ref(false)
const detail = ref<MemberItem | null>(null)
const detailId = ref('')

function fetchLoginLogs(pageNo: number): Promise<PagedResult<MemberLoginLogItem>> {
  return memberAdapter.fetchLoginLogs(detailId.value, pageNo)
}

async function openDetail(row: MemberItem): Promise<void> {
  detailOpen.value = true
  detailLoading.value = true
  detail.value = null
  detailId.value = row.id
  try {
    detail.value = await memberAdapter.fetchMemberDetail(row.id)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载成员详情失败')
  } finally {
    detailLoading.value = false
  }
}

/** ============ 成员操作（危险操作确认文案沿用旧页面） ============ */

async function onToggleMember(row: MemberItem): Promise<void> {
  const disabling = row.memberStatus === 'Enabled'
  try {
    await ElMessageBox.confirm(
      disabling
        ? `停用「${row.displayName}」后，该成员将无法登录本企业（其它租户不受影响），是否继续？`
        : `启用「${row.displayName}」后恢复本企业访问权限，是否继续？`,
      disabling ? '停用成员' : '启用成员',
      { confirmButtonText: disabling ? '确认停用' : '确认启用', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await memberAdapter.toggleMember(row.id)
    ElMessage.success(disabling ? '成员已停用' : '成员已启用')
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

async function onResetPassword(row: MemberItem): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `将向「${row.displayName}」的手机号或邮箱发送密码重置指引；重置后其历史会话全部失效。`,
      '重置密码',
      { confirmButtonText: '发送指引', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    const newPassword = window.prompt('请输入新密码（≥8位）：', 'Qlx@123456')
    if (!newPassword) {
      return
    }
    await memberAdapter.resetMemberPassword(row.id, newPassword)
    ElMessage.success('密码已重置，请通知成员登录后修改')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '重置密码失败')
  }
}

async function onResendInvite(row: MemberItem): Promise<void> {
  try {
    await memberAdapter.resendInvite(row.id)
    ElMessage.success(`已重发邀请至「${row.loginAccount}」`)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '重发邀请失败')
  }
}

async function onRemoveMember(row: MemberItem): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `移除「${row.displayName}」后，该自然人将失去本企业全部数据访问权限；其门户账号与其它租户关系不受影响。是否继续？`,
      '移除成员',
      { confirmButtonText: '确认移除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await memberAdapter.removeMember(row.id)
    ElMessage.success('成员已移除')
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '移除成员失败')
  }
}
</script>

<template>
  <QlxPagePanel
    title="成员管理"
    subtitle="邀请、批量导入、启停与移除本企业成员；自然人全局身份由门户统一管理（规范 5.2.2）"
  >
    <template #toolbar>
      <el-button v-permission="'bas.admin.member.action'" type="primary" @click="inviteOpen = true">
        <AppIcon name="UserFilled" :size="14" />
        邀请成员
      </el-button>
      <el-button v-permission="'bas.admin.member.action'" @click="importOpen = true">
        <AppIcon name="Document" :size="14" />
        批量导入
      </el-button>
    </template>

    <div class="member-layout">
      <OrgTreePanel
        title="组织筛选"
        :nodes="orgTree"
        :loading="orgLoading"
        :selected-key="selectedOrgId"
        empty-text="暂无组织"
        class="member-tree"
        @select="onSelectOrg"
      >
        <template #footer>
          <div class="member-tree__footer">
            <p class="member-tree__hint">
              筛选范围：{{ selectedOrgId ? `${selectedOrgName}（含下级组织）` : '全部组织' }}
            </p>
            <el-button v-if="selectedOrgId" link type="primary" size="small" @click="selectedOrgId = ''">
              查看全部组织
            </el-button>
          </div>
        </template>
      </OrgTreePanel>

      <section class="member-main">
        <QlxSearchBar :fields="searchFields" :model-value="query" @search="search" @reset="reset" />

        <MemberTable
          :items="items"
          :loading="loading"
          :total="total"
          :page="page"
          :page-size="pageSize"
          :resolve-org-name="resolveOrgName"
          :resolve-role-name="resolveRoleName"
          @detail="openDetail"
          @toggle="onToggleMember"
          @reset="onResetPassword"
          @remove="onRemoveMember"
          @resend="onResendInvite"
          @page-change="changePage"
        />
      </section>
    </div>

    <MemberInviteDialog
      v-model="inviteOpen"
      :org-options="orgOptions"
      :role-options="roleOptions"
      :default-org-id="selectedOrgId"
      :submitting="inviteSubmitting"
      @submit="onInviteSubmit"
    />

    <MemberImportDialog
      v-model="importOpen"
      :submitting="importSubmitting"
      :result="importResult"
      @submit="onImportSubmit"
    />

    <MemberDetailDrawer
      v-model="detailOpen"
      :member="detail"
      :loading="detailLoading"
      :fetch-logs="fetchLoginLogs"
      :resolve-org-name="resolveOrgName"
      :resolve-role-name="resolveRoleName"
    />
  </QlxPagePanel>
</template>

<style scoped>
/* 左树右表（方案 §9：桌面 264~288px） */
.member-layout {
  display: grid;
  gap: var(--qlx-space-4);
  grid-template-columns: 264px minmax(0, 1fr);
  align-items: start;
}

.member-tree {
  min-height: 460px;
}

.member-tree__hint {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  text-align: center;
}

.member-tree__footer {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
}

.member-main {
  min-width: 0;
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
}

.member-main :deep(.qlx-search) {
  border-top-left-radius: var(--qlx-radius-md);
  border-top-right-radius: var(--qlx-radius-md);
}

.member-main :deep(.qlx-table) {
  padding: 0 var(--qlx-space-4) var(--qlx-space-4);
}

/* 平板横屏（768~1199px）：树栏收窄 220px */
@media (max-width: 1199px) {
  .member-layout {
    grid-template-columns: 220px minmax(0, 1fr);
  }
}

/* 平板竖屏 / 手机（<768px）：单栏堆叠 */
@media (max-width: 767px) {
  .member-layout {
    grid-template-columns: 1fr;
  }
}
</style>
