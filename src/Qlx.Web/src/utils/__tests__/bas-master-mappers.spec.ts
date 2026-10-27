/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：基础信息中心展示层纯函数单元测试——树组装 / 枚举映射 / 金额日期格式化 / 脱敏 / 快照摘要 / 行键补齐
 */

import { describe, expect, it } from 'vitest'
import type { ChangeLogRow, CodeRuleRow, ItemCategoryRow } from '@/types/bas-master'
import {
  buildCategoryOptions,
  buildCategoryTree,
  changeLogEntityLabel,
  changeLogOperationLabel,
  codeRuleBizTypeLabel,
  collectCategoryIds,
  creditGradeLabel,
  creditGradeTone,
  employStatusLabel,
  employStatusTone,
  findCategoryNode,
  formatAmount,
  formatDateOnly,
  formatEffectPeriod,
  formatPercent,
  formatRate,
  formatSegments,
  idTypeLabel,
  labelOfCode,
  maskBankCard,
  maskIdNo,
  maskMobile,
  mapOrgAdjustApprovals,
  ORG_ADJUST_BIZ_TYPE,
  partnerStatusLabel,
  partnerStatusTone,
  partnerTypeLabel,
  personTypeLabel,
  prettyJson,
  summarizeOrgSnapshot,
  supplierGradeLabel,
  supplierGradeTone,
  toChangeLogRows,
  toCodeRuleRows,
  toNumberOptions,
  toStringOptions,
} from '../bas-master-mappers'

function category(
  id: string,
  parentId: string | null,
  code: string,
  name: string,
): ItemCategoryRow {
  return {
    id,
    parentId,
    code,
    name,
    levelNo: code.length / 2,
    unitGroupId: null,
    status: 1,
    updatedAt: '2026-10-25T08:00:00+08:00',
  }
}

const flatCategories: ItemCategoryRow[] = [
  category('c2', 'c1', '0102', '原材料'),
  category('c1', 'c0', '01', '主料'),
  category('c0', null, '01', '主料'),
  category('c3', 'c2', '010201', '钢材'),
  category('orphan', 'missing', '02', '孤儿分类'),
]

describe('商品分类树组装', () => {
  it('扁平列表按 parentId 嵌套、同级按编码升序、孤儿节点兜底为根', () => {
    const tree = buildCategoryTree(flatCategories)
    expect(tree.map((node) => node.id)).toEqual(['c0', 'orphan'])
    const root = tree[0]
    expect(root.children.map((node) => node.id)).toEqual(['c1'])
    expect(root.children[0].children.map((node) => node.id)).toEqual(['c2'])
    expect(root.children[0].children[0].children.map((node) => node.id)).toEqual(['c3'])
  })

  it('按 Id 深度优先查找节点', () => {
    const tree = buildCategoryTree(flatCategories)
    expect(findCategoryNode(tree, 'c3')?.name).toBe('钢材')
    expect(findCategoryNode(tree, 'not-exists')).toBeNull()
  })

  it('收集自身及全部下级 Id', () => {
    const tree = buildCategoryTree(flatCategories)
    const root = findCategoryNode(tree, 'c0')!
    expect([...collectCategoryIds(root)].sort()).toEqual(['c0', 'c1', 'c2', 'c3'])
  })

  it('层级下拉按深度缩进并可排除子树', () => {
    const tree = buildCategoryTree(flatCategories)
    const options = buildCategoryOptions(tree)
    expect(options[0]).toEqual({ label: '主料', value: 'c0' })
    expect(options[1].label).toContain('└ 主料')

    const excluded = buildCategoryOptions(tree, new Set(['c1']))
    expect(excluded.some((option) => option.value === 'c1')).toBe(false)
  })
})

