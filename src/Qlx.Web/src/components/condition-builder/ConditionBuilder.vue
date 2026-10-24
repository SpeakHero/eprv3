<!--
  所属公司：四川淇利馨集团有限公司
  描述：条件构建器——字段树 → 操作符（按字段类型白名单）→ 值（常量/字典/参数变量）→ AND/OR 分组（≤5 层）
  输出：AST JSON（expr_json 形态，可被 IConditionExpressionEngine.Parse 消费）+ 可读文本 + 样例试算
-->
<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import ConditionGroupNode from './ConditionGroupNode.vue'
import { astToText, builderToAst, collectVariables, createGroup, validateBuilder } from './ast'
import { fetchConditionFields, mockSampleData, previewCondition } from './mock'
import type { ConditionAstNode, ConditionFieldDef, ConditionFieldGroup, ConditionGroup, ConditionTrialResult } from './types'

const emit = defineEmits<{
  change: [payload: { ast: ConditionAstNode | null; text: string; valid: boolean }]
}>()

const fieldGroups = ref<ConditionFieldGroup[]>([])
const root = ref<ConditionGroup>(createGroup('AND'))
const sampleJson = ref(JSON.stringify(mockSampleData, null, 2))
const trial = ref<ConditionTrialResult | null>(null)
const trialing = ref(false)

const flatFields = computed<ConditionFieldDef[]>(() =>
  fieldGroups.value.flatMap((group) => group.children),
)

const fieldLookup = computed(() => {
  const map = new Map<string, ConditionFieldDef>()
  flatFields.value.forEach((field) => map.set(field.name, field))
  return map
})

const ast = computed<ConditionAstNode | null>(() => builderToAst(root.value, fieldLookup.value))
const errors = computed(() => (flatFields.value.length === 0 ? [] : validateBuilder(root.value, fieldLookup.value)))
const valid = computed(() => ast.value !== null && errors.value.length === 0)

const readableText = computed(() =>
  ast.value ? astToText(ast.value, fieldLookup.value) : '（暂无条件）',
)

const astJsonText = computed(() =>
  ast.value ? JSON.stringify(ast.value, null, 2) : '// 暂无条件',
)

const usedVariables = computed(() =>
  ast.value ? [...collectVariables(ast.value)] : [],
)

const sampleParseError = computed(() => {
  try {
    const parsed: unknown = JSON.parse(sampleJson.value)
    return parsed !== null && typeof parsed === 'object' ? '' : '样例数据必须是 JSON 对象'
  } catch {
    return '样例数据不是合法 JSON'
  }
})

async function runTrial(): Promise<void> {
  if (!ast.value || sampleParseError.value) {
    return
  }
  trialing.value = true
  try {
    trial.value = await previewCondition({
      astJson: ast.value,
      sampleData: JSON.parse(sampleJson.value) as Record<string, unknown>,
    })
  } finally {
    trialing.value = false
  }
}

function emitChange(): void {
  emit('change', { ast: ast.value, text: readableText.value, valid: valid.value })
}

onMounted(async () => {
  fieldGroups.value = await fetchConditionFields()
})

// 树内任意编辑（含深层嵌套）都同步输出
watch(root, () => emitChange(), { deep: true })
watch(flatFields, () => emitChange())
</script>

