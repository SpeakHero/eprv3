<!--
  所属公司：四川淇利馨集团有限公司
  描述：门户管理 · 门户角色——RoleListPanel + 基本信息 / 功能权限 / 角色成员三 Tab 单页（方案 §6.1 / D4）
  说明：与租户角色页共用 role-admin 组件族（双端同构）；场景差异全部由 caps.isPortal 表达
        （编码规则 portal. 前缀、内置全锁、成员模型 portal_users、权限选项按角色路由加载）；
        旧独立路由 /portal/roles/:id/permissions|members 已改为重定向到本页
-->
<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import RoleListPanel from '@/components/role-admin/RoleListPanel.vue'
import RoleBasicPanel from '@/components/role-admin/RoleBasicPanel.vue'
import RoleFormDialog from '@/components/role-admin/RoleFormDialog.vue'
import RolePermissionPanel from '@/components/role-admin/RolePermissionPanel.vue'
import RoleMemberPanel from '@/components/role-admin/RoleMemberPanel.vue'
import { createPortalRoleAdminAdapter } from '@/api/adapters/roleAdmin'
import type { MemberOption, PermissionGroup, RoleItem, SaveRoleInput } from '@/types/org-admin'

const adapter = createPortalRoleAdminAdapter()
const caps = adapter.caps

const activeTab = ref<'basic' | 'permissions' | 'members'>('basic')

/** ============ 角色列表 ============ */

const roles = ref<RoleItem[]>([])
const rolesLoading = ref(true)
const selectedRoleId = ref('')

const selectedRole = computed<RoleItem | null>(
  () => roles.value.find((role) => role.id === selectedRoleId.value) ?? null,
)

const subtitle = computed(() => {
  const role = selectedRole.value
  return role ? `${role.permissionCount} 项功能权限 · ${role.memberCount} 名成员` : ''
})

async function loadRoles(keepId?: string): Promise<void> {
  rolesLoading.value = true
  try {
    const list = await adapter.listRoles()
    roles.value = list
    const target = keepId ?? selectedRoleId.value
    selectedRoleId.value = list.some((role) => role.id === target) ? target : (list[0]?.id ?? '')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载角色失败')
  } finally {
    rolesLoading.value = false
  }
}

function onSelectRole(id: string): void {
  selectedRoleId.value = id
}

/** ============ 权限 / 成员回显（门户场景权限选项与成员候选挂在角色路由下，按选中角色加载） ============ */

const permissionGroups = ref<PermissionGroup[]>([])
const memberOptions = ref<MemberOption[]>([])
const checkedPermissions = ref<string[]>([])
const selectedMemberIds = ref<string[]>([])
const extrasLoading = ref(false)

async function loadRoleExtras(id: string): Promise<void> {
  extrasLoading.value = true
  try {
    const [codes, memberIds, groups, options] = await Promise.all([
      adapter.fetchRolePermissions(id),
      adapter.fetchRoleMemberIds(id),
      adapter.fetchPermissionGroups(id),
      adapter.fetchMemberOptions(id),
    ])
    checkedPermissions.value = codes
    selectedMemberIds.value = memberIds
    permissionGroups.value = groups
    memberOptions.value = options
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载角色详情失败')
  } finally {
    extrasLoading.value = false
  }
}

watch(selectedRoleId, (id, oldId) => {
  if (id && id !== oldId) {
    void loadRoleExtras(id)
  }
  if (!id) {
    checkedPermissions.value = []
    selectedMemberIds.value = []
    permissionGroups.value = []
    memberOptions.value = []
  }
})

onMounted(() => loadRoles())

/** ============ 新建 / 编辑 / 删除 ============ */

const roleDialogOpen = ref(false)
const roleSubmitting = ref(false)
const editingRole = ref<RoleItem | null>(null)

function openCreateRole(): void {
  editingRole.value = null
  roleDialogOpen.value = true
}

function openEditRole(): void {
  if (!selectedRole.value) {
    return
  }
  editingRole.value = selectedRole.value
  roleDialogOpen.value = true
}

async function onRoleSubmit(input: SaveRoleInput): Promise<void> {
  const editing = editingRole.value
  roleSubmitting.value = true
  try {
    const saved = await adapter.saveRole(editing?.id ?? null, input)
    roleDialogOpen.value = false
    await loadRoles(saved.id)
    ElMessage.success(editing ? '角色更新成功' : '角色创建成功，请继续分配功能权限与成员')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    roleSubmitting.value = false
  }
}

