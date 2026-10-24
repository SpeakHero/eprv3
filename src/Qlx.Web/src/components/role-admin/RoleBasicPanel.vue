<!--
  所属公司：四川淇利馨集团有限公司
  描述：角色基本信息面板——描述列表 + 编辑 / 删除操作（内嵌 QlxDescriptionList）
  说明：受控展示组件；编辑 / 删除可用性按 caps（builtinLock / canEditBuiltin / dataScope）收敛，
        危险操作确认框由页面触发（组件只 emit 意图）
-->
<script setup lang="ts">
import { computed } from 'vue'
import QlxDescriptionList from '@/components/crud/QlxDescriptionList.vue'
import type { DescriptionItem } from '@/components/crud/QlxDescriptionList.vue'
import type { RoleAdminCaps } from '@/api/adapters/roleAdmin'
import type { RoleItem } from '@/types/org-admin'
import { dataScopeHints, dataScopeLabels } from '@/types/tenant'
import { formatDateTime } from '@/utils/format'

const props = defineProps<{
  role: RoleItem | null
  caps: RoleAdminCaps
}>()

const emit = defineEmits<{
  edit: []
  delete: []
}>()

/** 编辑可用性：内置角色是否可编辑由 caps.canEditBuiltin 决定（当前两场景均 false）。 */
const editDisabled = computed(() => (props.role?.isBuiltin ?? false) && !props.caps.canEditBuiltin)

/** 删除可用性：内置角色一律不可删除。 */
const deleteVisible = computed(() => props.role !== null && !props.role.isBuiltin)

const editHint = computed(() => {
  if (!props.role?.isBuiltin) {
    return ''
  }
  return props.caps.builtinLock === 'all' ? '系统内置角色全锁定，不可编辑' : '内置角色仅可调整描述'
})

const items = computed<DescriptionItem[]>(() => {
  const role = props.role
  if (!role) {
    return []
  }
  const list: DescriptionItem[] = [
    { label: '角色名称', value: role.name },
    { label: '角色编码', value: role.code },
    { label: '角色类型', value: role.isBuiltin ? '系统内置（不可删除）' : '自定义角色' },
  ]
  if (props.caps.dataScope && role.dataScope) {
    list.push({ label: '数据范围', value: dataScopeLabels[role.dataScope] })
  }
  list.push(
    { label: '功能权限', value: `${role.permissionCount} 项` },
    { label: '角色成员', value: `${role.memberCount} 人` },
    { label: '更新时间', value: formatDateTime(role.updatedAt) },
  )
  if (props.caps.dataScope && role.dataScope) {
    list.push({ label: '数据范围说明', value: dataScopeHints[role.dataScope], full: true })
  }
  list.push({ label: '角色描述', value: role.description || '—', full: true })
  return list
})
</script>

<template>
  <div v-if="role" class="role-basic">
    <div class="role-basic__toolbar">
      <el-tag v-if="role.isBuiltin" type="warning" size="small" effect="light">系统内置</el-tag>
      <span v-if="editHint" class="role-basic__hint">{{ editHint }}</span>
      <span class="role-basic__spacer" />
      <el-button
        v-permission="caps.isPortal ? 'portal.role.update' : 'bas.admin.role.action'"
        size="small"
        :disabled="editDisabled"
        @click="emit('edit')"
      >
        编辑基本信息
      </el-button>
      <el-button
        v-if="deleteVisible"
        v-permission="caps.isPortal ? 'portal.role.delete' : 'bas.admin.role.action'"
        size="small"
        type="danger"
        plain
        @click="emit('delete')"
      >
        删除角色
      </el-button>
    </div>

    <QlxDescriptionList :items="items" :column="3" />

    <p v-if="caps.dataScope" class="role-basic__note">
      功能权限决定“能做什么”，数据范围决定“能看到谁的数据”；两者叠加后生效。
    </p>
  </div>
  <el-empty v-else description="请选择左侧角色查看基本信息" />
</template>

<style scoped>
.role-basic__toolbar {
  display: flex;
  margin-bottom: var(--qlx-space-4);
  align-items: center;
  gap: var(--qlx-space-2);
}

.role-basic__spacer {
  flex: 1;
}

.role-basic__hint {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.role-basic__note {
  margin-top: var(--qlx-space-4);
  padding-left: var(--qlx-space-3);
  border-left: 2px solid var(--qlx-gold-400);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}
</style>
