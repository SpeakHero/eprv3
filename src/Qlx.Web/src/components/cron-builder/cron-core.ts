/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：六段式 CRON 纯逻辑——表达式构建 / 解析校验 / 未来 N 次预览（本地计算）
 * 语义：与后端 Qlx.BuildingBlocks/Jobs/SixFieldCronSchedule.cs 完全一致——
 *       支持 * ? - / ,；a/n 语义为「从 a 起以步长 n 到字段上限」；
 *       日（DOM）与周（DOW）同时受限按「或」匹配；周 0 与 7 均表示周日；
 *       触发时刻按 UTC 逐秒推进搜索（5 年视野），预览展示本地时间。
 */

export type CronSegmentMode =
  | 'every' // *
  | 'unspecified' // ?（仅日 / 周 / 年）
  | 'value' // 单值
  | 'range' // a-b
  | 'step' // a/n（a 为 * 时即 */n）
  | 'list' // a,b,c
  | 'none' // 年段省略（6 段式）

/** 单段可视化配置。 */
export interface CronFieldSpec {
  mode: CronSegmentMode
  value?: number
  start?: number
  end?: number
  stepSize?: number
  values?: number[]
}

/** 段定义（范围与 ? 支持对齐后端）。 */
export interface CronFieldDef {
  key: 'second' | 'minute' | 'hour' | 'day' | 'month' | 'dayOfWeek' | 'year'
  label: string
  min: number
  max: number
  allowUnspecified: boolean
  /** 周段显示名。 */
  valueNames?: Record<number, string>
}

export const CRON_FIELD_DEFS: CronFieldDef[] = [
  { key: 'second', label: '秒', min: 0, max: 59, allowUnspecified: false },
  { key: 'minute', label: '分', min: 0, max: 59, allowUnspecified: false },
  { key: 'hour', label: '时', min: 0, max: 23, allowUnspecified: false },
  { key: 'day', label: '日', min: 1, max: 31, allowUnspecified: true },
  { key: 'month', label: '月', min: 1, max: 12, allowUnspecified: false },
  {
    key: 'dayOfWeek',
    label: '周',
    min: 0,
    max: 7,
    allowUnspecified: true,
    valueNames: { 0: '周日', 1: '周一', 2: '周二', 3: '周三', 4: '周四', 5: '周五', 6: '周六', 7: '周日' },
  },
  { key: 'year', label: '年', min: 1970, max: 2099, allowUnspecified: true },
]

/** 默认分段（每天 0 点，年段省略）。 */
export function defaultSpecs(): CronFieldSpec[] {
  return [
    { mode: 'value', value: 0 },
    { mode: 'value', value: 0 },
    { mode: 'value', value: 0 },
    { mode: 'unspecified' },
    { mode: 'every' },
    { mode: 'unspecified' },
    { mode: 'none' },
  ]
}

/** 分段 → 表达式（年段 none 时输出 6 段）。 */
export function buildExpression(specs: CronFieldSpec[]): string {
  const parts = specs.slice(0, 6).map((spec, index) => segmentToText(spec, CRON_FIELD_DEFS[index]))
  if (specs[6] && specs[6].mode !== 'none') {
    parts.push(segmentToText(specs[6], CRON_FIELD_DEFS[6]))
  }
  return parts.join(' ')
}

/** 分段 → 表达式片段（单段；用于摘要展示）。 */
export function segmentToText(spec: CronFieldSpec, def: CronFieldDef): string {
  switch (spec.mode) {
    case 'every':
      return '*'
    case 'unspecified':
      return '?'
    case 'value':
      return String(spec.value ?? def.min)
    case 'range':
      return `${spec.start ?? def.min}-${spec.end ?? def.max}`
    case 'step': {
      const start = spec.start ?? def.min
      return `${start}/${spec.stepSize ?? 1}`
    }
    case 'list':
      return (spec.values ?? []).join(',')
    default:
      return '*'
  }
}

/** 解析结果。 */
export interface CronParseResult {
  ok: boolean
  message?: string
  specs?: CronFieldSpec[]
}

