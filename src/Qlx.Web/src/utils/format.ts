/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：通用格式化工具——时间 / 文本等纯函数，无框架依赖
 */

/** ISO 时间 → YYYY-MM-DD HH:mm（空值返回占位符）。 */
export function formatDateTime(iso: string | null | undefined, placeholder = '—'): string {
  if (!iso) {
    return placeholder
  }
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) {
    return iso
  }
  const pad = (value: number): string => String(value).padStart(2, '0')
  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ` +
    `${pad(date.getHours())}:${pad(date.getMinutes())}`
  )
}

/** ISO 时间 → YYYY-MM-DD。 */
export function formatDate(iso: string | null | undefined, placeholder = '—'): string {
  return formatDateTime(iso, placeholder).slice(0, 10)
}

/** 布尔 → 是 / 否。 */
export function yesNo(value: boolean | null | undefined): string {
  return value ? '是' : '否'
}

/**
 * 从枚举标签映射表中安全取标签。
 * 主要解决模板中 el-table row 为 any 时无法直接索引 Record<Code, string> 的类型问题；
 * 同时兜底空值与未知码，避免模板渲染报错。
 * 兼容数字码（如 MessageStatusCode）与字符串码（如 JobStatusName）两种映射。
 */
export function labelOf(
  map: Record<string, string>,
  key: number | string | null | undefined,
  fallback = '—',
): string {
  if (key === null || key === undefined || key === '') {
    return fallback
  }
  const code = typeof key === 'string' ? key : String(key)
  return map[code] ?? fallback
}

/** 字节数 → 人类可读大小（B / KB / MB / GB）。 */
export function formatBytes(bytes: number | null | undefined, fallback = '—'): string {
  if (bytes === null || bytes === undefined || Number.isNaN(bytes)) {
    return fallback
  }
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`
  return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`
}
