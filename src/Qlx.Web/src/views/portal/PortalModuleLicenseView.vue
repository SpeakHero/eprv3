<!--
  所属公司：四川淇利馨集团有限公司
  描述：门户管理 · 模块许可——模块目录（版本/功能点）+ 租户授权矩阵 + 全局授权记录（规范 4.2.3）
  说明：授权编辑入口收敛于租户详情页，此处矩阵为全局视图 + 快捷跳转，避免双处维护同一编辑逻辑
-->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import { licenseRecordColumns } from '@/data/adminColumns'
import { fetchLicenseRecords, fetchModules, fetchTenants } from '@/api/admin'
import type { LicenseRecord, PortalModule, PortalTenant } from '@/types/admin'

interface MatrixRow {
  id: string
  tenantName: string
  tenantCode: string
  tenantStatus: PortalTenant['status']
  licensedModuleCodes: string[]
  /** 动态展开：moduleCode → 是否授权（列 prop 与模块编码一致）。 */
  [moduleCode: string]: unknown
}

const modules = ref<PortalModule[]>([])
const records = ref<LicenseRecord[]>([])
const loading = ref(true)

const moduleCards = computed(() =>
  modules.value.map((module) => ({
    ...module,
    featureText: module.features.join(' · '),
  })),
)

/** 矩阵列：租户固定两列 + 每模块一列（勾选标记）+ 操作列。 */
const matrixColumns = computed(() => [
  { prop: 'tenantName', label: '租户', minWidth: 220, fixed: 'left' as const },
  { prop: 'tenantCode', label: '编码', type: 'code' as const, width: 120 },
  ...modules.value.map((module) => ({
    prop: module.code,
    label: module.name,
    type: 'check' as const,
    width: 84,
    align: 'center' as const,
  })),
])

const matrixSource = ref<PortalTenant[]>([])

const matrixRows = computed<MatrixRow[]>(() => matrixSource.value.map((tenant) => ({
  id: tenant.id,
  tenantName: tenant.name,
  tenantCode: tenant.code,
  tenantStatus: tenant.status,
  licensedModuleCodes: tenant.licensedModuleCodes,
  ...Object.fromEntries(tenant.licensedModuleCodes.map((code) => [code, true])),
})))

const recordRows = computed(() => records.value)

async function load(): Promise<void> {
  loading.value = true
  try {
    const [moduleList, tenants, recordList] = await Promise.all([
      fetchModules(),
      fetchTenants({ page: 1, pageSize: 100 }),
      fetchLicenseRecords(),
    ])
    modules.value = moduleList
    matrixSource.value = tenants.items
    records.value = recordList
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <QlxPagePanel
    v-loading="loading"
    title="模块许可"
    subtitle="模块目录与租户授权总览：模块版本、功能点与各租户许可状态（规范 4.2.3）"
  >
    <!-- ============ 模块目录 ============ -->
    <section class="catalog">
      <h2 class="catalog__title">模块目录（{{ modules.length }} 个）</h2>
      <div class="catalog__grid">
        <article v-for="module in moduleCards" :key="module.code" class="catalog__card">
          <div class="catalog__card-head">
            <strong>{{ module.name }}</strong>
            <span class="catalog__version qlx-numeric">{{ module.version }}</span>
          </div>
          <p class="catalog__desc">{{ module.description }}</p>
          <p class="catalog__features">{{ module.featureText }}</p>
          <p class="catalog__count qlx-numeric">
            <AppIcon name="Finished" :size="12" />
            已授权 {{ module.licensedTenantCount }} 家租户
          </p>
        </article>
      </div>
    </section>

    <!-- ============ 租户授权矩阵 ============ -->
    <section class="matrix">
      <h2 class="matrix__title">租户授权矩阵</h2>
      <p class="matrix__hint">
        点击「调整授权」进入租户详情编辑模块与功能点授权，变更即时生效并写入授权记录。
      </p>
      <QlxDataTable
        :items="matrixRows"
        :columns="matrixColumns"
        :total="matrixRows.length"
        :page-size="10"
        :actions-width="110"
        empty-text="暂无租户"
      >
        <template #cell-tenantName="{ row }">
          <a class="matrix__tenant" :href="`/portal/tenants/${row.id}`" target="_blank" rel="noopener">
            {{ row.tenantName }}
          </a>
        </template>
        <template #actions="{ row }">
          <a class="matrix__edit" :href="`/portal/tenants/${row.id}`" target="_blank" rel="noopener">
            调整授权
          </a>
        </template>
      </QlxDataTable>
    </section>

    <!-- ============ 全局授权记录 ============ -->
    <section class="records">
      <h2 class="records__title">授权记录（{{ records.length }}）</h2>
      <QlxDataTable
        :items="recordRows"
        :columns="licenseRecordColumns"
        :total="recordRows.length"
        :page-size="10"
        empty-text="暂无授权变更记录"
      />
    </section>
  </QlxPagePanel>
</template>

<style scoped>
.catalog__title,
.matrix__title,
.records__title {
  margin-bottom: var(--qlx-space-3);
  padding-left: var(--qlx-space-3);
  border-left: 3px solid var(--qlx-gold-500);
  color: var(--qlx-brand-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-md);
  font-weight: 600;
  letter-spacing: 0.04em;
}

/* ============ 模块目录卡片 ============ */
.catalog__grid {
  display: grid;
  gap: var(--qlx-space-3);
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
}

.catalog__card {
  padding: var(--qlx-space-4);
  border: 1px solid var(--qlx-line);
  border-top: 2px solid var(--qlx-brand-600);
  border-radius: var(--qlx-radius-sm);
  background: var(--qlx-surface);
  transition: box-shadow var(--qlx-duration-fast) var(--qlx-ease-out);
}

.catalog__card:hover {
  box-shadow: var(--qlx-shadow-sm);
}

.catalog__card-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
}

.catalog__card-head strong {
  color: var(--qlx-ink-800);
  font-size: var(--qlx-text-base);
}

.catalog__version {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.catalog__desc {
  margin-top: var(--qlx-space-1);
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
}

.catalog__features {
  margin-top: var(--qlx-space-2);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

.catalog__count {
  display: flex;
  margin-top: var(--qlx-space-3);
  align-items: center;
  gap: 4px;
  color: var(--qlx-success);
  font-size: var(--qlx-text-xs);
}

/* ============ 矩阵与记录 ============ */
.matrix {
  margin-top: var(--qlx-space-8);
}

.matrix__hint,
.records__title + * {
  margin: 0;
}

.matrix__hint {
  margin-bottom: var(--qlx-space-3);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.records {
  margin-top: var(--qlx-space-8);
}

.matrix__tenant {
  color: var(--qlx-ink-800);
  font-weight: 500;
}

.matrix__tenant:hover {
  color: var(--qlx-brand-600);
}

.matrix__edit {
  color: var(--qlx-brand-600);
  font-size: var(--qlx-text-sm);
}

.matrix__edit:hover {
  text-decoration: underline;
}
</style>