/** 表达式 → 分段配置（供原始表达式切换回可视化）；失败时携带段定位消息。 */
export function parseExpression(expression: string): CronParseResult {
  const parts = expression.trim().split(/\s+/)
  if (parts.length < 6 || parts.length > 7) {
    return { ok: false, message: `表达式必须为 6 段（秒 分 时 日 月 周）或 7 段（追加年），实际 ${parts.length} 段` }
  }
  const specs: CronFieldSpec[] = []
  for (let index = 0; index < 6; index++) {
    const result = parseSegment(parts[index], CRON_FIELD_DEFS[index])
    if (!result.ok) {
      return { ok: false, message: `第 ${index + 1} 段（${CRON_FIELD_DEFS[index].label}）：${result.message}` }
    }
    specs.push(result.spec!)
  }
  if (parts.length === 7) {
    const result = parseSegment(parts[6], CRON_FIELD_DEFS[6])
    if (!result.ok) {
      return { ok: false, message: `第 7 段（年）：${result.message}` }
    }
    specs.push(result.spec!)
  } else {
    specs.push({ mode: 'none' })
  }
  return { ok: true, specs }
}

function parseSegment(text: string, def: CronFieldDef): { ok: boolean; message?: string; spec?: CronFieldSpec } {
  if (text === '*') {
    return { ok: true, spec: { mode: 'every' } }
  }
  if (text === '?') {
    if (!def.allowUnspecified) {
      return { ok: false, message: '该段不支持 ? 占位' }
    }
    return { ok: true, spec: { mode: 'unspecified' } }
  }
  if (text.includes(',')) {
    const values: number[] = []
    for (const piece of text.split(',')) {
      const parsed = parseValue(piece.trim(), def)
      if (parsed === null) {
        return { ok: false, message: `取值「${piece}」超出范围 [${def.min}, ${def.max}] 或非法` }
      }
      values.push(parsed)
    }
    return { ok: true, spec: { mode: 'list', values } }
  }
  const slashIndex = text.indexOf('/')
  const rangePart = slashIndex >= 0 ? text.slice(0, slashIndex) : text
  if (slashIndex >= 0) {
    const stepSize = Number(text.slice(slashIndex + 1))
    if (!Number.isInteger(stepSize) || stepSize <= 0) {
      return { ok: false, message: `步长「${text.slice(slashIndex + 1)}」必须为正整数` }
    }
    if (rangePart === '*' || (rangePart === '?' && def.allowUnspecified)) {
      return { ok: true, spec: { mode: 'step', start: def.min, stepSize } }
    }
    const start = parseValue(rangePart, def)
    if (start === null) {
      return { ok: false, message: `起点「${rangePart}」超出范围 [${def.min}, ${def.max}] 或非法` }
    }
    return { ok: true, spec: { mode: 'step', start, stepSize } }
  }
  const dashIndex = rangePart.indexOf('-')
  if (dashIndex >= 0) {
    const start = parseValue(rangePart.slice(0, dashIndex), def)
    const end = parseValue(rangePart.slice(dashIndex + 1), def)
    if (start === null || end === null) {
      return { ok: false, message: `区间「${rangePart}」非法` }
    }
    if (start > end) {
      return { ok: false, message: `区间下界 ${start} 大于上界 ${end}` }
    }
    return { ok: true, spec: { mode: 'range', start, end } }
  }
  const value = parseValue(rangePart, def)
  if (value === null) {
    return { ok: false, message: `取值「${rangePart}」超出范围 [${def.min}, ${def.max}] 或非法` }
  }
  return { ok: true, spec: { mode: 'value', value } }
}

function parseValue(text: string, def: CronFieldDef): number | null {
  if (!/^\d+$/.test(text)) {
    return null
  }
  const value = Number(text)
  if (value < def.min || value > def.max) {
    return null
  }
  return value
}

// ============ 预览引擎（与 SixFieldCronSchedule.NextOccurrence 逐步对齐） ============

interface FieldValues {
  isWildcard: boolean
  values: Set<number>
}

function compileSegment(text: string, def: CronFieldDef): FieldValues {
  if (text === '*' || (text === '?' && def.allowUnspecified)) {
    return { isWildcard: true, values: new Set() }
  }
  const values = new Set<number>()
  for (const token of text.split(',')) {
    compileToken(token.trim(), def, values)
  }
  return { isWildcard: false, values }
}

