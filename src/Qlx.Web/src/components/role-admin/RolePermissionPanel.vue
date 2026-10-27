<!--
  所属公司：四川淇利馨集团有限公司
  描述：角色功能权限面板——el-tree 勾选 + 全选 / 清空 / 计数 + 内置角色只读提示条（双端同构）
  说明：受控展示组件；树构建 / 勾选码提取 / 计数走 role-admin-utils 纯函数；
        只读语义由 readonly 表达（租户内置 = 权限只读，门户内置 = 全锁，页面按 caps.builtinLock 计算）
-->
<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import type { PermissionGroup } from '@/types/org-admin'
import {
  allPermissionCodes,
  buildPermissionTree,
  countPermissions,
  extractCheckedCodes,
} from './role-admin-utils'

/** el-tree 暴露方法子集（避免为类型引入运行时依赖）。 */
interface PermissionTreeExpose {
  setCheckedKeys: (keys: string[]) => void
  getCheckedKeys: (leafOnly?: boolean) => Array<string | number>
}

const props = withDefaults(
  defineProps<{
    groups: PermissionGroup[]
    /** 已勾选权限码（回显，受控）。 */
    checked: string[]
    /** 只读（内置角色锁定）。 */
    readonly?: boolean
    saving?: boolean
    loading?: boolean
    /** 只读原因提示（页面按 caps.builtinLock 给出场景化文案）。 */
    readonlyReason?: string
  }>(),
  { readonly: false, saving: false, loading: false, readonlyReason: '' },
)

const emit = defineEmits<{
  save: [codes: string[]]
}>()

const treeRef = ref<PermissionTreeExpose>()
const checkedCount = ref(0)

const treeData = computed(() => buildPermissionTree(props.groups))
const total = computed(() => countPermissions(props.groups))

/** 外部勾选集变化 → 同步到 el-tree（回显）。 */
async function syncChecked(): Promise<void> {
  await nextTick()
  treeRef.value?.setCheckedKeys([...props.checked])
  checkedCount.value = props.checked.length
}

watch(() => [props.groups, props.checked] as const, syncChecked, { immediate: true, deep: true })

function readChecked(): string[] {
  // 仅取叶子节点：父节点（module: 前缀）勾选时由 el-tree 级联到叶子，避免只勾父节点导致权限码为空
  return extractCheckedCodes(treeRef.value?.getCheckedKeys(true) ?? [])
}

function onCheck(): void {
  checkedCount.value = readChecked().length
}

function checkAll(): void {
  const codes = allPermissionCodes(props.groups)
  treeRef.value?.setCheckedKeys(codes)
  checkedCount.value = codes.length
}

function clearAll(): void {
  treeRef.value?.setCheckedKeys([])
  checkedCount.value = 0
}

function onSave(): void {
  if (props.readonly) {
    return
  }
  emit('save', readChecked())
}
</script>

<template>
  <div class="role-perm">
    <div class="role-perm__toolbar">
      <div class="role-perm__actions">
        <el-button size="small" :disabled="readonly" @click="checkAll">全选</el-button>
        <el-button size="small" :disabled="readonly" @click="clearAll">清空</el-button>
        <span class="role-perm__count qlx-numeric">已选 {{ checkedCount }} / {{ total }} 项</span>
      </div>
      <el-button
        v-if="!readonly"
        v-permission="['portal.role.assign', 'bas.admin.role.action']"
        type="primary"
        size="small"
        :loading="saving"
        @click="onSave"
      >
        保存权限
      </el-button>
    </div>

    <p v-if="readonly" class="role-perm__readonly">
      {{ readonlyReason || '内置角色的功能权限由系统固定，不可调整；如需差异化授权请新建自定义角色。' }}
    </p>

    <div v-loading="loading" class="role-perm__tree" :class="{ 'is-readonly': readonly }">
      <el-tree
        v-if="treeData.length"
        ref="treeRef"
        :data="treeData"
        node-key="id"
        show-checkbox
        default-expand-all
        :props="{ label: 'label', children: 'children' }"
        @check="onCheck"
      >
        <template #default="{ data }">
          <span class="perm-node">
            <AppIcon v-if="data.icon" :name="data.icon" :size="13" class="perm-node__icon" />
            <span class="perm-node__label">{{ data.label }}</span>
            <em v-if="data.description" class="perm-node__desc">{{ data.description }}</em>
          </span>
        </template>
      </el-tree>
      <el-empty v-else-if="!loading" description="暂无权限定义" :image-size="72" />
    </div>
  </div>
</template>

<style scoped>
.role-perm__toolbar {
  display: flex;
  margin-bottom: var(--qlx-space-3);
  align-items: center;
  justify-content: space-between;
  gap: var(--qlx-space-3);
}

.role-perm__actions {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
}

.role-perm__count {
  margin-left: var(--qlx-space-2);
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
}

.role-perm__readonly {
  margin-bottom: var(--qlx-space-3);
  padding: var(--qlx-space-2) var(--qlx-space-3);
  border-left: 3px solid var(--qlx-warning);
  background: var(--qlx-warning-soft);
  color: var(--qlx-warning);
  font-size: var(--qlx-text-xs);
}

.role-perm__tree {
  min-height: 200px;
  max-height: 560px;
  padding: var(--qlx-space-3);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-sm);
  background: var(--qlx-surface);
  overflow: auto;
}

.role-perm__tree.is-readonly {
  background: var(--qlx-canvas);
  pointer-events: none;
}

.role-perm__tree :deep(.el-tree) {
  background: transparent;
  font-size: var(--qlx-text-sm);
}

.role-perm__tree :deep(.el-tree-node__content) {
  height: 32px;
}

.perm-node {
  display: inline-flex;
  min-width: 0;
  align-items: center;
  gap: 6px;
}

.perm-node__icon {
  color: var(--qlx-brand-600);
}

.perm-node__label {
  color: var(--qlx-ink-800);
}

.perm-node__desc {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  font-style: normal;
}
</style>