<template>
  <div class="cond-builder">
    <!-- 左：字段树 -->
    <aside class="cond-builder__fields">
      <div class="cond-builder__panel-title">
        <AppIcon name="Menu" :size="14" />
        字段清单
      </div>
      <el-tree
        :data="fieldGroups"
        :props="{ label: 'label', children: 'children' }"
        default-expand-all
        :expand-on-click-node="false"
      >
        <template #default="{ data }">
          <span class="cond-field-node">
            <span class="cond-field-node__label">{{ data.label }}</span>
            <span v-if="data.name" class="cond-field-node__name">{{ data.name }}</span>
          </span>
        </template>
      </el-tree>
      <p class="cond-builder__hint">操作符白名单随字段类型自动过滤；参数变量在后端按 ${变量名} 求值。</p>
    </aside>

    <!-- 中：规则画布 -->
    <section class="cond-builder__canvas">
      <div class="cond-builder__panel-title">
        <AppIcon name="Filter" :size="14" />
        条件编排
      </div>
      <ConditionGroupNode :group="root" :depth="1" :fields="flatFields" :removable="false" />
      <el-alert
        v-for="error in errors.slice(0, 3)"
        :key="error"
        :title="error"
        type="warning"
        :closable="false"
        class="cond-builder__alert"
      />
    </section>

    <!-- 右：输出与试算 -->
    <aside class="cond-builder__output">
      <div class="cond-builder__panel-title">
        <AppIcon name="Document" :size="14" />
        输出与试算
      </div>

      <div class="cond-block">
        <div class="cond-block__head">
          <span>可读文本</span>
          <el-tag v-if="valid" type="success" size="small">可提交</el-tag>
          <el-tag v-else type="info" size="small">未完成</el-tag>
        </div>
        <p class="cond-block__text">{{ readableText }}</p>
        <p class="cond-block__meta">变量：{{ usedVariables.length ? usedVariables.join('、') : '—' }}</p>
      </div>

      <div class="cond-block">
        <div class="cond-block__head"><span>AST JSON（expr_json）</span></div>
        <pre class="cond-block__json">{{ astJsonText }}</pre>
      </div>

      <div class="cond-block">
        <div class="cond-block__head"><span>样例数据试算</span></div>
        <el-input
          v-model="sampleJson"
          type="textarea"
          :rows="6"
          class="cond-block__sample"
          spellcheck="false"
        />
        <div class="cond-block__trial">
          <el-button type="primary" size="small" :disabled="!valid || !!sampleParseError" :loading="trialing" @click="runTrial">
            试算
          </el-button>
          <span v-if="sampleParseError" class="cond-block__error">{{ sampleParseError }}</span>
          <template v-if="trial">
            <el-tag :type="trial.matched ? 'success' : 'danger'" size="small">
              {{ trial.matched ? '命中' : '未命中' }}
            </el-tag>
            <span class="cond-block__detail">{{ trial.detail }}</span>
          </template>
        </div>
      </div>
    </aside>
  </div>
</template>

<style scoped>
.cond-builder {
  display: grid;
  grid-template-columns: 240px minmax(0, 1fr) 380px;
  gap: var(--qlx-space-4);
  align-items: start;
}

.cond-builder__fields,
.cond-builder__canvas,
.cond-builder__output {
  background: var(--qlx-surface);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-lg);
  padding: var(--qlx-space-4);
  display: grid;
  gap: var(--qlx-space-3);
}

.cond-builder__panel-title {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-1);
  font-size: var(--qlx-text-sm);
  font-weight: 600;
  color: var(--qlx-brand-800);
  padding-bottom: var(--qlx-space-2);
  border-bottom: 1px solid var(--qlx-line);
}

.cond-field-node {
  display: flex;
  align-items: baseline;
  gap: var(--qlx-space-2);
  min-width: 0;
}

.cond-field-node__name {
  font-size: var(--qlx-text-xs);
  color: var(--qlx-ink-400);
  font-family: var(--qlx-font-mono);
}

.cond-builder__hint {
  margin: 0;
  font-size: var(--qlx-text-xs);
  color: var(--qlx-ink-400);
  line-height: 1.6;
}

.cond-builder__alert {
  margin: 0;
}

.cond-block {
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  padding: var(--qlx-space-3);
  display: grid;
  gap: var(--qlx-space-2);
  background: var(--qlx-canvas);
}

.cond-block__head {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  font-size: var(--qlx-text-sm);
  font-weight: 600;
  color: var(--qlx-ink-700);
}

.cond-block__text {
  margin: 0;
  font-size: var(--qlx-text-sm);
  line-height: 1.7;
  color: var(--qlx-ink-800);
}

.cond-block__meta {
  margin: 0;
  font-size: var(--qlx-text-xs);
  color: var(--qlx-ink-500);
  font-family: var(--qlx-font-mono);
  word-break: break-all;
}

.cond-block__json {
  margin: 0;
  max-height: 240px;
  overflow: auto;
  font-family: var(--qlx-font-mono);
  font-size: var(--qlx-text-xs);
  line-height: 1.6;
  color: var(--qlx-brand-800);
  background: var(--qlx-surface);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-sm);
  padding: var(--qlx-space-2);
  white-space: pre-wrap;
  word-break: break-all;
}

.cond-block__sample :deep(textarea) {
  font-family: var(--qlx-font-mono);
  font-size: var(--qlx-text-xs);
}

.cond-block__trial {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  flex-wrap: wrap;
}

.cond-block__error {
  font-size: var(--qlx-text-xs);
  color: var(--qlx-danger);
}

.cond-block__detail {
  font-size: var(--qlx-text-xs);
  color: var(--qlx-ink-500);
}

@media (max-width: 1280px) {
  .cond-builder {
    grid-template-columns: 1fr;
  }
}
</style>