describe('枚举映射（数值对接）', () => {
  it('labelOfCode 未命中 / 空值兜底', () => {
    expect(labelOfCode({ 1: '启用' }, 1)).toBe('启用')
    expect(labelOfCode({ 1: '启用' }, 9)).toBe('—')
    expect(labelOfCode({ 1: '启用' }, null, '未知')).toBe('未知')
  })

  it('下拉选项值类型：表单为数值、搜索为字符串', () => {
    expect(toNumberOptions({ 1: '启用', 2: '停用' })).toEqual([
      { label: '启用', value: 1 },
      { label: '停用', value: 2 },
    ])
    expect(toStringOptions({ 1: '启用' })).toEqual([{ label: '启用', value: '1' }])
  })

  it('业务枚举中文与色调', () => {
    expect(partnerTypeLabel(1)).toBe('生产厂家')
    expect(supplierGradeLabel(4)).toBe('黑名单')
    expect(supplierGradeTone(4)).toBe('danger')
    expect(supplierGradeTone(1)).toBe('success')
    expect(creditGradeLabel(4)).toBe('D 级')
    expect(creditGradeTone(4)).toBe('danger')
    expect(partnerStatusLabel(2)).toBe('合作中')
    expect(partnerStatusTone(4)).toBe('danger')
    expect(personTypeLabel(2)).toBe('劳务人员')
    expect(idTypeLabel(1)).toBe('身份证')
    expect(employStatusLabel(4)).toBe('离职')
    expect(employStatusTone(4)).toBe('info')
  })

  it('字符串业务类型映射（编码规则 / 变更日志）', () => {
    expect(codeRuleBizTypeLabel('supplier')).toBe('供应商')
    expect(codeRuleBizTypeLabel('unknown-biz')).toBe('unknown-biz')
    expect(codeRuleBizTypeLabel(null)).toBe('—')
    expect(changeLogOperationLabel('VIEW_PLAINTEXT')).toBe('查看敏感明文')
    expect(changeLogOperationLabel('CUSTOM')).toBe('CUSTOM')
    expect(changeLogEntityLabel('Person')).toBe('人员')
    expect(changeLogEntityLabel('Custom')).toBe('Custom')
  })
})

describe('金额 / 日期格式化', () => {
  it('formatAmount 千分位与空值占位', () => {
    expect(formatAmount(1234567.5)).toBe('1,234,567.50')
    expect(formatAmount(null)).toBe('—')
    expect(formatAmount(0)).toBe('0.00')
  })

  it('formatPercent / formatRate', () => {
    expect(formatPercent(12.34)).toBe('12.3%')
    expect(formatPercent(null)).toBe('—')
    expect(formatRate(1.5)).toBe('1.5')
    expect(formatRate(0.0000001)).toBe('0')
    expect(formatRate(null)).toBe('—')
  })

  it('formatDateOnly 裁剪 ISO 时间并保留空值占位', () => {
    expect(formatDateOnly('2026-10-25T08:00:00+08:00')).toBe('2026-10-25')
    expect(formatDateOnly('2026-10-25')).toBe('2026-10-25')
    expect(formatDateOnly(null)).toBe('—')
  })

  it('formatEffectPeriod 失效日期为空表示长期有效', () => {
    expect(formatEffectPeriod('2026-01-01', '2026-12-31')).toBe('2026-01-01 ~ 2026-12-31')
    expect(formatEffectPeriod('2026-01-01', null)).toBe('2026-01-01 ~ 长期有效')
  })
})

describe('脱敏（前端兜底展示）', () => {
  it('手机号 / 证件号 / 银行卡号', () => {
    expect(maskMobile('13800001234')).toBe('138****1234')
    expect(maskMobile('123')).toBe('1****')
    expect(maskMobile(null)).toBe('—')
    expect(maskIdNo('510101199001011234')).toBe('510101********1234')
    expect(maskIdNo('12345')).toBe('12****45')
    expect(maskBankCard('6222020200112233445')).toBe('**** **** 3445')
    expect(maskBankCard('')).toBe('—')
  })
})

