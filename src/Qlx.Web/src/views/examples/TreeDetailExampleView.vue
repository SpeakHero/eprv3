<!--
  所属公司：四川淇利馨集团有限公司
  描述：标准示例 · 左树右详情——QlxTreePanel 类目树 + QlxDescriptionList 类目详情 +
        QlxDataTable 该类目下商品清单（mock 驱动）
  说明：适用于组织机构、分类档案、区域目录等「主从结构」页面（参考 TenantOrganizationView）。
        树组装在前端完成（B-2）：后端只下发扁平列表 + parent 字段，前端组装层级。
  组件适用场景：
    QlxTreePanel       左树：标题 + 关键字过滤 + 树选中 + #node 节点插槽 + #footer 底部操作
    QlxDescriptionList 右侧详情头部：配置驱动键值对，复杂值用 #item-{label} 插槽
-->
<script setup lang="ts">
import { computed, ref } from 'vue'
import { ElMessage } from 'element-plus'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import QlxDescriptionList from '@/components/crud/QlxDescriptionList.vue'
import type { DescriptionItem } from '@/components/crud/QlxDescriptionList.vue'
import QlxTreePanel from '@/components/crud/QlxTreePanel.vue'
import {
  buildGoodsCategoryTree,
  goodsCategoryLabels,
  goodsStatusLabels,
  listGoodsByCategory,
} from './mock/goodsDemo'
import type { DemoGoods, DemoTreeNode } from './mock/goodsDemo'

/* ============================================================
 * 1. 左树（QlxTreePanel）
 *    data 为树形数组（泛型 T 需含 id）；labelKey / childrenKey 可改字段名；
 *    @select 回传 (id, nodeData)；#node 插槽自定义节点内容（此处追加计数徽标）；
 *    #footer 插槽放置树级操作（如「新增根节点」）。
 * ============================================================ */
const treeData = ref<DemoTreeNode[]>(buildGoodsCategoryTree())
const selectedKey = ref('all')
const selectedNode = ref<DemoTreeNode | null>(null)

function onSelect(id: string, node: DemoTreeNode): void {
  selectedKey.value = id
  selectedNode.value = node
}

/* ============================================================
 * 2. 右侧详情（QlxDescriptionList）+ 关联清单（QlxDataTable）
 *    树选中联动：按选中类目过滤商品（真实场景：选中后调接口重查）。
 * ============================================================ */
const categoryGoods = computed<DemoGoods[]>(() => listGoodsByCategory(selectedKey.value))

const detailItems = computed<DescriptionItem[]>(() => {
  const key = selectedKey.value
  const label =
    key === 'all' ? '全部类目' : goodsCategoryLabels[key as DemoGoods['category']] ?? key
  const rows = categoryGoods.value
  const onShelf = rows.filter((item) => item.status === 'OnShelf').length
  const stockTotal = rows.reduce((sum, item) => sum + item.stock, 0)
  return [
    { label: '类目名称', value: label },
    { label: '商品数', value: `${rows.length} 个` },
    { label: '在售 / 下架', value: `${onShelf} / ${rows.length - onShelf}` },
    { label: '库存合计', value: `${stockTotal} 件` },
    { label: '节点路径', value: selectedNode.value?.children ? `全部类目 / …` : `全部类目 / ${label}` },
  ]
})

/** 关联清单列（与标准列表页共用列配置思路，此处精简；无操作列则不传 #actions）。 */
const goodsColumns: Array<CrudColumn<DemoGoods>> = [
  { prop: 'code', label: '编码', type: 'code', width: 110 },
  { prop: 'name', label: '商品名称', minWidth: 140 },
  { prop: 'price', label: '单价（元）', width: 100, align: 'right' },
  { prop: 'stock', label: '库存', width: 80, align: 'right' },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 80,
    align: 'center',
    tagText: (row) => goodsStatusLabels[row.status],
    tagTone: (row) => (row.status === 'OnShelf' ? 'success' : 'info'),
  },
]
</script>

<template>
  <div class="tree-demo">
    <!-- 左树：selected-key 受控（父级可清空高亮）；#node 自定义节点；#footer 树级操作 -->
    <QlxTreePanel
      title="商品类目"
      :data="treeData"
      :selected-key="selectedKey"
      search-placeholder="搜索类目名称"
      @select="onSelect"
    >
      <!-- 节点插槽：默认只显示名称，此处追加该类目商品计数（B-4：展示组装在前端） -->
      <template #node="{ data }">
        <span class="tree-demo__node">
          <span class="tree-demo__node-name">{{ data.name }}</span>
          <span v-if="data.id !== 'all'" class="tree-demo__node-count qlx-numeric">
            {{ listGoodsByCategory(data.id).length }}
          </span>
        </span>
      </template>
      <template #footer>
        <el-button plain @click="ElMessage.info('演示：树级操作入口（如新增根类目）')">
          新增根类目
        </el-button>
      </template>
    </QlxTreePanel>

    <!-- 右侧：描述列表（详情头部）+ 该类目商品清单（无分页：total 传行数即可） -->
    <div class="tree-demo__content">
      <section class="tree-demo__panel">
        <h2 class="tree-demo__panel-title">类目详情</h2>
        <QlxDescriptionList :items="detailItems" :column="2" />
      </section>

      <section class="tree-demo__panel">
        <h2 class="tree-demo__panel-title">该类目下商品</h2>
        <QlxDataTable
          :items="categoryGoods.slice(0, 10)"
          :columns="goodsColumns"
          :total="categoryGoods.length"
          :page="1"
          :page-size="10"
          empty-text="该类目下暂无商品"
        />
        <p class="tree-demo__hint">清单仅演示前 10 条；真实场景选中类目后经 useCrudTable 重查接口。</p>
      </section>
    </div>
  </div>
</template>

<style scoped>
.tree-demo {
  display: flex;
  align-items: flex-start;
  gap: var(--qlx-space-4);
}

.tree-demo__content {
  display: flex;
  min-width: 0;
  flex: 1;
  flex-direction: column;
  gap: var(--qlx-space-4);
}

.tree-demo__panel {
  padding: var(--qlx-space-4) var(--qlx-space-5) var(--qlx-space-5);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
  box-shadow: var(--qlx-shadow-xs);
}

.tree-demo__panel-title {
  margin-bottom: var(--qlx-space-3);
  color: var(--qlx-brand-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-md);
  font-weight: 600;
}

.tree-demo__hint {
  margin-top: var(--qlx-space-2);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

/* 树节点：名称 + 计数徽标 */
.tree-demo__node {
  display: flex;
  min-width: 0;
  flex: 1;
  align-items: center;
  justify-content: space-between;
  gap: var(--qlx-space-2);
}

.tree-demo__node-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.tree-demo__node-count {
  padding: 0 6px;
  border-radius: var(--qlx-radius-full);
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-700);
  font-size: 11px;
  line-height: 18px;
}

/* 窄屏降列：树在上、详情在下 */
@media (max-width: 1080px) {
  .tree-demo {
    flex-direction: column;
  }

  .tree-demo > :first-child {
    width: 100%;
  }
}
</style>
