<!--
  所属公司：四川淇利馨集团有限公司
  描述：条件分组节点（递归）——AND/OR 逻辑 + 条件行 + 嵌套分组（≤5 层）
-->
<script setup lang="ts">
import { computed } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import { createGroup, createRule } from './ast'
import { operatorsForType } from './operators'
import type {
  BuilderNode,
  ConditionFieldDef,
  ConditionGroup,
  ConditionRule,
  ConditionValueMode,
} from './types'

const props = defineProps<{
  group: ConditionGroup
  depth: number
  fields: ConditionFieldDef[]
  removable: boolean
}>()

const emit = defineEmits<{ remove: [] }>()

/** 嵌套上限（含根分组）。 */
const MAX_DEPTH = 5
const canNest = computed(() => props.depth < MAX_DEPTH)

/** 操作符选项随字段类型切换。 */
function operatorOptions(field: ConditionFieldDef) {
  return operatorsForType(field.type).map((op) => ({ label: `${op.label}（${op.symbol}）`, value: op.symbol }))
}

function fieldOf(rule: ConditionRule): ConditionFieldDef | undefined {
  return props.fields.find((item) => item.name === rule.field)
}

function currentOperator(field: ConditionFieldDef, rule: ConditionRule) {
  return operatorsForType(field.type).find((op) => op.symbol === rule.operatorSymbol)
}

/** 字典模式仅对字典字段开放。 */
function modeOptions(field: ConditionFieldDef): Array<{ label: string; value: ConditionValueMode }> {
  const options: Array<{ label: string; value: ConditionValueMode }> = [
    { label: '常量', value: 'const' },
  ]
  if (field.type === 'dictionary' && field.options?.length) {
    options.push({ label: '字典项', value: 'dict' })
  }
  options.push({ label: '参数变量', value: 'param' })
  return options
}

/** 切换字段：重置操作符与值（保持模式合法）。 */
function onFieldChange(rule: ConditionRule, fieldName: string): void {
  rule.field = fieldName
  const field = fieldOf(rule)
  if (!field) {
    return
  }
  const whitelist = operatorsForType(field.type)
  if (!whitelist.some((op) => op.symbol === rule.operatorSymbol)) {
    rule.operatorSymbol = whitelist[0]?.symbol ?? '=='
  }
  if (field.type === 'dictionary' && field.options?.length && rule.value.mode === 'const') {
    rule.value.mode = 'dict'
  }
  rule.value.constText = ''
  rule.value.dictValues = []
  rule.value.paramField = ''
  rule.constTextEnd = ''
}

function addRule(): void {
  const firstField = props.fields[0]
  if (!firstField) {
    return
  }
  props.group.children.push(createRule(firstField))
}

function addGroup(): void {
  props.group.children.push(createGroup('AND'))
}

function removeChild(node: BuilderNode): void {
  const index = props.group.children.indexOf(node)
  if (index >= 0) {
    props.group.children.splice(index, 1)
  }
}
</script>

