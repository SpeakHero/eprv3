<!--
  所属公司：四川淇利馨集团有限公司
  描述：角色成员面板——el-transfer 穿梭框封装（左全部候选 / 右已分配），全量替换保存
  说明：受控展示组件；候选与已选 Id 经 props 注入，保存意图经 save 抛出（混淆串数组直通，不做转换）。
        <768px 穿梭框纵排（方案 §9 断点规范）
-->
<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { MemberOption } from '@/types/org-admin'

const props = withDefaults(
  defineProps<{
    /** 全部候选成员。 */
    options: MemberOption[]
    /** 已分配成员 Id（回显，受控）。 */
    selectedIds: string[]
    saving?: boolean
    loading?: boolean
    /** 当前角色名（仅用于说明文案）。 */
    roleName?: string
  }>(),
  { saving: false, loading: false, roleName: '' },
)

const emit = defineEmits<{
  save: [ids: string[]]
}>()

const value = ref<string[]>([...props.selectedIds])

watch(
  () => props.selectedIds,
  (ids) => {
    value.value = [...ids]
  },
)

const transferData = computed(() =>
  props.options.map((option) => ({
    key: option.id,
    label: option.orgName ? `${option.name}（${option.orgName}）` : `${option.name}（${option.account}）`,
  })),
)

function onSave(): void {
  emit('save', [...value.value])
}
</script>

<template>
  <div v-loading="loading" class="role-member">
    <div class="role-member__head">
      <p>
        左侧为全部候选成员，右侧为已加入{{ roleName ? `「${roleName}」` : '该角色' }}的成员；
        成员可同时拥有多个角色，权限按并集生效。
      </p>
      <el-button
        v-permission="['portal.role.assign', 'bas.admin.role.action']"
        type="primary"
        size="small"
        :loading="saving"
        @click="onSave"
      >
        保存成员
      </el-button>
    </div>

    <el-transfer
      v-model="value"
      :data="transferData"
      filterable
      filter-placeholder="搜索成员姓名 / 账号"
      :titles="['全部成员', '已分配成员']"
      :button-texts="['移出', '加入']"
      class="role-member__transfer"
    />

    <p class="role-member__note">
      企业管理员账号必须保留内置管理员角色，移除时后端将校验并拒绝。
    </p>
  </div>
</template>

<style scoped>
.role-member__head {
  display: flex;
  margin-bottom: var(--qlx-space-4);
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--qlx-space-4);
}

.role-member__head p {
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

.role-member__transfer {
  display: flex;
  align-items: center;
  justify-content: center;
}

.role-member__transfer :deep(.el-transfer-panel) {
  width: 280px;
}

.role-member__note {
  margin-top: var(--qlx-space-4);
  padding-left: var(--qlx-space-3);
  border-left: 2px solid var(--qlx-gold-400);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

/* 平板横屏（768~1199px）：穿梭框面板收窄 */
@media (max-width: 1199px) {
  .role-member__transfer :deep(.el-transfer-panel) {
    width: 220px;
  }
}

/* 平板竖屏 / 手机（<768px）：穿梭框纵排 */
@media (max-width: 767px) {
  .role-member__transfer {
    flex-direction: column;
    gap: var(--qlx-space-2);
  }

  .role-member__transfer :deep(.el-transfer-panel) {
    width: 100%;
  }

  .role-member__transfer :deep(.el-transfer__buttons) {
    display: flex;
    padding: 0 var(--qlx-space-2);
    gap: var(--qlx-space-2);
  }

  .role-member__transfer :deep(.el-transfer__buttons .el-button) {
    margin: 0;
    transform: rotate(90deg);
  }
}
</style>