function compileToken(token: string, def: CronFieldDef, values: Set<number>): void {
  const slashIndex = token.indexOf('/')
  const rangePart = slashIndex >= 0 ? token.slice(0, slashIndex) : token
  let step = 1
  if (slashIndex >= 0) {
    step = Number(token.slice(slashIndex + 1))
    if (!Number.isInteger(step) || step <= 0) {
      throw new Error(`第 ${def.label} 段步长非法`)
    }
  }

  let rangeStart: number
  let rangeEnd: number
  if (rangePart === '*' || (rangePart === '?' && def.allowUnspecified)) {
    rangeStart = def.min
    rangeEnd = def.max
  } else {
    const dashIndex = rangePart.indexOf('-')
    if (dashIndex >= 0) {
      rangeStart = requireValue(rangePart.slice(0, dashIndex), def)
      rangeEnd = requireValue(rangePart.slice(dashIndex + 1), def)
      if (rangeStart > rangeEnd) {
        throw new Error(`第 ${def.label} 段区间下界 ${rangeStart} 大于上界 ${rangeEnd}`)
      }
    } else {
      rangeStart = requireValue(rangePart, def)
      // a/n 语义：从 a 起以步长 n 到字段上限（Quartz 惯例）
      rangeEnd = slashIndex >= 0 ? def.max : rangeStart
    }
  }
  for (let value = rangeStart; value <= rangeEnd; value += step) {
    values.add(value)
  }
}

function requireValue(text: string, def: CronFieldDef): number {
  const value = parseValue(text, def)
  if (value === null) {
    throw new Error(`第 ${def.label} 段取值「${text}」超出范围 [${def.min}, ${def.max}] 或非法`)
  }
  return value
}

function contains(field: FieldValues, value: number): boolean {
  return field.isWildcard || field.values.has(value)
}

/** 解析后的调度计划（内部编译产物）。 */
export interface CompiledCron {
  segments: FieldValues[]
  dayRestricted: boolean
  dayOfWeekRestricted: boolean
}

/** 校验并编译表达式；失败时抛出可读错误。 */
export function compileExpression(expression: string): CompiledCron {
  const parts = expression.trim().split(/\s+/)
  if (parts.length < 6 || parts.length > 7) {
    throw new Error(`表达式必须为 6 段（秒 分 时 日 月 周）或 7 段（追加年），实际 ${parts.length} 段`)
  }
  const effectiveDefs = parts.length === 7 ? CRON_FIELD_DEFS : CRON_FIELD_DEFS.slice(0, 6)
  const segments = parts.map((part, index) => compileSegment(part, effectiveDefs[index]))
  return {
    segments,
    dayRestricted: !segments[3].isWildcard,
    dayOfWeekRestricted: !segments[5].isWildcard,
  }
}

function matchesDay(cron: CompiledCron, year: number, month: number, day: number): boolean {
  const dayMatches = contains(cron.segments[3], day)
  // JS Date UTC getUTCDay：周日=0，与 CRON 0=周日一致；7 亦表示周日
  const dow = new Date(Date.UTC(year, month - 1, day)).getUTCDay()
  const dowMatches = contains(cron.segments[5], dow) || (cron.segments[5].values.has(7) && dow === 0)
  if (cron.dayRestricted && cron.dayOfWeekRestricted) {
    return dayMatches || dowMatches
  }
  if (cron.dayRestricted) {
    return dayMatches
  }
  if (cron.dayOfWeekRestricted) {
    return dowMatches
  }
  return true
}