describe('快照与分段模板展示', () => {
  it('prettyJson 美化合法 JSON、原样回显非法 JSON', () => {
    expect(prettyJson('{"a":1}')).toBe('{\n  "a": 1\n}')
    expect(prettyJson('not-json')).toBe('not-json')
    expect(prettyJson(null)).toBe('—')
  })

  it('summarizeOrgSnapshot 取关键字段，解析失败返回 null', () => {
    const summary = summarizeOrgSnapshot(
      '{"name":"总部","type":1,"status":2,"parentId":"p1","version":3,"junk":true}',
    )
    expect(summary).toEqual({ name: '总部', type: 1, status: 2, parentId: 'p1', version: 3 })
    expect(summarizeOrgSnapshot('broken')).toBeNull()
    expect(summarizeOrgSnapshot(null)).toBeNull()
  })

  it('formatSegments 将分段模板转为中文说明', () => {
    expect(formatSegments('["{type}","-","{seq:4}"]')).toBe('类型代码 · - · 4 位流水号')
    expect(formatSegments('["{parent}","{yyyyMM}"]')).toBe('上级编码 · 年月(yyyyMM)')
    expect(formatSegments('["{custom}"]')).toBe('{custom}')
    expect(formatSegments('raw-text')).toBe('raw-text')
    expect(formatSegments(null)).toBe('—')
  })
})

/** 构造审批实例（后端「我发起的」列表直出字段）。 */
function approvalInstance(
  bizId: string,
  startedAt: string,
  status = 1,
  bizType: string = ORG_ADJUST_BIZ_TYPE,
): {
  instanceId: string
  bizType: string
  bizId: string
  status: number
  startedAt: string
} {
  return { instanceId: `${bizId}@${startedAt}`, bizType, bizId, status, startedAt }
}

describe('组织调整审批归并', () => {
  it('同 bizId 多条取 startedAt 最新，不同 bizId 互不覆盖', () => {
    const map = mapOrgAdjustApprovals([
      approvalInstance('org1', '2026-10-24T09:00:00+08:00', 1),
      approvalInstance('org1', '2026-10-25T09:00:00+08:00', 2),
      approvalInstance('org2', '2026-10-23T09:00:00+08:00', 3),
    ])
    expect(map.size).toBe(2)
    expect(map.get('org1')?.status).toBe(2)
    expect(map.get('org1')?.startedAt).toBe('2026-10-25T09:00:00+08:00')
    expect(map.get('org2')?.status).toBe(3)
  })

  it('乱序输入仍取最新一条', () => {
    const map = mapOrgAdjustApprovals([
      approvalInstance('org1', '2026-10-25T09:00:00+08:00', 2),
      approvalInstance('org1', '2026-10-20T09:00:00+08:00', 3),
      approvalInstance('org1', '2026-10-22T09:00:00+08:00', 4),
    ])
    expect(map.get('org1')?.status).toBe(2)
  })

  it('忽略其他 bizType 与空 bizId，空列表返回空 Map', () => {
    const map = mapOrgAdjustApprovals([
      approvalInstance('org1', '2026-10-25T09:00:00+08:00', 1, 'bas.supplier.admission'),
      approvalInstance('', '2026-10-25T09:00:00+08:00', 1),
    ])
    expect(map.size).toBe(0)
    expect(mapOrgAdjustApprovals([]).size).toBe(0)
  })
})

describe('表格行键补齐', () => {
  it('编码规则行以 bizType 作为 id', () => {
    const rows: CodeRuleRow[] = [
      { bizType: 'org', prefix: 'ORG', segmentsJson: '[]', status: 1, updatedAt: '2026-10-25' },
    ]
    expect(toCodeRuleRows(rows)[0].id).toBe('org')
  })

  it('变更日志行以 eventId 作为 id', () => {
    const rows: ChangeLogRow[] = [
      {
        eventId: 'e1',
        entityType: 'Person',
        entityId: 'p1',
        operation: 'UPDATE',
        oldValues: null,
        newValues: '{}',
        changedBy: 'u1',
        changedAt: '2026-10-25T08:00:00+08:00',
      },
    ]
    expect(toChangeLogRows(rows)[0].id).toBe('e1')
  })
})
