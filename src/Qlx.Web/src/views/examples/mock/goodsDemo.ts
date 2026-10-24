/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：标准示例页 mock 数据源——示例商品（DemoGoods）实体、分页查询、增删改、树数据
 * 说明：与真实 REST 共用 PagedQuery / PagedResult 形态；页面内直接替换为 @/api 下的
 *       真实接口函数即可完成对接，视图代码零改动（参考 PortalUserListView → fetchUsers）。
 */

import type { PagedQuery, PagedResult } from '@/types/admin'

/** 示例业务实体——列表页 / 表单 / 树详情三个示例共用。 */
export interface DemoGoods {
  id: string
  /** 业务编码（表格 code 型列：可复制）。 */
  code: string
  name: string
  /** 类目（查询区 select / 表格 enum 型列演示）。 */
  category: 'office' | 'electronics' | 'furniture' | 'consumable'
  /** 状态（表格 tag 型列演示）。 */
  status: 'OnShelf' | 'OffShelf'
  price: number
  stock: number
  remark: string
  createdAt: string
  updatedAt: string
}

/** 枚举 → 中文文案映射（B-2：映射字典在前端公共层维护，后端只下发数值/代码）。 */
export const goodsCategoryLabels: Record<DemoGoods['category'], string> = {
  office: '办公用品',
  electronics: '电子设备',
  furniture: '办公家具',
  consumable: '耗材',
}

export const goodsStatusLabels: Record<DemoGoods['status'], string> = {
  OnShelf: '在售',
  OffShelf: '下架',
}

/* ============ mock 数据生成（模块级内存库，演示期间可增删改） ============ */

const CATEGORY_KEYS = Object.keys(goodsCategoryLabels) as DemoGoods['category'][]
const NAME_PARTS = [
  '复印纸', '签字笔', '订书机', '文件柜', '碎纸机', '投影仪', '笔记本', '标签纸',
  '白板', '会议桌', '人体工学椅', '墨盒', '扫描仪', '名片盒', '回形针', '档案盒',
]

function buildSeedRows(): DemoGoods[] {
  const rows: DemoGoods[] = []
  // 生成 47 条：足以覆盖多页分页演示（默认 10 条/页 → 5 页）
  for (let i = 0; i < 47; i += 1) {
    const category = CATEGORY_KEYS[i % CATEGORY_KEYS.length]
    const day = (i % 28) + 1
    rows.push({
      id: `g-${1000 + i}`,
      code: `GD-${String(10001 + i)}`,
      name: `${NAME_PARTS[i % NAME_PARTS.length]} ${['标准款', '加厚款', '经济款'][i % 3]}`,
      category,
      status: i % 4 === 3 ? 'OffShelf' : 'OnShelf',
      price: Math.round((9.9 + ((i * 37) % 900)) * 100) / 100,
      stock: (i * 53) % 500,
      remark: i % 5 === 0 ? '重点管控物资，采购需二级审批' : '',
      createdAt: `2026-09-${String(day).padStart(2, '0')}T09:30:00+08:00`,
      updatedAt: `2026-10-${String(day).padStart(2, '0')}T16:20:00+08:00`,
    })
  }
  return rows
}

const goodsStore: DemoGoods[] = buildSeedRows()
let idSeq = 2000

/** 模拟网络延迟（演示 loading 态）。 */
function delay(ms = 180): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

/* ============ 查询 / 增删改（与真实接口同签名，便于平移替换） ============ */

export interface DemoGoodsQuery extends PagedQuery {
  keyword?: string
  category?: string
  status?: string
}

/** 列表数据源：供 useCrudTable(fetchGoodsDemoList) 直接消费。 */
export async function fetchGoodsDemoList(query: DemoGoodsQuery): Promise<PagedResult<DemoGoods>> {
  await delay()
  const keyword = (query.keyword ?? '').trim().toLowerCase()
  let matched = goodsStore.filter((item) => {
    if (query.category && item.category !== query.category) {
      return false
    }
    if (query.status && item.status !== query.status) {
      return false
    }
    if (keyword) {
      const haystack = `${item.code} ${item.name}`.toLowerCase()
      if (!haystack.includes(keyword)) {
        return false
      }
    }
    return true
  })
  // 按更新时间倒序（最新在前），模拟后端默认排序
  matched = [...matched].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))

  const page = Math.max(1, query.page)
  const pageSize = Math.max(1, query.pageSize)
  const start = (page - 1) * pageSize
  const items = matched.slice(start, start + pageSize)
  const totalCount = matched.length
  return {
    items,
    totalCount,
    page,
    pageSize,
    totalPages: Math.ceil(totalCount / pageSize),
    isLastPage: page >= Math.ceil(totalCount / pageSize),
  }
}

/** 新建（编码由“后端”生成——对应 N-1：前端不传编码，示例中由 mock 层模拟生成）。 */
export async function createGoodsDemo(
  input: Pick<DemoGoods, 'name' | 'category' | 'price' | 'stock' | 'remark'>,
): Promise<DemoGoods> {
  await delay(240)
  const now = new Date().toISOString()
  const row: DemoGoods = {
    id: `g-${idSeq}`,
    code: `GD-${idSeq}`,
    status: 'OnShelf',
    ...input,
    createdAt: now,
    updatedAt: now,
  }
  idSeq += 1
  goodsStore.unshift(row)
  return row
}

/** 更新（编码不可变——对应 N-1-3：编码一经生成不可修改）。 */
export async function updateGoodsDemo(
  id: string,
  input: Pick<DemoGoods, 'name' | 'category' | 'price' | 'stock' | 'remark'>,
): Promise<DemoGoods> {
  await delay(240)
  const row = goodsStore.find((item) => item.id === id)
  if (!row) {
    throw new Error('记录不存在或已被删除')
  }
  Object.assign(row, input, { updatedAt: new Date().toISOString() })
  return row
}

/** 删除（示例删除确认演示）。 */
export async function removeGoodsDemo(id: string): Promise<void> {
  await delay(200)
  const index = goodsStore.findIndex((item) => item.id === id)
  if (index >= 0) {
    goodsStore.splice(index, 1)
  }
}

/** 上/下架切换（行内状态操作演示）。 */
export async function toggleGoodsDemoStatus(id: string): Promise<void> {
  await delay(200)
  const row = goodsStore.find((item) => item.id === id)
  if (row) {
    row.status = row.status === 'OnShelf' ? 'OffShelf' : 'OnShelf'
    row.updatedAt = new Date().toISOString()
  }
}

/* ============ 树详情示例数据（类目 → 商品） ============ */

/** 树节点（QlxTreePanel 泛型 T 需含 id）。 */
export interface DemoTreeNode {
  id: string
  name: string
  children?: DemoTreeNode[]
}

/** 类目树：一级「全部类目」+ 四个类目节点（叶子数 = 该类目商品数，演示节点插槽计数）。 */
export function buildGoodsCategoryTree(): DemoTreeNode[] {
  return [
    {
      id: 'all',
      name: '全部类目',
      children: CATEGORY_KEYS.map((key) => ({
        id: key,
        name: goodsCategoryLabels[key],
      })),
    },
  ]
}

/** 按类目取商品（树选中联动右侧详情/表格演示）。 */
export function listGoodsByCategory(category: string): DemoGoods[] {
  if (!category || category === 'all') {
    return [...goodsStore]
  }
  return goodsStore.filter((item) => item.category === category)
}
