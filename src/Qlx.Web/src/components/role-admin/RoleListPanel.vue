<!--
  所属公司：四川淇利馨集团有限公司
  描述：角色列表面板——搜索 + 角色项（名称 / 编码 / 内置徽标 / 成员数）+ 新建入口（双端同构）
  说明：受控展示组件，不发请求；数据经 props 注入，意图经 select / create 抛出（方案 §6.1）
-->
<script setup lang="ts">
import { computed, ref } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import type { RoleAdminCaps } from '@/api/adapters/roleAdmin'
import type { RoleItem } from '@/types/org-admin'
import { countBuiltinRoles, filterRoles } from './role-admin-utils'

const props = withDefaults(
  defineProps<{
    roles: RoleItem[]
    loading?: boolean
    selectedId?: string
    caps: RoleAdminCaps
  }>(),
  { loading: false, selectedId: '' },
)

const emit = defineEmits<{
  select: [id: string]
  create: []
}>()

const keyword = ref('')

const filtered = computed(() => filterRoles(props.roles, keyword.value))
const builtinCount = computed(() => countBuiltinRoles(props.roles))
</script>

<template>
  <aside class="role-list-panel">
    <header class="role-list-panel__head">
      <div>
        <h2>角色</h2>
        <p class="qlx-numeric">{{ roles.length }} 个 · 内置 {{ builtinCount }}</p>
      </div>
      <el-button
        v-permission="caps.isPortal ? 'portal.role.create' : 'bas.admin.role.action'"
        type="primary"
        size="small"
        @click="emit('create')"
      >
        <AppIcon name="Lock" :size="13" />
        新建
      </el-button>
    </header>

    <div class="role-list-panel__search">
      <el-input v-model="keyword" size="small" clearable placeholder="搜索角色名称 / 编码">
        <template #prefix>
          <AppIcon name="Search" :size="13" />
        </template>
      </el-input>
    </div>

    <ul v-loading="loading" class="role-list">
      <li v-for="role in filtered" :key="role.id">
        <button
          type="button"
          class="role-item"
          :class="{ 'is-active': role.id === selectedId }"
          @click="emit('select', role.id)"
        >
          <span class="role-item__main">
            <strong>{{ role.name }}</strong>
            <em class="role-item__code">{{ role.code }}</em>
          </span>
          <span class="role-item__meta">
            <el-tag v-if="role.isBuiltin" size="small" effect="plain" type="warning">内置</el-tag>
            <span class="qlx-numeric">{{ role.memberCount }} 人</span>
          </span>
        </button>
      </li>
      <li v-if="!loading && !filtered.length" class="role-list__empty">
        {{ keyword ? '暂无匹配角色' : '暂无角色，请点击右上角新建' }}
      </li>
    </ul>

    <p class="role-list-panel__note">
      {{ caps.isPortal
        ? '系统内置角色全锁定（不可编辑 / 删除 / 分配）；自定义角色编码须带 portal. 前缀。'
        : '内置角色权限由系统固定，自定义角色可按业务需要组合功能与数据范围。' }}
    </p>
  </aside>
</template>

<style scoped>
.role-list-panel {
  display: flex;
  min-height: 460px;
  flex-direction: column;
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
}

.role-list-panel__head {
  display: flex;
  padding: var(--qlx-space-3) var(--qlx-space-4);
  border-bottom: 1px solid var(--qlx-line);
  align-items: center;
  justify-content: space-between;
  gap: var(--qlx-space-2);
}

.role-list-panel__head h2 {
  color: var(--qlx-brand-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-base);
  font-weight: 600;
  letter-spacing: 0.04em;
}

.role-list-panel__head p {
  margin-top: 2px;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.role-list-panel__search {
  padding: var(--qlx-space-3) var(--qlx-space-3) var(--qlx-space-2);
}

.role-list {
  flex: 1;
  padding: 0 var(--qlx-space-2);
  overflow-y: auto;
}

.role-item {
  display: flex;
  width: 100%;
  padding: var(--qlx-space-2) var(--qlx-space-3);
  border: 1px solid transparent;
  border-radius: var(--qlx-radius-sm);
  background: none;
  text-align: left;
  align-items: center;
  justify-content: space-between;
  gap: var(--qlx-space-2);
  cursor: pointer;
  transition:
    background var(--qlx-duration-fast) var(--qlx-ease-out),
    border-color var(--qlx-duration-fast) var(--qlx-ease-out);
}

.role-item:hover {
  background: var(--qlx-brand-50);
}

.role-item.is-active {
  border-color: var(--qlx-brand-100);
  background: var(--qlx-brand-50);
}

.role-item__main {
  display: flex;
  min-width: 0;
  flex-direction: column;
  line-height: 1.4;
}

.role-item__main strong {
  color: var(--qlx-ink-800);
  font-size: var(--qlx-text-sm);
  font-weight: 500;
}

.role-item.is-active .role-item__main strong {
  color: var(--qlx-brand-800);
}

.role-item__code {
  overflow: hidden;
  color: var(--qlx-ink-400);
  font-size: 11px;
  font-style: normal;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.role-item__meta {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  gap: 6px;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.role-list__empty {
  padding: var(--qlx-space-8) 0;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  text-align: center;
}

.role-list-panel__note {
  padding: var(--qlx-space-3);
  border-top: 1px dashed var(--qlx-ink-200);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}
</style>
