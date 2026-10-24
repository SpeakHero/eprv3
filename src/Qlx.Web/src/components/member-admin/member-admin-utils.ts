/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：成员组件族纯函数——行状态→可用操作集收敛 / 批量导入行映射（可单测，无框架依赖）
 * 说明：承接原 TenantMemberView.vue 操作列模板分支（管理员保护 / 待激活分支 / 停用态禁重置）。
 */

import type { ImportRow, MemberItem } from '@/types/org-admin'

/** 成员行可用操作（组件按集合渲染按钮，页面按操作名接线）。 */
export type MemberAction = 'detail' | 'toggle' | 'reset' | 'remove' | 'resend'

/**
 * 行状态 → 可用操作集：
 * - detail：恒可用；
 * - resend：仅待激活（PendingInvite）；
 * - toggle：非待激活且非企业管理员（管理员不可在租户内停用）；
 * - reset：非待激活且当前为启用态（停用成员不发送重置指引）；
 * - remove：非企业管理员（管理员保护，后端 409 同规则）。
 */
export function availableMemberActions(row: MemberItem): MemberAction[] {
  const actions: MemberAction[] = ['detail']
  if (row.memberRole === 'PendingInvite') {
    actions.push('resend')
  } else {
    if (row.memberRole !== 'TenantAdmin') {
      actions.push('toggle')
    }
    if (row.memberStatus === 'Enabled') {
      actions.push('reset')
    }
  }
  if (row.memberRole !== 'TenantAdmin') {
    actions.push('remove')
  }
  return actions
}

/** QlxImportDialog 解析行（Record<string,string>）→ 批量导入入参（保留原始行号，缺省按顺序补齐）。 */
export function mapImportRows(rows: Array<Record<string, string>>): ImportRow[] {
  return rows.map((row, index) => ({
    line: Number(row.line) || index + 1,
    displayName: row.displayName ?? '',
    account: row.account ?? '',
    orgName: row.orgName ?? '',
    roleName: row.roleName ?? '',
  }))
}
