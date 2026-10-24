<!--
  所属公司：四川淇利馨集团有限公司
  描述：通用表单弹窗——配置驱动字段渲染（输入/多行/下拉/数字/开关）+ 校验 + 提交态
  说明：所有新建/编辑/重置密码等操作弹窗复用；提交后由父级调用 mock/REST 并自行关闭
-->
<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'

export interface FormFieldOption {
  label: string
  value: string | number
}

/** 字段值类型（多选下拉为 string[]）。 */
export type FormValue = string | number | boolean | string[]

export interface FormField {
  prop: string
  label: string
  type: 'input' | 'textarea' | 'select' | 'number' | 'switch'
  placeholder?: string
  required?: boolean
  options?: FormFieldOption[]
  /** 下拉是否多选（值为 string[]）。 */
  multiple?: boolean
  /** 下拉是否可搜索。 */
  filterable?: boolean
  /** 输入提示（表单项下方灰字）。 */
  hint?: string
  /** 打开时的初始值。 */
  defaultValue?: FormValue
  /** 正则校验（input 型）。 */
  pattern?: RegExp
  patternMessage?: string
  maxlength?: number
  disabled?: boolean
  /** 是否独占整行（默认单字段半宽）。 */
  full?: boolean
}

const props = withDefaults(
  defineProps<{
    /** 弹窗开关（v-model）。 */
    modelValue: boolean
    title: string
    fields: FormField[]
    submitting?: boolean
    submitText?: string
    width?: number
    /** 服务端字段级校验错误（422 透出），键为字段 prop；重新打开弹窗或再次提交前由父级清空。 */
    fieldErrors?: Record<string, string>
  }>(),
  { submitting: false, submitText: '确定', width: 560 },
)

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
  submit: [payload: Record<string, FormValue>]
}>()

const formRef = ref<FormInstance>()
const form = reactive<Record<string, FormValue>>({})

const visible = computed({
  get: () => props.modelValue,
  set: (open) => emit('update:modelValue', open),
})

/** 打开时按字段默认值初始化表单。 */
watch(
  () => props.modelValue,
  (open) => {
    if (!open) {
      return
    }
    Object.keys(form).forEach((key) => delete form[key])
    for (const field of props.fields) {
      form[field.prop] =
        field.defaultValue ??
        (field.type === 'switch'
          ? false
          : field.type === 'number'
            ? 0
            : field.multiple
              ? []
              : '')
    }
    void formRef.value?.clearValidate()
  },
  { immediate: true },
)

const rules = computed<FormRules>(() => {
  const result: FormRules = {}
  for (const field of props.fields) {
    const fieldRules: FormRules[string] = []
    if (field.required) {
      fieldRules.push({
        required: true,
        message: `${field.label}不能为空`,
        trigger: field.type === 'select' || field.type === 'switch' ? 'change' : 'blur',
      })
    }
    if (field.pattern) {
      const pattern = field.pattern
      fieldRules.push({
        validator: (
          _rule: unknown,
          value: string,
          callback: (error?: Error) => void,
        ) => {
          if (!value || pattern.test(value)) {
            callback()
          } else {
            callback(new Error(field.patternMessage ?? `${field.label}格式不正确`))
          }
        },
        trigger: 'blur',
      })
    }
    if (fieldRules.length) {
      result[field.prop] = fieldRules
    }
  }
  return result
})

async function onSubmit(): Promise<void> {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) {
    return
  }
  emit('submit', { ...form })
}
</script>

<template>
  <el-dialog
    v-model="visible"
    :title="title"
    :width="width"
    :close-on-click-modal="false"
    append-to-body
    class="qlx-form-dialog"
  >
    <el-form
      ref="formRef"
      :model="form"
      :rules="rules"
      label-width="108px"
      label-position="right"
      @submit.prevent="onSubmit"
    >
      <el-row :gutter="16">
        <el-col
          v-for="field in fields"
          :key="field.prop"
          :span="field.full ? 24 : 12"
        >
          <el-form-item :prop="field.prop" :label="field.label" :error="fieldErrors?.[field.prop]">
            <el-select
              v-if="field.type === 'select'"
              v-model="form[field.prop]"
              :placeholder="field.placeholder ?? '请选择'"
              :disabled="field.disabled"
              :multiple="field.multiple"
              :filterable="field.filterable"
              :collapse-tags="field.multiple"
              :collapse-tags-tooltip="field.multiple"
              style="width: 100%"
            >
              <el-option
                v-for="option in field.options"
                :key="String(option.value)"
                :label="option.label"
                :value="option.value"
              />
            </el-select>
            <el-switch
              v-else-if="field.type === 'switch'"
              v-model="form[field.prop] as boolean"
            />
            <el-input-number
              v-else-if="field.type === 'number'"
              v-model="form[field.prop] as number"
              :min="0"
              style="width: 100%"
            />
            <el-input
              v-else-if="field.type === 'textarea'"
              v-model="form[field.prop] as string"
              type="textarea"
              :rows="3"
              :maxlength="field.maxlength ?? 200"
              show-word-limit
              :placeholder="field.placeholder ?? `请输入${field.label}`"
            />
            <el-input
              v-else
              v-model="form[field.prop] as string"
              :maxlength="field.maxlength ?? 64"
              :disabled="field.disabled"
              :placeholder="field.placeholder ?? `请输入${field.label}`"
              clearable
            />
            <p v-if="field.hint" class="qlx-form-dialog__hint">{{ field.hint }}</p>
          </el-form-item>
        </el-col>
      </el-row>
    </el-form>

    <template #footer>
      <el-button @click="visible = false">取消</el-button>
      <el-button type="primary" :loading="submitting" @click="onSubmit">
        {{ submitText }}
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.qlx-form-dialog__hint {
  width: 100%;
  margin-top: 2px;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.5;
}
</style>
