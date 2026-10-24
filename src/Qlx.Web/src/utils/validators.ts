/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：前端表单格式校验（UX 预校验，B-4/V-1）——信用代码 / 手机号 / 邮箱。
 *       仅用于输入即时反馈，不构成安全边界；后端独立校验与 422 字段级错误仍然保留。
 */

/** 18 位统一社会信用代码（GB 32100-2015 允许字符集，排除 I/O/S/V/Z）。 */
export const CREDIT_CODE_PATTERN = /^[0-9A-HJ-NPQRTUWXY]{2}\d{6}[0-9A-HJ-NPQRTUWXY]{10}$/

/** 中国大陆手机号。 */
export const MOBILE_PATTERN = /^1[3-9]\d{9}$/

/** 邮箱（宽松校验：本地部分@域名.后缀，不含空白）。 */
export const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** 管理员账号：手机号或邮箱。 */
export const ADMIN_ACCOUNT_PATTERN = /^(?:1[3-9]\d{9}|[^\s@]+@[^\s@]+\.[^\s@]+)$/

/** 是否为合法统一社会信用代码（空值交由 required 规则处理，此处返回 false）。 */
export function isCreditCode(value: string): boolean {
  return CREDIT_CODE_PATTERN.test(value)
}

/** 是否为合法管理员账号（手机号或邮箱）。 */
export function isAdminAccount(value: string): boolean {
  return ADMIN_ACCOUNT_PATTERN.test(value)
}
