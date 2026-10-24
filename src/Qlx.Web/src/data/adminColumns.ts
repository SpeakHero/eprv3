/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：门户管理端共享表格列配置——租户详情与模块许可页共用的授权记录列
 */

import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import { licenseActionLabels } from '@/types/admin'
import type { LicenseRecord } from '@/types/admin'

/** 授权记录列（LicenseRecord）。 */
export const licenseRecordColumns: Array<CrudColumn<LicenseRecord>> = [
  { prop: 'tenantName', label: '租户', width: 160 },
  { prop: 'moduleName', label: '模块', width: 140 },
  {
    prop: 'action',
    label: '操作',
    type: 'tag',
    width: 90,
    align: 'center',
    tagText: (row) => licenseActionLabels[row.action],
    tagTone: (row) => (row.action === 'Granted' ? 'success' : row.action === 'Revoked' ? 'danger' : 'warning'),
  },
  { prop: 'remark', label: '变更说明', minWidth: 260 },
  { prop: 'operatorName', label: '操作人', width: 110 },
  { prop: 'at', label: '时间', type: 'time', width: 150 },
]
