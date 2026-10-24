<!--
  所属公司：四川淇利馨集团有限公司
  描述：租户自管理 · 角色权限——RoleListPanel + 基本信息 / 功能权限 / 角色成员三 Tab 组装（方案 §6.2）
  说明：页面只做组装 + 接线 + 状态编排；权限树 / 穿梭框 / 编码校验均已迁入 role-admin 组件族与适配层；危险操作确认框由页面触发，错误消息透传后端 message（方案 §6.4）
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
import { createTenantRoleAdminAdapter } from '@/api/adapters/roleAdmin'
import type { MemberOption, PermissionGroup, RoleItem, SaveRoleInput } from '@/types/org-admin'
import { dataScopeLabels } from '@/types/tenant'

const adapter = createTenantRoleAdminAdapter()
const caps = adapter.caps
const activeTab = ref<'basic' | 'permissions' | 'members'>('basic')
/** 错误消息透传后端 message（适配层已归一）。 */
function showError(error: unknown, fallback: string): void {
  ElMessage.error(error instanceof Error ? error.message : fallback)
}

/** ============ 角色列表 ============ */
const roles = ref<RoleItem[]>([])
const rolesLoading = ref(true)
const selectedRoleId = ref('')
const selectedRole = computed(() => roles.value.find((r) => r.id === selectedRoleId.value) ?? null)
const subtitle = computed(() => {
  const role = selectedRole.value
  if (!role) return ''
  const scope = role.dataScope ? `${dataScopeLabels[role.dataScope]} · ` : ''
  return `${scope}${role.permissionCount} 项功能权限 · ${role.memberCount} 名成员`
})

async function loadRoles(keepId?: string): Promise<void> {
  rolesLoading.value = true
  try {
    const list = await adapter.listRoles()
    roles.value = list
    const target = keepId ?? selectedRoleId.value
    selectedRoleId.value = list.some((r) => r.id === target) ? target : (list[0]?.id ?? '')
  } catch (error) {
    showError(error, '加载角色失败')
  } finally { rolesLoading.value = false }
}

/** ============ 权限 / 成员回显（B-1 / B-2 端点，随选中角色切换加载） ============ */
const permissionGroups = ref<PermissionGroup[]>([])
const memberOptions = ref<MemberOption[]>([])
const checkedPermissions = ref<string[]>([])
const selectedMemberIds = ref<string[]>([])
const extrasLoading = ref(false)

async function loadRoleExtras(id: string): Promise<void> {
  extrasLoading.value = true
  try {
    ;[checkedPermissions.value, selectedMemberIds.value] = await Promise.all([
      adapter.fetchRolePermissions(id),
      adapter.fetchRoleMemberIds(id),
    ])
  } catch (error) {
    showError(error, '加载角色详情失败')
  } finally { extrasLoading.value = false }
}

watch(selectedRoleId, (id, oldId) => {
  if (id && id !== oldId) void loadRoleExtras(id)
  if (!id) { checkedPermissions.value = []; selectedMemberIds.value = [] }
})

onMounted(async () => {
  try {
    ;[permissionGroups.value, memberOptions.value] = await Promise.all([
      adapter.fetchPermissionGroups(), adapter.fetchMemberOptions(),
    ])
  } catch (error) {
    showError(error, '加载权限定义失败')
  }
  await loadRoles()
})

/** ============ 新建 / 编辑 / 删除 ============ */
const roleDialogOpen = ref(false)
const roleSubmitting = ref(false)
const editingRole = ref<RoleItem | null>(null)

function openCreateRole(): void {
  editingRole.value = null
  roleDialogOpen.value = true
}

function openEditRole(): void {
  if (!selectedRole.value) return
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
    ElMessage.success(editing ? '角色信息已保存' : '角色已创建，请继续分配功能权限与成员')
  } catch (error) {
    showError(error, '保存角色失败')
  } finally { roleSubmitting.value = false }
}

