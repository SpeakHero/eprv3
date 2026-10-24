/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：通用列表页状态逻辑——加载 / 分页 / 搜索 / 刷新，供所有 CRUD 列表页复用
 */

import { onMounted, ref, shallowRef } from 'vue'
import type { Ref, ShallowRef } from 'vue'
import type { PagedQuery, PagedResult } from '@/types/admin'

/** 列表数据源形态（mock 与真实 REST 共用）。 */
export type CrudFetcher<T> = (query: PagedQuery & Record<string, unknown>) => Promise<PagedResult<T>>

export interface UseCrudTableOptions {
  /** 默认页大小。 */
  pageSize?: number
  /** 是否挂载即加载（默认 true；跨标签返回刷新场景可关闭）。 */
  immediate?: boolean
}

export interface CrudTableState<T> {
  loading: Ref<boolean>
  items: ShallowRef<T[]>
  total: Ref<number>
  page: Ref<number>
  pageSize: Ref<number>
  /** 当前查询条件（搜索栏 v-model 绑定对象）。 */
  query: Ref<Record<string, string>>
  /** 按给定条件重查（重置页码到第 1 页）。 */
  search: (nextQuery: Record<string, string>) => Promise<void>
  /** 清空条件重查。 */
  reset: () => Promise<void>
  /** 保持条件重查当前页。 */
  refresh: () => Promise<void>
  /** 翻页 / 改页大小（由 QlxDataTable 分页器回调）。 */
  changePage: (page: number, pageSize: number) => Promise<void>
}

/**
 * 列表页状态机：search 条件变更回到第 1 页；changePage 保持条件翻页。
 * 数据源函数自行负责把 query 过滤参数并入请求。
 */
export function useCrudTable<T>(
  fetcher: CrudFetcher<T>,
  options: UseCrudTableOptions = {},
): CrudTableState<T> {
  const loading = ref(false)
  const items = shallowRef<T[]>([]) as ShallowRef<T[]>
  const total = ref(0)
  const page = ref(1)
  const pageSize = ref(options.pageSize ?? 10)
  const query = ref<Record<string, string>>({})

  async function load(): Promise<void> {
    loading.value = true
    try {
      const result = await fetcher({ ...query.value, page: page.value, pageSize: pageSize.value })
      items.value = result.items
      total.value = result.totalCount
    } finally {
      loading.value = false
    }
  }

  async function search(nextQuery: Record<string, string>): Promise<void> {
    query.value = nextQuery
    page.value = 1
    await load()
  }

  async function reset(): Promise<void> {
    query.value = {}
    page.value = 1
    await load()
  }

  async function changePage(nextPage: number, nextPageSize: number): Promise<void> {
    page.value = nextPage
    pageSize.value = nextPageSize
    await load()
  }

  if (options.immediate !== false) {
    onMounted(() => {
      void load()
    })
  }

  return { loading, items, total, page, pageSize, query, search, reset, refresh: load, changePage }
}
