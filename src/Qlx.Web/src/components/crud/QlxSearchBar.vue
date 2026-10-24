<!--
  所属公司：四川淇利馨集团有限公司
  描述：通用搜索栏——配置驱动（输入框 / 下拉），查询 + 重置，所有列表页复用
-->
<script setup lang="ts">
import { reactive, watch } from 'vue'
import AppIcon from '@/components/AppIcon.vue'

export interface SearchFieldOption {
  label: string
  value: string
}

export interface SearchField {
  prop: string
  label: string
  type: 'input' | 'select'
  placeholder?: string
  options?: SearchFieldOption[]
  /** 输入框宽度（默认 220px）。 */
  width?: number
}

const props = defineProps<{
  fields: SearchField[]
  /** 查询条件对象（受控于父级 useCrudTable.query）。 */
  modelValue: Record<string, string>
}>()

const emit = defineEmits<{
  search: [query: Record<string, string>]
  reset: []
}>()

const form = reactive<Record<string, string>>({ ...props.modelValue })

watch(
  () => props.modelValue,
  (value) => {
    Object.keys(form).forEach((key) => delete form[key])
    Object.assign(form, value)
  },
)

function submit(): void {
  emit('search', { ...form })
}

function reset(): void {
  Object.keys(form).forEach((key) => delete form[key])
  emit('reset')
}
</script>

<template>
  <el-form class="qlx-search" inline @submit.prevent="submit">
    <el-form-item v-for="field in fields" :key="field.prop" :label="field.label">
      <el-select
        v-if="field.type === 'select'"
        v-model="form[field.prop]"
        :placeholder="field.placeholder ?? '全部'"
        clearable
        :style="{ width: `${field.width ?? 160}px` }"
      >
        <el-option
          v-for="option in field.options"
          :key="option.value"
          :label="option.label"
          :value="option.value"
        />
      </el-select>
      <el-input
        v-else
        v-model="form[field.prop]"
        :placeholder="field.placeholder ?? `请输入${field.label}`"
        clearable
        :style="{ width: `${field.width ?? 220}px` }"
        @keyup.enter="submit"
      />
    </el-form-item>
    <el-form-item>
      <el-button type="primary" native-type="submit">
        <AppIcon name="Search" :size="14" />
        查询
      </el-button>
      <el-button @click="reset">重置</el-button>
    </el-form-item>
  </el-form>
</template>

<style scoped>
.qlx-search {
  padding: var(--qlx-space-4) var(--qlx-space-4) 0;
  border-bottom: 1px solid var(--qlx-line);
}

.qlx-search :deep(.el-form-item) {
  margin-right: var(--qlx-space-4);
  margin-bottom: var(--qlx-space-4);
}

.qlx-search :deep(.el-button .el-icon) {
  margin-right: 2px;
}
</style>