async function onDeleteRole(): Promise<void> {
  const role = selectedRole.value
  if (!role) return
  try {
    await ElMessageBox.confirm(
      `删除「${role.name}」将同时解除该角色与全部成员的关联，成员将保留其它角色。是否继续？`,
      '删除角色',
      { confirmButtonText: '确认删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch { return }
  try {
    await adapter.deleteRole(role.id)
    ElMessage.success('角色已删除')
    await loadRoles()
  } catch (error) {
    showError(error, '删除角色失败')
  }
}

/** ============ 功能权限 / 角色成员保存 ============ */
const permissionSaving = ref(false)
const membersSaving = ref(false)
/** 内置角色权限只读（租户 builtinLock=permissions；成员仍可配、描述仍可编辑）。 */
const permissionReadonly = computed(() => selectedRole.value?.isBuiltin ?? false)
async function onSavePermissions(codes: string[]): Promise<void> {
  const role = selectedRole.value
  if (!role || permissionReadonly.value) return
  permissionSaving.value = true
  try {
    await adapter.saveRolePermissions(role.id, codes)
    checkedPermissions.value = codes
    ElMessage.success(`已保存「${role.name}」的功能权限（${codes.length} 项）`)
    await loadRoles(role.id)
  } catch (error) {
    showError(error, '保存功能权限失败')
  } finally { permissionSaving.value = false }
}

async function onSaveMembers(ids: string[]): Promise<void> {
  const role = selectedRole.value
  if (!role) return
  membersSaving.value = true
  try {
    await adapter.saveRoleMembers(role.id, ids)
    selectedMemberIds.value = ids
    ElMessage.success(`已更新「${role.name}」的角色成员（${ids.length} 人）`)
    await loadRoles(role.id)
  } catch (error) {
    showError(error, '保存角色成员失败')
  } finally { membersSaving.value = false }
}
</script>

<template>
  <div class="role-page">
    <RoleListPanel
      :roles="roles" :loading="rolesLoading" :selected-id="selectedRoleId" :caps="caps"
      @select="(id: string) => (selectedRoleId = id)" @create="openCreateRole" />
    <section class="role-main">
      <QlxPagePanel v-if="selectedRole" :title="selectedRole.name" :subtitle="subtitle">
        <el-tabs v-model="activeTab">
          <el-tab-pane label="基本信息" name="basic">
            <RoleBasicPanel :role="selectedRole" :caps="caps" @edit="openEditRole" @delete="onDeleteRole" />
          </el-tab-pane>
          <el-tab-pane :label="`功能权限（${selectedRole.permissionCount}）`" name="permissions">
            <RolePermissionPanel :groups="permissionGroups" :checked="checkedPermissions" :readonly="permissionReadonly"
              :saving="permissionSaving" :loading="extrasLoading" @save="onSavePermissions" />
          </el-tab-pane>
          <el-tab-pane :label="`角色成员（${selectedRole.memberCount}）`" name="members">
            <RoleMemberPanel :options="memberOptions" :selected-ids="selectedMemberIds" :saving="membersSaving"
              :loading="extrasLoading" :role-name="selectedRole.name" @save="onSaveMembers" />
          </el-tab-pane>
        </el-tabs>
      </QlxPagePanel>
      <QlxPagePanel v-else title="角色权限" subtitle="请选择或新建角色"><el-empty description="暂无角色" /></QlxPagePanel>
    </section>
    <RoleFormDialog v-model="roleDialogOpen" :editing="editingRole" :caps="caps" :submitting="roleSubmitting" @submit="onRoleSubmit" />
  </div>
</template>
<style scoped>
/* 左列表 + 右工作区（方案 §9 三档断点：≥1200 双栏 280px / 768~1199 收窄 220px / <768 单栏堆叠） */
.role-page { display: grid; gap: var(--qlx-space-4); grid-template-columns: 280px minmax(0, 1fr); align-items: start; }
.role-main { min-width: 0; }
@media (max-width: 1199px) { .role-page { grid-template-columns: 220px minmax(0, 1fr); } }
@media (max-width: 767px) { .role-page { grid-template-columns: 1fr; } }
</style>
