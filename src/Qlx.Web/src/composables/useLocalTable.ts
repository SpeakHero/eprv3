/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：扁平列表本地分页组合式函数——后端返回全量扁平集合时，前端负责过滤 / 排序外的展示分页（B-4）
 * 说明：列表端点不分页（仓库 / 商品 / 期初 / 台账 / 流水 / 批次）时复用；过滤条件透传给数据源，
 *       可选本地过滤器仅用于数据源不支持的展示筛选，不改变后端下发的原始数据。
 */

import { computed, onMounted, shallowRef, ref } from 'vue'
import type { ComputedRef, Ref, ShallowRef } from 'vue'

export interface LocalTableOptions<T> {
  /** 本地展示过滤（在数据源返回结果上按查询条件筛选）。 */
  filter?: (row: T, query: Record<string, string>) => boolean
  /** 是否挂载即加载（默认 true）。 */
  immediate?: boolean
  /** 默认页大小。 */
  pageSize?: number
}

export interface LocalTableState<T> {
  /** 数据源返回的全量行（未经分页）。 */
  rows: ShallowRef<T[]>
  loading: Ref<boolean>
  /** 当前页展示行。 */
  items: ComputedRef<T[]>
  total: ComputedRef<number>
  page: Ref<number>
  pageSize: Ref<number>
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
 * 扁平列表状态机：数据源按查询条件取全量行，前端做展示分页。
 * @param source 数据源（接收当前查询条件，返回全量扁平行）。
 * @param options 本地过滤 / 初始页大小 / 是否立即加载。
 */
export function useLocalTable<T>(
  source: (query: Record<string, string>) => Promise<T[]>,
  options: LocalTableOptions<T> = {},
): LocalTableState<T> {
  const rows = shallowRef<T[]>([]) as ShallowRef<T[]>
  const loading = ref(false)
  const page = ref(1)
  const pageSize = ref(options.pageSize ?? 10)
  const query = ref<Record<string, string>>({})

  const filtered = computed<T[]>(() =>
    options.filter ? rows.value.filter((row) => options.filter!(row, query.value)) : rows.value,
  )
  const total = computed(() => filtered.value.length)
  const items = computed(() =>
    filtered.value.slice((page.value - 1) * pageSize.value, page.value * pageSize.value),
  )

  async function load(): Promise<void> {
    loading.value = true
    try {
      rows.value = await source({ ...query.value })
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
  }

  if (options.immediate !== false) {
    onMounted(() => {
      void load()
    })
  }

  return {
    rows,
    loading,
    items,
    total,
    page,
    pageSize,
    query,
    search,
    reset,
    refresh: load,
    changePage,
  }
}
