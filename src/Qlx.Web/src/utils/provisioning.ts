/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：租户数据库预配进度展示助手（步骤中文映射 / 百分比夹紧 / 完成判定）
 * 说明：纯函数，供门户租户详情页与单元测试复用；映射字典集中维护，禁止页面内散落硬编码。
 */

/** 预配进度 SSE 事件载荷（TenantProvisioningProgress，camelCase JSON）。 */
export interface TenantProvisioningProgressPayload {
  tenantId?: number
  step?: string
  progress?: number
  status?: string
}

/** 预配步骤中文映射。 */
export const provisioningStepLabels: Record<string, string> = {
  Migrating: '数据库迁移中',
  Completed: '预配完成',
  Failed: '预配失败',
  MaxRetryReached: '已达重试上限',
}

/** 步骤中文标签（未知 / 空返回「等待预配进度」）。 */
export function provisioningStepLabel(step?: string): string {
  if (!step) {
    return '等待预配进度'
  }
  return provisioningStepLabels[step] ?? '等待预配进度'
}

/** 百分比夹紧：非有限数字归零，四舍五入后限制在 0..100。 */
export function clampProvisioningPercent(progress?: number): number {
  if (typeof progress !== 'number' || !Number.isFinite(progress)) {
    return 0
  }
  return Math.min(100, Math.max(0, Math.round(progress)))
}

/** 预配是否已结束（成功激活或失败）。 */
export function isProvisioningFinished(status?: string): boolean {
  return status === 'Active' || status === 'Failed'
}
