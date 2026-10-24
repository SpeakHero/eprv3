/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：模块导航目录（工作台顶部导航 / 悬停全量菜单 / 快捷入口共同数据源）
 * 说明：后续接入 IModule Manifest 下发后，本目录退化为本地兜底；当前与后端已注册模块编码保持一致。
 */

export interface NavItem {
  code: string
  name: string
  icon: string
}

export interface NavGroup {
  name: string
  items: NavItem[]
}

export interface NavModule {
  code: string
  name: string
  groups: NavGroup[]
}

export interface TopMenu {
  code: string
  name: string
  /** 归属该顶级菜单的模块编码。 */
  modules: string[]
}

/** 业务模块目录。 */
export const navModules: NavModule[] = [
  {
    code: 'bas',
    name: '基础信息',
    groups: [
      {
        name: '组织与人员',
        items: [
          { code: 'org', name: '组织架构', icon: 'OfficeBuilding' },
          { code: 'position', name: '岗位管理', icon: 'UserFilled' },
          { code: 'person', name: '人员档案', icon: 'User' },
        ],
      },
      {
        name: '往来单位',
        items: [
          { code: 'supplier', name: '供应商档案', icon: 'Shop' },
          { code: 'customer', name: '客户档案', icon: 'Avatar' },
          { code: 'rating', name: '往来评级', icon: 'Star' },
        ],
      },
      {
        name: '基础数据',
        items: [
          { code: 'category', name: '商品分类', icon: 'Grid' },
          { code: 'unit', name: '计量单位', icon: 'Coin' },
          { code: 'dictionary', name: '数据字典', icon: 'Collection' },
        ],
      },
    ],
  },
  {
    code: 'stk',
    name: '商品管理',
    groups: [
      {
        name: '商品档案',
        items: [
          { code: 'product', name: '商品档案', icon: 'Goods' },
          { code: 'spec', name: '商品规格', icon: 'Box' },
          { code: 'image', name: '商品图片', icon: 'Picture' },
        ],
      },
      {
        name: '库存管理',
        items: [
          { code: 'ledger', name: '库存台账', icon: 'Notebook' },
          { code: 'transaction', name: '出入库流水', icon: 'Tickets' },
          { code: 'alert', name: '库存预警', icon: 'Warning' },
        ],
      },
    ],
  },
  {
    code: 'pur',
    name: '采购管理',
    groups: [
      {
        name: '采购前端',
        items: [
          { code: 'requisition', name: '采购需求', icon: 'Memo' },
          { code: 'application', name: '采购申请', icon: 'Document' },
        ],
      },
      {
        name: '采购执行',
        items: [
          { code: 'order', name: '采购订单', icon: 'ShoppingCart' },
          { code: 'receipt', name: '收货对账', icon: 'Van' },
        ],
      },
      {
        name: '结算',
        items: [{ code: 'payment', name: '采购付款', icon: 'Money' }],
      },
    ],
  },
  {
    code: 'sal',
    name: '销售管理',
    groups: [
      {
        name: '销售前端',
        items: [
          { code: 'quotation', name: '销售报价', icon: 'Postcard' },
          { code: 'order', name: '销售订单', icon: 'Sell' },
        ],
      },
      {
        name: '执行',
        items: [
          { code: 'issue', name: '销售出库', icon: 'Van' },
          { code: 'reconciliation', name: '销售对账', icon: 'Finished' },
        ],
      },
      {
        name: '结算',
        items: [{ code: 'receipt', name: '销售回款', icon: 'Wallet' }],
      },
    ],
  },
  {
    code: 'wms',
    name: '物流管理',
    groups: [
      {
        name: '仓库',
        items: [
          { code: 'warehouse', name: '仓库档案', icon: 'House' },
          { code: 'location', name: '库位管理', icon: 'Grid' },
        ],
      },
      {
        name: '出入库',
        items: [
          { code: 'receipt', name: '收货单', icon: 'Van' },
          { code: 'issue', name: '发货单', icon: 'Van' },
          { code: 'transfer', name: '内部调拨', icon: 'Switch' },
          { code: 'check', name: '库存盘点', icon: 'List' },
        ],
      },
    ],
  },
  {
    code: 'hr',
    name: '人员管理',
    groups: [
      {
        name: '人员',
        items: [
          { code: 'onboarding', name: '入职登记', icon: 'UserFilled' },
          { code: 'transfer', name: '人事异动', icon: 'Switch' },
          { code: 'leave', name: '离职办理', icon: 'User' },
        ],
      },
      {
        name: '用工',
        items: [
          { code: 'contract', name: '劳动合同', icon: 'Files' },
          { code: 'attendance', name: '考勤打卡', icon: 'Timer' },
          { code: 'payroll', name: '薪资管理', icon: 'Coin' },
        ],
      },
    ],
  },
  {
    code: 'ctr',
    name: '合同管理',
    groups: [
      {
        name: '合同',
        items: [
          { code: 'archive', name: '合同档案', icon: 'Files' },
          { code: 'review', name: '合同评审', icon: 'Finished' },
          { code: 'change', name: '合同变更', icon: 'Switch' },
        ],
      },
      {
        name: '结算',
        items: [{ code: 'settlement', name: '结算单', icon: 'Ticket' }],
      },
    ],
  },
  {
    code: 'inv',
    name: '票据管理',
    groups: [
      {
        name: '发票',
        items: [
          { code: 'input', name: '进项发票', icon: 'Document' },
          { code: 'output', name: '销项发票', icon: 'Document' },
          { code: 'expense', name: '费用票据', icon: 'Tickets' },
          { code: 'labor', name: '劳务票据', icon: 'CreditCard' },
        ],
      },
      {
        name: '核验',
        items: [
          { code: 'verify', name: '发票核验', icon: 'Finished' },
          { code: 'matching', name: '三单匹配', icon: 'DataAnalysis' },
        ],
      },
    ],
  },
  {
    code: 'fin',
    name: '财务管理',
    groups: [
      {
        name: '账务',
        items: [
          { code: 'voucher', name: '凭证管理', icon: 'Tickets' },
          { code: 'cost', name: '成本核算', icon: 'PieChart' },
          { code: 'budget', name: '预算管理', icon: 'TrendCharts' },
        ],
      },
      {
        name: '资金',
        items: [
          { code: 'expense', name: '报销管理', icon: 'Wallet' },
          { code: 'fund', name: '资金管理', icon: 'Money' },
          { code: 'receivable', name: '往来管理', icon: 'CreditCard' },
        ],
      },
    ],
  },
  {
    code: 'prj',
    name: '项目管理',
    groups: [
      {
        name: '项目',
        items: [
          { code: 'archive', name: '项目档案', icon: 'Management' },
          { code: 'budget', name: '项目预算', icon: 'TrendCharts' },
          { code: 'milestone', name: '里程碑', icon: 'Calendar' },
        ],
      },
      {
        name: '归集',
        items: [
          { code: 'output', name: '产值归集', icon: 'DataAnalysis' },
          { code: 'cost', name: '成本归集', icon: 'PieChart' },
        ],
      },
    ],
  },
  {
    code: 'eqp',
    name: '设备与租赁',
    groups: [
      {
        name: '设备',
        items: [
          { code: 'asset', name: '设备台账', icon: 'Odometer' },
          { code: 'depreciation', name: '折旧管理', icon: 'TrendCharts' },
        ],
      },
      {
        name: '租赁',
        items: [
          { code: 'rental', name: '租赁订单', icon: 'Memo' },
          { code: 'settlement', name: '租赁结算', icon: 'Ticket' },
        ],
      },
    ],
  },
  {
    code: 'portal',
    name: '系统管理',
    groups: [
      {
        name: '门户管理',
        items: [
          { code: 'tenant', name: '租户管理', icon: 'OfficeBuilding' },
          { code: 'license', name: '许可管理', icon: 'Setting' },
          { code: 'module', name: '模块定义', icon: 'Grid' },
        ],
      },
      {
        name: '身份权限',
        items: [
          { code: 'user', name: '用户管理', icon: 'User' },
          { code: 'role', name: '角色权限', icon: 'Lock' },
          { code: 'parameter', name: '系统参数', icon: 'SetUp' },
        ],
      },
    ],
  },
]

