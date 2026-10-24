<!--
  所属公司：四川淇利馨集团有限公司
  描述：通用详情描述列表——配置驱动的键值对展示（详情页头部复用）
-->
<script setup lang="ts">
export interface DescriptionItem {
  label: string
  /** 静态文本（与 slot 二选一）。 */
  value?: string
  /** 独占整行。 */
  full?: boolean
}

defineProps<{
  items: DescriptionItem[]
  /** 列数（默认 3 列，窄屏自动降列）。 */
  column?: number
}>()
</script>

<template>
  <el-descriptions :column="column ?? 3" border class="qlx-desc">
    <el-descriptions-item
      v-for="item in items"
      :key="item.label"
      :label="item.label"
      :span="item.full ? (column ?? 3) : 1"
      label-class-name="qlx-desc__label"
    >
      <slot :name="`item-${item.label}`" :item="item">
        <span class="qlx-desc__value">{{ item.value || '—' }}</span>
      </slot>
    </el-descriptions-item>
  </el-descriptions>
</template>

<style scoped>
.qlx-desc :deep(.qlx-desc__label) {
  width: 128px;
  color: var(--qlx-ink-500);
  background: var(--qlx-brand-50);
  font-weight: 400;
}

.qlx-desc :deep(.el-descriptions__label) {
  font-size: var(--qlx-text-sm);
}

.qlx-desc__value {
  color: var(--qlx-ink-800);
  font-size: var(--qlx-text-sm);
}
</style>
