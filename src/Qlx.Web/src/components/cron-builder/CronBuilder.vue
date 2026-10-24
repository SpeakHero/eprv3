<!--
  所属公司：四川淇利馨集团有限公司
  描述：CRON 构建器——六段可视化（秒/分/时/日/月/周/年）+ 快捷模式 + 原始表达式切换 + 未来 5 次预览
  语义：与后端 SixFieldCronSchedule 一致（UTC 逐秒搜索；预览展示本地时间）
-->
<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import {
  CRON_FIELD_DEFS,
  CRON_PRESETS,
  buildExpression,
  defaultSpecs,
  nextOccurrences,
  parseExpression,
  segmentToText,
} from './cron-core'
import type { CronFieldSpec, CronSegmentMode } from './cron-core'

const props = defineProps<{ modelValue?: string }>()

const emit = defineEmits<{ 'update:modelValue': [value: string] }>()

const specs = ref<CronFieldSpec[]>(defaultSpecs())
const activeTab = ref('segments')
const rawText = ref('')
const rawError = ref('')

const expression = computed(() => buildExpression(specs.value))

const modeLabels: Record<CronSegmentMode, string> = {
  every: '任意值（*）',
  unspecified: '不指定（?）',
  value: '指定值',
  range: '区间（a-b）',
  step: '步长（a/n）',
  list: '列表（a,b,c）',
  none: '省略年段',
}

function modeOptions(defIndex: number): Array<{ label: string; value: CronSegmentMode }> {
  const def = CRON_FIELD_DEFS[defIndex]
  const options: Array<{ label: string; value: CronSegmentMode }> = [
    { label: modeLabels.every, value: 'every' },
    { label: modeLabels.value, value: 'value' },
    { label: modeLabels.range, value: 'range' },
    { label: modeLabels.step, value: 'step' },
    { label: modeLabels.list, value: 'list' },
  ]
  if (def.allowUnspecified) {
    options.splice(1, 0, { label: modeLabels.unspecified, value: 'unspecified' })
  }
  if (def.key === 'year') {
    options.unshift({ label: modeLabels.none, value: 'none' })
  }
  return options
}

/** 切换模式时初始化该模式的缺省配置。 */
function onModeChange(spec: CronFieldSpec, defIndex: number): void {
  const def = CRON_FIELD_DEFS[defIndex]
  spec.value = def.min
  spec.start = def.min
  spec.end = def.max
  spec.stepSize = spec.mode === 'step' ? 5 : 1
  spec.values = spec.mode === 'list' ? [def.min] : undefined
}

function valueOptions(defIndex: number): number[] {
  const def = CRON_FIELD_DEFS[defIndex]
  return Array.from({ length: def.max - def.min + 1 }, (_, index) => def.min + index)
}

function valueLabel(defIndex: number, value: number): string {
  const names = CRON_FIELD_DEFS[defIndex].valueNames
  return names ? `${value}（${names[value] ?? value}）` : String(value)
}

function applyPreset(index: number): void {
  specs.value = CRON_PRESETS[index].specs.map((spec) => ({ ...spec }))
}

function applyRaw(): void {
  const result = parseExpression(rawText.value)
  if (!result.ok) {
    rawError.value = result.message ?? '表达式非法'
    return
  }
  rawError.value = ''
  specs.value = result.specs!.map((spec) => ({ ...spec }))
}

// ---- 未来 5 次预览（本地计算，UTC 语义 + 本地时间展示） ----
const previewBase = ref(Date.now())
const previewError = ref('')
const previewList = ref<number[]>([])

function refreshPreview(): void {
  try {
    previewList.value = nextOccurrences(expression.value, previewBase.value, 5)
    previewError.value = ''
  } catch (error) {
    previewList.value = []
    previewError.value = error instanceof Error ? error.message : '预览计算失败'
  }
}

watch(expression, refreshPreview, { immediate: true })
watch(activeTab, (tab) => {
  if (tab === 'raw') {
    rawText.value = expression.value
    rawError.value = ''
  }
  if (tab === 'segments') {
    // 从原始表达式切回：已解析的 specs 即时生效
  }
})

watch(expression, (value) => emit('update:modelValue', value), { immediate: true })
if (props.modelValue) {
  const parsed = parseExpression(props.modelValue)
  if (parsed.ok) {
    specs.value = parsed.specs!
  }
}

