/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：通用 CSV 导出纯函数——列定义 + 行数据 → 带 UTF-8 BOM 的 CSV 字符串并触发浏览器下载
 * 说明：
 *   1. 逗号 / 双引号 / 回车换行按 RFC 4180 转义（含特殊字符的单元格整体加引号，内部引号翻倍）。
 *   2. 首列前写 UTF-8 BOM，保证 Excel 正确识别中文。
 *   3. 仅用于界面导出（B-4），不改变任何业务值。
 */

import { triggerDownload } from '@/utils/bas-master-mappers'

/** CSV 列定义（title 为表头文案，value 取原始展示值）。 */
export interface CsvColumn<TRow> {
  title: string
  value: (row: TRow) => string | number | null | undefined
}

/** 单单元格转义：空值为空串；含逗号 / 引号 / 换行时加引号并把内部引号翻倍。 */
export function escapeCsvCell(value: string | number | null | undefined): string {
  const text = value === null || value === undefined ? '' : String(value)
  if (/[",\r\n]/.test(text)) {
    return `"${text.replace(/"/g, '""')}"`
  }
  return text
}

/** 列定义 + 行数据 → CSV 文本（首部 UTF-8 BOM，行尾 CRLF）。 */
export function buildCsv<TRow>(columns: Array<CsvColumn<TRow>>, rows: readonly TRow[]): string {
  const header = columns.map((column) => escapeCsvCell(column.title)).join(',')
  const body = rows.map((row) =>
    columns.map((column) => escapeCsvCell(column.value(row))).join(','),
  )
  return `\uFEFF${[header, ...body].join('\r\n')}`
}

/** 触发 CSV 下载（自动补 .csv 后缀）。 */
export function downloadCsv(fileName: string, content: string): void {
  const name = fileName.toLowerCase().endsWith('.csv') ? fileName : `${fileName}.csv`
  triggerDownload(new Blob([content], { type: 'text/csv;charset=utf-8' }), name)
}

/** 一步导出：构建 CSV 并触发下载。 */
export function exportCsv<TRow>(
  fileName: string,
  columns: Array<CsvColumn<TRow>>,
  rows: readonly TRow[],
): void {
  downloadCsv(fileName, buildCsv(columns, rows))
}