<template>
  <div class="cond-group" :class="{ 'cond-group--or': group.logic === 'OR' }">
    <div class="cond-group__head">
      <el-radio-group v-model="group.logic" size="small">
        <el-radio-button value="AND">且</el-radio-button>
        <el-radio-button value="OR">或</el-radio-button>
      </el-radio-group>
      <div class="cond-group__actions">
        <el-button size="small" text type="primary" @click="addRule">
          <AppIcon name="Plus" :size="14" /> 添加条件
        </el-button>
        <el-button size="small" text @click="addGroup" :disabled="!canNest">
          <AppIcon name="FolderAdd" :size="14" /> 添加分组
        </el-button>
        <el-button v-if="removable" size="small" text type="danger" @click="emit('remove')">
          <AppIcon name="Delete" :size="14" /> 删除分组
        </el-button>
        <span class="cond-group__depth">第 {{ depth }} 层 / 上限 5 层</span>
      </div>
    </div>

    <div v-if="group.children.length === 0" class="cond-group__empty">
      尚无条件，点击「添加条件」开始配置
    </div>

    <template v-for="child in group.children" :key="child.id">
      <!-- 嵌套分组 -->
      <ConditionGroupNode
        v-if="child.kind === 'group'"
        :group="child"
        :depth="depth + 1"
        :fields="fields"
        :removable="true"
        @remove="removeChild(child)"
      />

      <!-- 条件行 -->
      <div v-else class="cond-rule">
        <el-select
          v-model="child.field"
          class="cond-rule__field"
          filterable
          placeholder="选择字段"
          @change="(value: string) => onFieldChange(child, value)"
        >
          <el-option
            v-for="field in fields"
            :key="field.name"
            :label="`${field.label}（${field.name}）`"
            :value="field.name"
          />
        </el-select>

        <el-select
          v-model="child.operatorSymbol"
          class="cond-rule__op"
          placeholder="操作符"
        >
          <el-option
            v-for="op in (fieldOf(child) ? operatorOptions(fieldOf(child)!) : [])"
            :key="op.value"
            :label="op.label"
            :value="op.value"
          />
        </el-select>

        <!-- 无值操作符 -->
        <span v-if="currentOperator(fieldOf(child)!, child)?.valueKind === 'none'" class="cond-rule__none">
          无需取值
        </span>

        <!-- 值编辑区 -->
        <div
          v-else-if="fieldOf(child)"
          class="cond-rule__value"
        >
          <el-radio-group
            v-model="child.value.mode"
            size="small"
            class="cond-rule__mode"
          >
            <el-radio-button
              v-for="mode in modeOptions(fieldOf(child)!)"
              :key="mode.value"
              :value="mode.value"
            >
              {{ mode.label }}
            </el-radio-button>
          </el-radio-group>

          <!-- 列表形态 -->
          <template v-if="currentOperator(fieldOf(child)!, child)?.valueKind === 'list'">
            <el-select
              v-if="child.value.mode === 'dict'"
              v-model="child.value.dictValues"
              multiple
              collapse-tags
              collapse-tags-tooltip
              filterable
              placeholder="选择字典项（可多选）"
              class="cond-rule__input"
            >
              <el-option
                v-for="option in fieldOf(child)!.options ?? []"
                :key="option.value"
                :label="option.label"
                :value="option.value"
              />
            </el-select>
            <el-input
              v-else-if="child.value.mode === 'const'"
              v-model="child.value.constText"
              placeholder="多个值用英文逗号分隔，如 Draft,Submitted"
              class="cond-rule__input"
            />
            <el-select
              v-else
              v-model="child.value.paramField"
              filterable
              allow-create
              placeholder="选择或输入变量名（列表变量）"
              class="cond-rule__input"
            >
              <el-option
                v-for="field in fields"
                :key="field.name"
                :label="`\${${field.name}}`"
                :value="field.name"
              />
            </el-select>
          </template>

          <!-- 区间形态（Between） -->
          <template v-else-if="currentOperator(fieldOf(child)!, child)?.valueKind === 'range'">
            <template v-if="child.value.mode === 'param'">
              <el-select
                v-model="child.value.paramField"
                filterable
                allow-create
                placeholder="下界变量"
                class="cond-rule__input cond-rule__input--narrow"
              >
                <el-option
                  v-for="field in fields"
                  :key="field.name"
                  :label="`\${${field.name}}`"
                  :value="field.name"
                />
              </el-select>
              <span class="cond-rule__dash">至</span>
              <el-input
                v-model="child.constTextEnd"
                placeholder="上界常量"
                class="cond-rule__input cond-rule__input--narrow"
              />
            </template>
            <template v-else>
              <el-date-picker
                v-if="fieldOf(child)!.type === 'date'"
                v-model="child.value.constText"
                type="date"
                value-format="YYYY-MM-DD"
                placeholder="下界日期"
                class="cond-rule__input cond-rule__input--narrow"
              />
              <el-date-picker
                v-else-if="fieldOf(child)!.type === 'datetime'"
                v-model="child.value.constText"
                type="datetime"
                value-format="YYYY-MM-DD[T]HH:mm:ss"
                placeholder="下界时间"
                class="cond-rule__input cond-rule__input--narrow"
              />
              <el-input
                v-else
                v-model="child.value.constText"
                :placeholder="fieldOf(child)!.type === 'number' ? '下界数值' : '下界常量'"
                class="cond-rule__input cond-rule__input--narrow"
              />
              <span class="cond-rule__dash">至</span>
              <el-date-picker
                v-if="fieldOf(child)!.type === 'date'"
                v-model="child.constTextEnd"
                type="date"
                value-format="YYYY-MM-DD"
                placeholder="上界日期"
                class="cond-rule__input cond-rule__input--narrow"
              />
              <el-date-picker
                v-else-if="fieldOf(child)!.type === 'datetime'"
                v-model="child.constTextEnd"
                type="datetime"
                value-format="YYYY-MM-DD[T]HH:mm:ss"
                placeholder="上界时间"
                class="cond-rule__input cond-rule__input--narrow"
              />
              <el-input
                v-else
                v-model="child.constTextEnd"
                :placeholder="fieldOf(child)!.type === 'number' ? '上界数值' : '上界常量'"
                class="cond-rule__input cond-rule__input--narrow"
              />
            </template>
          </template>

          <!-- 单值形态 -->
          <template v-else>
            <el-select
              v-if="child.value.mode === 'dict'"
              v-model="child.value.dictValues[0]"
              filterable
              placeholder="选择字典项"
              class="cond-rule__input"
            >
              <el-option
                v-for="option in fieldOf(child)!.options ?? []"
                :key="option.value"
                :label="option.label"
                :value="option.value"
              />
            </el-select>
            <el-select
              v-else-if="child.value.mode === 'param'"
              v-model="child.value.paramField"
              filterable
              allow-create
              placeholder="选择或输入变量名"
              class="cond-rule__input"
            >
              <el-option
                v-for="field in fields"
                :key="field.name"
                :label="`\${${field.name}}`"
                :value="field.name"
              />
            </el-select>
            <template v-else>
              <el-date-picker
                v-if="fieldOf(child)!.type === 'date'"
                v-model="child.value.constText"
                type="date"
                value-format="YYYY-MM-DD"
                placeholder="选择日期"
                class="cond-rule__input"
              />
              <el-date-picker
                v-else-if="fieldOf(child)!.type === 'datetime'"
                v-model="child.value.constText"
                type="datetime"
                value-format="YYYY-MM-DD[T]HH:mm:ss"
                placeholder="选择时间"
                class="cond-rule__input"
              />
              <el-select
                v-else-if="fieldOf(child)!.type === 'boolean'"
                v-model="child.value.constText"
                placeholder="选择布尔值"
                class="cond-rule__input"
              >
                <el-option label="true（是）" value="true" />
                <el-option label="false（否）" value="false" />
              </el-select>
              <el-input
                v-else
                v-model="child.value.constText"
                :placeholder="fieldOf(child)!.type === 'number' ? '输入数值' : '输入常量'"
                class="cond-rule__input"
              />
            </template>
          </template>
        </div>

        <el-button class="cond-rule__remove" size="small" text type="danger" @click="removeChild(child)">
          <AppIcon name="Close" :size="14" />
        </el-button>
      </div>
    </template>
  </div>