function formatLocal(ms: number): string {
  const date = new Date(ms)
  const pad = (value: number): string => String(value).padStart(2, '0')
  const weekdays = ['日', '一', '二', '三', '四', '五', '六']
  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ` +
    `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())} ` +
    `周${weekdays[date.getDay()]}`
  )
}

const segmentSummary = computed(() =>
  CRON_FIELD_DEFS.map((def, index) => {
    const spec = specs.value[index]
    if (!spec || spec.mode === 'none') {
      return null
    }
    return { label: def.label, text: segmentToDisplayText(spec, def) }
  }).filter((item): item is { label: string; text: string } => item !== null),
)

function segmentToDisplayText(spec: CronFieldSpec, def: (typeof CRON_FIELD_DEFS)[number]): string {
  const text = segmentToText(spec, def)
  return text === '?' && !def.allowUnspecified ? '*' : text
}
</script>

<template>
  <div class="cron-builder">
    <div class="cron-builder__head">
      <span class="cron-builder__expr">{{ expression }}</span>
      <el-tag v-if="!previewError" type="success" size="small">表达式合法</el-tag>
      <el-tag v-else type="danger" size="small">表达式非法</el-tag>
    </div>
    <p v-if="previewError" class="cron-builder__error">{{ previewError }}</p>

    <el-tabs v-model="activeTab" class="cron-builder__tabs">
      <el-tab-pane label="分段构建" name="segments">
        <div class="cron-segments">
          <div v-for="(def, index) in CRON_FIELD_DEFS" :key="def.key" class="cron-segment">
            <div class="cron-segment__title">
              {{ def.label }}
              <span class="cron-segment__range">[{{ def.min }} ~ {{ def.max }}]</span>
            </div>
            <template v-if="specs[index] && specs[index].mode !== 'none'">
              <el-select
                v-model="specs[index].mode"
                size="small"
                class="cron-segment__mode"
                @change="onModeChange(specs[index], index)"
              >
                <el-option
                  v-for="option in modeOptions(index)"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>

              <div class="cron-segment__editor">
                <template v-if="specs[index].mode === 'value'">
                  <el-select v-model="specs[index].value" size="small" filterable>
                    <el-option
                      v-for="value in valueOptions(index)"
                      :key="value"
                      :label="valueLabel(index, value)"
                      :value="value"
                    />
                  </el-select>
                </template>

                <template v-else-if="specs[index].mode === 'range'">
                  <el-input-number
                    v-model="specs[index].start"
                    size="small"
                    :min="def.min"
                    :max="def.max"
                    :controls="false"
                    class="cron-segment__num"
                  />
                  <span class="cron-segment__dash">至</span>
                  <el-input-number
                    v-model="specs[index].end"
                    size="small"
                    :min="def.min"
                    :max="def.max"
                    :controls="false"
                    class="cron-segment__num"
                  />
                </template>

                <template v-else-if="specs[index].mode === 'step'">
                  <span class="cron-segment__dash">从</span>
                  <el-input-number
                    v-model="specs[index].start"
                    size="small"
                    :min="def.min"
                    :max="def.max"
                    :controls="false"
                    class="cron-segment__num"
                  />
                  <span class="cron-segment__dash">起每</span>
                  <el-input-number
                    v-model="specs[index].stepSize"
                    size="small"
                    :min="1"
                    :max="def.max"
                    :controls="false"
                    class="cron-segment__num"
                  />
                  <span class="cron-segment__dash">{{ def.label }}</span>
                </template>

                <template v-else-if="specs[index].mode === 'list'">
                  <el-select v-model="specs[index].values" size="small" multiple collapse-tags filterable>
                    <el-option
                      v-for="value in valueOptions(index)"
                      :key="value"
                      :label="valueLabel(index, value)"
                      :value="value"
                    />
                  </el-select>
                </template>

                <span v-else class="cron-segment__plain">
                  {{ specs[index].mode === 'every' ? '任意值都匹配' : '不指定（由对方段约束）' }}
                </span>
              </div>
            </template>
            <span v-else class="cron-segment__plain">年段省略，输出 6 段表达式</span>
          </div>
        </div>
        <div class="cron-builder__summary">
          <span v-for="item in segmentSummary" :key="item.label" class="cron-builder__summary-item">
            {{ item.label }}：<b>{{ item.text }}</b>
          </span>
        </div>
      </el-tab-pane>

      <el-tab-pane label="快捷模式" name="presets">
        <div class="cron-presets">
          <el-button
            v-for="(preset, index) in CRON_PRESETS"
            :key="preset.key"
            size="small"
            @click="applyPreset(index)"
          >
            {{ preset.label }}
          </el-button>
        </div>
        <p class="cron-builder__hint">
          选择预设后切到「分段构建」微调；预览区即时刷新。
        </p>
      </el-tab-pane>

      <el-tab-pane label="原始表达式" name="raw">
        <div class="cron-raw">
          <el-input v-model="rawText" placeholder="如：0 0 3 * * ? 或 0 0 3 1 1 ? 2027" class="cron-raw__input">
            <template #prepend>CRON</template>
          </el-input>
          <el-button type="primary" @click="applyRaw">解析并应用</el-button>
        </div>
        <p class="cron-builder__hint">
          支持 6~7 段；通配符 *、占位 ?（仅日/周/年）、区间 -、步长 /、列表 ,；a/n 语义为从 a 起步进到上限。
        </p>
        <el-alert v-if="rawError" :title="rawError" type="error" :closable="false" />
      </el-tab-pane>
    </el-tabs>

    <div class="cron-preview">
      <div class="cron-preview__title">
        <AppIcon name="Timer" :size="14" />
        未来 5 次触发预览（本地时间）
      </div>
      <ol v-if="previewList.length" class="cron-preview__list">
        <li v-for="(ms, index) in previewList" :key="ms" class="cron-preview__item">
          <span class="cron-preview__index">{{ index + 1 }}</span>
          <span class="cron-preview__time">{{ formatLocal(ms) }}</span>
        </li>
      </ol>
      <p v-else class="cron-builder__hint">暂无可预览结果。</p>
    </div>
  </div>
</template>

<style scoped>
.cron-builder {
  background: var(--qlx-surface);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-lg);
  padding: var(--qlx-space-4);
  display: grid;
  gap: var(--qlx-space-3);
}

.cron-builder__head {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-3);
}

.cron-builder__expr {
  font-family: var(--qlx-font-mono);
  font-size: var(--qlx-text-md);
  color: var(--qlx-brand-800);
  background: var(--qlx-brand-50);
  border: 1px solid var(--qlx-brand-100);
  border-radius: var(--qlx-radius-sm);
  padding: var(--qlx-space-1) var(--qlx-space-3);
  letter-spacing: 0.08em;
}

.cron-builder__error {
  margin: 0;
  font-size: var(--qlx-text-sm);
  color: var(--qlx-danger);
}

.cron-segments {
  display: grid;
  gap: var(--qlx-space-2);
}

.cron-segment {
  display: grid;
  grid-template-columns: 96px 170px 1fr;
  align-items: center;
  gap: var(--qlx-space-3);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-sm);
  padding: var(--qlx-space-2) var(--qlx-space-3);
  background: var(--qlx-canvas);
}

.cron-segment__title {
  font-weight: 600;
  color: var(--qlx-ink-800);
}

.cron-segment__range {
  display: block;
  font-size: var(--qlx-text-xs);
  font-weight: 400;
  color: var(--qlx-ink-400);
  font-family: var(--qlx-font-mono);
}

.cron-segment__editor {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  flex-wrap: wrap;
  min-width: 0;
}

.cron-segment__num {
  width: 84px;
}

.cron-segment__dash {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-sm);
}

.cron-segment__plain {
  font-size: var(--qlx-text-sm);
  color: var(--qlx-ink-400);
}

.cron-builder__summary {
  display: flex;
  flex-wrap: wrap;
  gap: var(--qlx-space-2) var(--qlx-space-4);
  font-size: var(--qlx-text-xs);
  color: var(--qlx-ink-500);
}

.cron-builder__summary-item b {
  font-family: var(--qlx-font-mono);
  color: var(--qlx-brand-700);
}

.cron-presets {
  display: flex;
  flex-wrap: wrap;
  gap: var(--qlx-space-2);
}

.cron-raw {
  display: flex;
  gap: var(--qlx-space-2);
}

.cron-raw__input {
  max-width: 420px;
}

.cron-builder__hint {
  margin: 0;
  font-size: var(--qlx-text-xs);
  color: var(--qlx-ink-400);
  line-height: 1.7;
}

.cron-preview {
  border-top: 1px solid var(--qlx-line);
  padding-top: var(--qlx-space-3);
}

.cron-preview__title {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-1);
  font-size: var(--qlx-text-sm);
  font-weight: 600;
  color: var(--qlx-brand-800);
  margin-bottom: var(--qlx-space-2);
}

.cron-preview__list {
  margin: 0;
  padding: 0;
  list-style: none;
  display: grid;
  gap: var(--qlx-space-1);
}

.cron-preview__item {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  font-size: var(--qlx-text-sm);
}

.cron-preview__index {
  width: 20px;
  height: 20px;
  border-radius: var(--qlx-radius-full);
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-700);
  font-size: var(--qlx-text-xs);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: none;
}

.cron-preview__time {
  font-family: var(--qlx-font-numeric);
  color: var(--qlx-ink-800);
}

@media (max-width: 900px) {
  .cron-segment {
    grid-template-columns: 1fr;
  }
}
</style>
