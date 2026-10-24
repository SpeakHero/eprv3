/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：工作台示例数据（待办 / 通知 / 指标）
 * 说明：骨架阶段无对应后端接口，此处为演示数据；接入业务模块后替换为接口请求。
 */

export interface StatCard {
  key: string
  label: string
  value: string
  unit: string
  trend: string
  icon: string
  tone: 'brand' | 'warning' | 'info' | 'danger'
}

export interface TodoRow {
  docType: string
  docNo: string
  submitter: string
  amount: string
  submittedAt: string
  status: string
  statusType: 'warning' | 'info' | 'success'
}

export interface NoticeRow {
  tag: string
  title: string
  date: string
}

export const workbenchStats: StatCard[] = [
  { key: 'todo', label: '待办审批', value: '8', unit: '件', trend: '较昨日 +2', icon: 'Finished', tone: 'brand' },
  { key: 'message', label: '未读消息', value: '5', unit: '条', trend: '含催办 1 条', icon: 'Bell', tone: 'warning' },
  { key: 'purchase', label: '本月采购额', value: '128.64', unit: '万元', trend: '环比 −4.2%', icon: 'TrendCharts', tone: 'info' },
  { key: 'warning', label: '库存预警', value: '3', unit: '项', trend: '低于安全库存', icon: 'Warning', tone: 'danger' },
]

export const workbenchTodos: TodoRow[] = [
  {
    docType: '采购订单',
    docNo: 'CG-QLX-202610-000128',
    submitter: '李敏',
    amount: '¥286,400.00',
    submittedAt: '10-21 09:24',
    status: '待审批',
    statusType: 'warning',
  },
  {
    docType: '付款申请',
    docNo: 'CW-QLX-202610-000076',
    submitter: '王强',
    amount: '¥92,800.00',
    submittedAt: '10-21 08:51',
    status: '待审批',
    statusType: 'warning',
  },
  {
    docType: '销售合同',
    docNo: 'HT-QLX-202610-000033',
    submitter: '赵蕾',
    amount: '¥1,120,000.00',
    submittedAt: '10-20 17:35',
    status: '待复核',
    statusType: 'info',
  },
  {
    docType: '费用报销',
    docNo: 'CW-QLX-202610-000214',
    submitter: '陈晨',
    amount: '¥3,460.00',
    submittedAt: '10-20 16:08',
    status: '待审批',
    statusType: 'warning',
  },
  {
    docType: '供应商准入',
    docNo: 'RY-QLX-202610-000019',
    submitter: '周宇',
    amount: '—',
    submittedAt: '10-20 14:42',
    status: '待审批',
    statusType: 'warning',
  },
]

export const workbenchNotices: NoticeRow[] = [
  { tag: '制度', title: '《集团采购管理办法（2026 年修订）》发布', date: '10-21' },
  { tag: '系统', title: '10 月 26 日 22:00 系统例行维护公告', date: '10-20' },
  { tag: '提醒', title: '第三季度劳务分包结算资料补充提醒', date: '10-20' },
  { tag: '制度', title: '数电票版式文件归档规范更新', date: '10-18' },
  { tag: '通知', title: '集团 2026 年度预算中期调整工作启动', date: '10-17' },
]

/** 按小时返回问候语。 */
export function greetingOf(hour: number): string {
  if (hour < 6) return '凌晨好'
  if (hour < 12) return '上午好'
  if (hour < 14) return '中午好'
  if (hour < 18) return '下午好'
  return '晚上好'
}

/** 中文长日期（2026年10月21日 星期三）。 */
export function longDateText(date: Date): string {
  const weekdays = ['星期日', '星期一', '星期二', '星期三', '星期四', '星期五', '星期六']
  return `${date.getFullYear()}年${date.getMonth() + 1}月${date.getDate()}日 ${weekdays[date.getDay()]}`
}

/** 最近进入时间（YYYY-MM-DD HH:mm），空值返回占位。 */
export function formatLastLogin(value: string | null): string {
  if (!value) {
    return '首次进入'
  }
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return '—'
  }
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}
