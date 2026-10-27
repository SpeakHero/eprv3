/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：CSV 导出纯函数单元测试——RFC 4180 转义（逗号 / 双引号 / 换行）、空值、UTF-8 BOM
 */

import { describe, expect, it } from 'vitest'
import { buildCsv, escapeCsvCell } from '../csv-export'

describe('escapeCsvCell', () => {
  it('普通单元格原样输出', () => {
    expect(escapeCsvCell('库存台账')).toBe('库存台账')
    expect(escapeCsvCell(123)).toBe('123')
    expect(escapeCsvCell(1.5)).toBe('1.5')
  })

  it('空值输出空串', () => {
    expect(escapeCsvCell(null)).toBe('')
    expect(escapeCsvCell(undefined)).toBe('')
    expect(escapeCsvCell('')).toBe('')
  })

  it('含逗号 / 双引号 / 换行的单元格整体加引号并翻倍内部引号', () => {
    expect(escapeCsvCell('成都,一号仓')).toBe('"成都,一号仓"')
    expect(escapeCsvCell('备注"特殊"')).toBe('"备注""特殊"""')
    expect(escapeCsvCell('第一行\n第二行')).toBe('"第一行\n第二行"')
    expect(escapeCsvCell('回车\r换行')).toBe('"回车\r换行"')
  })
})

describe('buildCsv', () => {
  interface Row {
    name: string
    qty: number | null
    note: string | null
  }

  const columns = [
    { title: '商品名称', value: (row: Row) => row.name },
    { title: '数量', value: (row: Row) => row.qty },
    { title: '备注', value: (row: Row) => row.note },
  ]

  it('首部写入 UTF-8 BOM，表头与行以 CRLF 分隔', () => {
    const csv = buildCsv(columns, [{ name: '螺栓', qty: 10, note: '正常' }])
    expect(csv.startsWith('\uFEFF')).toBe(true)
    expect(csv).toBe('\uFEFF商品名称,数量,备注\r\n螺栓,10,正常')
  })

  it('空值列输出空串，特殊字符按 RFC 4180 转义', () => {
    const csv = buildCsv(columns, [
      { name: '螺母,加长', qty: null, note: null },
      { name: '垫片"厚"', qty: 2.5, note: '第一行\n第二行' },
    ])
    expect(csv).toBe(
      '\uFEFF商品名称,数量,备注\r\n' +
        '"螺母,加长",,\r\n' +
        '"垫片""厚""",2.5,"第一行\n第二行"',
    )
  })

  it('无数据行时仅输出 BOM 与表头', () => {
    expect(buildCsv(columns, [])).toBe('\uFEFF商品名称,数量,备注')
  })
})
