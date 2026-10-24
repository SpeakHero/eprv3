/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：member-admin-utils 单元测试——行状态操作集收敛 / 导入行映射
 */

import { describe, expect, it } from 'vitest'
import type { MemberItem } from '@/types/org-admin'
import { availableMemberActions, mapImportRows } from '../member-admin-utils'

function member(partial: Partial<MemberItem>): MemberItem {
  return {
    id: 'm1',
    portalUserId: 'u1',
    loginAccount: '13800000000',
    displayName: '张三',
    phone: '',
    email: '',
    orgId: 'o1',
    roleCodes: [],
    memberRole: 'OrdinaryMember',
    memberStatus: 'Enabled',
    joinAt: '2026-10-23',
    lastLoginAt: null,
    ...partial,
  }
}

describe('availableMemberActions', () => {
  it('普通启用成员：详情 / 启停 / 重置 / 移除', () => {
    expect(availableMemberActions(member({}))).toEqual(['detail', 'toggle', 'reset', 'remove'])
  })

  it('停用成员：不再提供重置密码', () => {
    const actions = availableMemberActions(member({ memberStatus: 'Disabled' }))
    expect(actions).toContain('toggle')
    expect(actions).not.toContain('reset')
    expect(actions).toContain('remove')
  })

  it('企业管理员：禁启停、禁移除（管理员保护）', () => {
    const actions = availableMemberActions(member({ memberRole: 'TenantAdmin' }))
    expect(actions).toEqual(['detail', 'reset'])
  })

  it('待激活成员：显示重发邀请，不提供启停 / 重置', () => {
    const actions = availableMemberActions(member({ memberRole: 'PendingInvite' }))
    expect(actions).toEqual(['detail', 'resend', 'remove'])
  })
})

describe('mapImportRows', () => {
  it('行号从 1 起，缺失键兜底为空串', () => {
    const rows = mapImportRows([
      { displayName: '张三', account: '13800000019', orgName: '采购部', roleName: '采购专员' },
      { displayName: '李四' },
    ])
    expect(rows).toEqual([
      { line: 1, displayName: '张三', account: '13800000019', orgName: '采购部', roleName: '采购专员' },
      { line: 2, displayName: '李四', account: '', orgName: '', roleName: '' },
    ])
  })

  it('空输入返回空数组', () => {
    expect(mapImportRows([])).toEqual([])
  })
})