/** 顶部导航（对应电子税务局式一级栏目，鼠标悬停展开全量菜单）。 */
export const topMenus: TopMenu[] = [
  { code: 'workbench', name: '工作台', modules: [] },
  { code: 'business', name: '业务办理', modules: ['pur', 'sal', 'wms', 'stk'] },
  { code: 'finance', name: '财务票据', modules: ['fin', 'inv', 'ctr'] },
  { code: 'people', name: '人事项目', modules: ['hr', 'prj', 'eqp'] },
  { code: 'basedata', name: '基础资料', modules: ['bas'] },
  { code: 'system', name: '系统管理', modules: ['portal'] },
]

/** 工作台常用功能入口（按模块许可过滤后展示）。 */
export const quickEntries: Array<{ moduleCode: string; itemCode: string }> = [
  { moduleCode: 'bas', itemCode: 'org' },
  { moduleCode: 'bas', itemCode: 'supplier' },
  { moduleCode: 'bas', itemCode: 'customer' },
  { moduleCode: 'pur', itemCode: 'order' },
  { moduleCode: 'sal', itemCode: 'order' },
  { moduleCode: 'stk', itemCode: 'ledger' },
  { moduleCode: 'fin', itemCode: 'voucher' },
  { moduleCode: 'inv', itemCode: 'input' },
  { moduleCode: 'hr', itemCode: 'onboarding' },
  { moduleCode: 'ctr', itemCode: 'archive' },
  { moduleCode: 'prj', itemCode: 'archive' },
  { moduleCode: 'eqp', itemCode: 'asset' },
]

/** 按编码取模块。 */
export function findModule(moduleCode: string): NavModule | undefined {
  return navModules.find((item) => item.code === moduleCode)
}

/** 按模块与条目编码取菜单项。 */
export function findNavItem(moduleCode: string, itemCode: string): NavItem | undefined {
  return findModule(moduleCode)
    ?.groups.flatMap((group) => group.items)
    .find((item) => item.code === itemCode)
}

/** 业务功能页地址（原生新标签页打开）。 */
export function moduleHref(moduleCode: string, itemCode: string): string {
  return `/module/${moduleCode}/${itemCode}`
}