</template>

<style scoped>
.cond-group {
  border: 1px solid var(--qlx-brand-100);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-brand-50);
  padding: var(--qlx-space-3);
  display: grid;
  gap: var(--qlx-space-2);
}

.cond-group--or {
  border-color: var(--qlx-gold-100);
  background: #fdfaf1;
}

.cond-group__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: var(--qlx-space-2);
}

.cond-group__actions {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
}

.cond-group__depth {
  font-size: var(--qlx-text-xs);
  color: var(--qlx-ink-400);
}

.cond-group__empty {
  border: 1px dashed var(--qlx-ink-200);
  border-radius: var(--qlx-radius-sm);
  background: var(--qlx-surface);
  padding: var(--qlx-space-4);
  text-align: center;
  font-size: var(--qlx-text-sm);
  color: var(--qlx-ink-400);
}

.cond-rule {
  display: flex;
  align-items: flex-start;
  gap: var(--qlx-space-2);
  background: var(--qlx-surface);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-sm);
  padding: var(--qlx-space-2) var(--qlx-space-3);
}

.cond-rule__field {
  width: 210px;
  flex: none;
}

.cond-rule__op {
  width: 150px;
  flex: none;
}

.cond-rule__value {
  flex: 1;
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  flex-wrap: wrap;
  min-width: 0;
}

.cond-rule__mode {
  flex: none;
}

.cond-rule__input {
  width: 240px;
}

.cond-rule__input--narrow {
  width: 150px;
}

.cond-rule__dash {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-sm);
}

.cond-rule__none {
  font-size: var(--qlx-text-sm);
  color: var(--qlx-ink-400);
}

.cond-rule__remove {
  margin-left: auto;
  flex: none;
}
</style>