async function onDeleteRole(): Promise<void> {
  const role = selectedRole.value
  if (!role) {
    return
  }
  try {
    await ElMessageBox.confirm(
      `确认删除角色“${role.name}”？若已有成员请先移除。`,
      '警告',
      { confirmButtonText: '确认删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await adapter.deleteRole(role.id)
    ElMessage.success('角色已删除')
    await loadRoles()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '删除失败')
  }
}

/** ============ 功能权限 / 角色成员保存（门户内置角色全锁：权限与成员均不可改） ============ */

const permissionSaving = ref(false)
const membersSaving = ref(false)

const builtinLocked = computed(() => selectedRole.value?.isBuiltin ?? false)

async function onSavePermissions(codes: string[]): Promise<void> {
  const role = selectedRole.value
  if (!role || builtinLocked.value) {
    return
  }
  permissionSaving.value = true
  try {
    await adapter.saveRolePermissions(role.id, codes)
    checkedPermissions.value = codes
    ElMessage.success(`已保存「${role.name}」的功能权限（${codes.length} 项）`)
    await loadRoles(role.id)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存功能权限失败')
  } finally {
    permissionSaving.value = false
  }
}

async function onSaveMembers(ids: string[]): Promise<void> {
  const role = selectedRole.value
  if (!role) {
    return
  }
  if (builtinLocked.value) {
    ElMessage.warning('系统内置角色全锁定，不可分配成员')
    return
  }
  membersSaving.value = true
  try {
    await adapter.saveRoleMembers(role.id, ids)
    selectedMemberIds.value = ids
    ElMessage.success(`已更新「${role.name}」的角色成员（${ids.length} 人）`)
    await loadRoles(role.id)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存角色成员失败')
  } finally {
    membersSaving.value = false
  }
}
</script>

<template>
  <div class="role-page">
    <RoleListPanel
      :roles="roles"
      :loading="rolesLoading"
      :selected-id="selectedRoleId"
      :caps="caps"
      @select="onSelectRole"
      @create="openCreateRole"
    />

    <section class="role-main">
      <QlxPagePanel v-if="selectedRole" :title="selectedRole.name" :subtitle="subtitle">
        <el-tabs v-model="activeTab">
          <el-tab-pane label="基本信息" name="basic">
            <RoleBasicPanel
              :role="selectedRole"
              :caps="caps"
              @edit="openEditRole"
              @delete="onDeleteRole"
            />
          </el-tab-pane>

          <el-tab-pane :label="`功能权限（${selectedRole.permissionCount}）`" name="permissions">
            <RolePermissionPanel
              :groups="permissionGroups"
              :checked="checkedPermissions"
              :readonly="builtinLocked"
              readonly-reason="系统内置角色全锁定（不可编辑 / 删除 / 分配）；如需差异化授权请新建自定义角色。"
              :saving="permissionSaving"
              :loading="extrasLoading"
              @save="onSavePermissions"
            />
          </el-tab-pane>

          <el-tab-pane :label="`角色成员（${selectedRole.memberCount}）`" name="members">
            <RoleMemberPanel
              :options="memberOptions"
              :selected-ids="selectedMemberIds"
              :saving="membersSaving"
              :loading="extrasLoading"
              :role-name="selectedRole.name"
              @save="onSaveMembers"
            />
          </el-tab-pane>
        </el-tabs>
      </QlxPagePanel>

      <QlxPagePanel v-else title="门户角色管理" subtitle="请选择或新建角色">
        <el-empty description="暂无角色" />
      </QlxPagePanel>
    </section>

    <RoleFormDialog
      v-model="roleDialogOpen"
      :editing="editingRole"
      :caps="caps"
      :submitting="roleSubmitting"
      @submit="onRoleSubmit"
    />
  </div>
</template>

<style scoped>
/* 左列表 + 右工作区（方案 §9 三档断点，与租户角色页骨架一致） */
.role-page {
  display: grid;
  gap: var(--qlx-space-4);
  grid-template-columns: 280px minmax(0, 1fr);
  align-items: start;
}

.role-main {
  min-width: 0;
}

/* 平板横屏（768~1199px）：列表收窄 220px */
@media (max-width: 1199px) {
  .role-page {
    grid-template-columns: 220px minmax(0, 1fr);
  }
}

/* 平板竖屏 / 手机（<768px）：单栏堆叠 */
@media (max-width: 767px) {
  .role-page {
    grid-template-columns: 1fr;
  }
}
</style>
