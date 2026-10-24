/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：cron-builder 预览引擎单测——与后端 Qlx.BuildingBlocks/Jobs/SixFieldCronSchedule.cs 同语义断言
 * 说明：触发时刻按 UTC 语义计算（后端 NextOccurrence 以 DateTimeOffset.UtcNow 推进），断言用 Date.UTC 期望值。
 *       基准时刻：2026-10-22 10:30:15 UTC（星期四）。
 */

import { describe, expect, it } from 'vitest'
import {
  buildExpression,
  compileExpression,
  nextOccurrenceMs,
  nextOccurrences,
  parseExpression,
  segmentToText,
} from '../cron-core'
import { CRON_FIELD_DEFS, defaultSpecs } from '../cron-core'

/** 基准：2026-10-22T10:30:15Z（周四）。 */
const BASE = Date.UTC(2026, 9, 22, 10, 30, 15)
const at = (month: number, day: number, hour = 0, minute = 0, second = 0): number =>
  Date.UTC(2026, month - 1, day, hour, minute, second)

describe('cron 未来 5 次（逐次）预览与 SixFieldCronSchedule 期望值一致', () => {
  it('每天 03:00（0 0 3 * * *）：下一次为次日 03:00', () => {
    const results = nextOccurrences('0 0 3 * * *', BASE, 3)
    expect(results).toEqual([at(10, 23, 3), at(10, 24, 3), at(10, 25, 3)])
  })

  it('每 30 秒（*/30 * * * * *）：秒值集 {0,30}，跨分回卷', () => {
    const results = nextOccurrences('*/30 * * * * *', BASE, 3)
    expect(results).toEqual([
      Date.UTC(2026, 9, 22, 10, 30, 30),
      Date.UTC(2026, 9, 22, 10, 31, 0),
      Date.UTC(2026, 9, 22, 10, 31, 30),
    ])
  })

  it('每周一 09:00（0 0 9 ? * 1）：跨周推进', () => {
    const results = nextOccurrences('0 0 9 ? * 1', BASE, 2)
    expect(results).toEqual([at(10, 26, 9), at(11, 2, 9)])
  })

  it('每月 1 日 00:00（0 0 0 1 * *）', () => {
    const results = nextOccurrences('0 0 0 1 * *', BASE, 2)
    expect(results).toEqual([at(11, 1), at(12, 1)])
  })

  it('列表日 1,15 08:30（0 30 8 1,15 * *）', () => {
    const results = nextOccurrences('0 30 8 1,15 * *', BASE, 2)
    expect(results).toEqual([at(11, 1, 8, 30), at(11, 15, 8, 30)])
  })

  it('日/周同时受限按「或」（0 0 0 1 * 1）：1 日或周一均触发', () => {
    // 基准周四 10-22 → 下一匹配：周一 10-26（dow=1），随后 11-01（dom=1 且周日）、11-02（周一）
    const results = nextOccurrences('0 0 0 1 * 1', BASE, 3)
    expect(results).toEqual([at(10, 26), at(11, 1), at(11, 2)])
  })

  it('周段 7 亦表示周日（0 0 12 * * 7）：10-25 为周日', () => {
    const results = nextOccurrences('0 0 12 * * 7', BASE, 2)
    expect(results).toEqual([at(10, 25, 12), at(11, 1, 12)])
  })

  it('年段约束（0 0 0 1 1 * 2027）：跳至 2027-01-01；区间年段可跨年取值', () => {
    expect(nextOccurrences('0 0 0 1 1 * 2027', BASE, 1)).toEqual([Date.UTC(2027, 0, 1)])
    expect(nextOccurrences('0 0 0 1 1 * 2027-2029', BASE, 2)).toEqual([
      Date.UTC(2027, 0, 1),
      Date.UTC(2028, 0, 1),
    ])
  })

  it('a/n 步进到上限语义（0 0/15 * * * *）：分钟 0,15,30,45', () => {
    const results = nextOccurrences('0 0/15 * * * *', BASE, 3)
    expect(results).toEqual([
      Date.UTC(2026, 9, 22, 10, 45, 0),
      Date.UTC(2026, 9, 22, 11, 0, 0),
      Date.UTC(2026, 9, 22, 11, 15, 0),
    ])
  })

  it('未来 5 次完整预览（0 30 2 1 * *）：count=5 逐次递推', () => {
    const results = nextOccurrences('0 30 2 1 * *', BASE, 5)
    expect(results).toEqual([
      at(11, 1, 2, 30),
      at(12, 1, 2, 30),
      Date.UTC(2027, 0, 1, 2, 30),
      Date.UTC(2027, 1, 1, 2, 30),
      Date.UTC(2027, 2, 1, 2, 30),
    ])
  })
})

describe('cron 表达式构建 / 解析 / 校验', () => {
  it('默认分段 → 每日 0 点表达式（6 段，年省略）', () => {
    expect(buildExpression(defaultSpecs())).toBe('0 0 0 ? * ?')
  })

  it('构建 → 解析 → 构建 往返稳定', () => {
    const specs = parseExpression('0 30 8 1,15 * 1-5 2027').specs!
    expect(buildExpression(specs)).toBe('0 30 8 1,15 * 1-5 2027')
  })

  it('分段片段渲染：日段 ? 保留、非法段回退 *', () => {
    expect(segmentToText({ mode: 'unspecified' }, CRON_FIELD_DEFS[3])).toBe('?')
    expect(segmentToText({ mode: 'step', start: 0, stepSize: 30 }, CRON_FIELD_DEFS[1])).toBe('0/30')
  })

  it('非法表达式被拒绝：段数不足 / 越界 / 区间倒挂 / 非法 ?', () => {
    expect(parseExpression('0 0 3 * *').ok).toBe(false)
    expect(parseExpression('61 0 3 * * *').ok).toBe(false)
    expect(parseExpression('0 0 3 32 * *').ok).toBe(false)
    expect(parseExpression('0 0 3 20-5 * *').ok).toBe(false)
    expect(parseExpression('? 0 3 * * *').ok).toBe(false)
  })

  it('compileExpression 预编译后可直接求下一次触发（与字符串入口一致）', () => {
    const compiled = compileExpression('0 0 3 * * *')
    expect(nextOccurrenceMs(compiled, BASE)).toBe(at(10, 23, 3))
  })

  it('年段过窄时抛出无匹配错误（对齐引擎 5 年视野）', () => {
    expect(() => nextOccurrences('0 0 0 1 1 * 2099', BASE, 2)).toThrow(/5 年内无匹配/)
  })
})