/** 计算晚于 afterMs（epoch ms）的下一次触发（UTC 语义），返回 epoch ms；无匹配（5 年视野）返回 null。 */
export function nextOccurrenceMs(cron: CompiledCron, afterMs: number): number | null {
  // 截断到秒并 +1s
  let cursor = Math.floor(afterMs / 1000) * 1000 + 1000
  const horizon = cursor + 5 * 366 * 24 * 3600 * 1000

  while (cursor <= horizon) {
    const d = new Date(cursor)
    const year = d.getUTCFullYear()
    const month = d.getUTCMonth() + 1
    const day = d.getUTCDate()
    const hour = d.getUTCHours()
    const minute = d.getUTCMinutes()
    const second = d.getUTCSeconds()

    const yearField = cron.segments[6]
    if (yearField && !yearField.isWildcard && !contains(yearField, year)) {
      cursor = Date.UTC(year + 1, 0, 1, 0, 0, 0)
      continue
    }
    if (!contains(cron.segments[4], month)) {
      cursor = Date.UTC(year, month, 1, 0, 0, 0)
      continue
    }
    if (!matchesDay(cron, year, month, day)) {
      cursor = Date.UTC(year, month - 1, day + 1, 0, 0, 0)
      continue
    }
    if (!contains(cron.segments[2], hour)) {
      cursor = Date.UTC(year, month - 1, day, hour + 1, 0, 0)
      continue
    }
    if (!contains(cron.segments[1], minute)) {
      cursor = Date.UTC(year, month - 1, day, hour, minute + 1, 0)
      continue
    }
    if (contains(cron.segments[0], second)) {
      return cursor
    }
    cursor += 1000
  }
  return null
}

/** 预览未来 count 次触发（UTC epoch ms 列表）；表达式非法时抛错。 */
export function nextOccurrences(expression: string, afterMs: number, count: number): number[] {
  const cron = compileExpression(expression)
  const results: number[] = []
  let cursor = afterMs
  for (let index = 0; index < count; index++) {
    const next = nextOccurrenceMs(cron, cursor)
    if (next === null) {
      throw new Error('CRON 表达式在未来 5 年内无匹配时刻（年段约束或字段组合过窄）。')
    }
    results.push(next)
    cursor = next
  }
  return results
}

/** 快捷预设（键 → 分段配置）。 */
export interface CronPreset {
  key: string
  label: string
  specs: CronFieldSpec[]
}

export const CRON_PRESETS: CronPreset[] = [
  {
    key: 'everyMinute',
    label: '每分钟',
    specs: [
      { mode: 'value', value: 0 },
      { mode: 'every' },
      { mode: 'every' },
      { mode: 'unspecified' },
      { mode: 'every' },
      { mode: 'unspecified' },
      { mode: 'none' },
    ],
  },
  {
    key: 'everyHour',
    label: '每小时',
    specs: [
      { mode: 'value', value: 0 },
      { mode: 'value', value: 0 },
      { mode: 'every' },
      { mode: 'unspecified' },
      { mode: 'every' },
      { mode: 'unspecified' },
      { mode: 'none' },
    ],
  },
  {
    key: 'daily3am',
    label: '每天 03:00',
    specs: [
      { mode: 'value', value: 0 },
      { mode: 'value', value: 0 },
      { mode: 'value', value: 3 },
      { mode: 'unspecified' },
      { mode: 'every' },
      { mode: 'unspecified' },
      { mode: 'none' },
    ],
  },
  {
    key: 'weeklyMonday9am',
    label: '每周一 09:00',
    specs: [
      { mode: 'value', value: 0 },
      { mode: 'value', value: 0 },
      { mode: 'value', value: 9 },
      { mode: 'unspecified' },
      { mode: 'every' },
      { mode: 'value', value: 1 },
      { mode: 'none' },
    ],
  },
  {
    key: 'monthlyFirst',
    label: '每月 1 日 00:00',
    specs: [
      { mode: 'value', value: 0 },
      { mode: 'value', value: 0 },
      { mode: 'value', value: 0 },
      { mode: 'value', value: 1 },
      { mode: 'every' },
      { mode: 'unspecified' },
      { mode: 'none' },
    ],
  },
  {
    key: 'workdayHalfHour',
    label: '工作时段每半小时（08~18 点）',
    specs: [
      { mode: 'value', value: 0 },
      { mode: 'step', start: 0, stepSize: 30 },
      { mode: 'range', start: 8, end: 18 },
      { mode: 'unspecified' },
      { mode: 'every' },
      { mode: 'range', start: 1, end: 5 },
      { mode: 'none' },
    ],
  },
]
