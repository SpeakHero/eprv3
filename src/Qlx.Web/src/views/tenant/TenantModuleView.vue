<!--
  所属公司：四川淇利馨集团有限公司
  描述：租户自管理 · 已授权模块——门户许可范围内的模块目录与入口（规范 5.2.4）
  说明：模块启停由门户许可控制（规范 5.1），租户侧仅查看授权结果与进入业务功能；
        未授权卡片灰化并提示联系集团管理员，禁止在租户侧自行开通。
-->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import { fetchTenantModules } from '@/api/tenantAdmin'
import { findModule, moduleHref } from '@/data/navigation'
import type { TenantModuleCard } from '@/types/tenant'
import { formatDate } from '@/utils/format'

type ModuleFilter = 'all' | 'licensed' | 'unlicensed'

interface ModuleCardView extends TenantModuleCard {
  icon: string
  /** 已授权模块的首个业务功能入口（原生新标签页打开）。 */
  entry: string | null
}

const loading = ref(true)
const modules = ref<TenantModuleCard[]>([])
const filter = ref<ModuleFilter>('all')

const cards = computed<ModuleCardView[]>(() =>
  modules.value.map((module) => {
    const nav = findModule(module.code)
    const firstItem = nav?.groups[0]?.items[0]
    return {
      ...module,
      icon: firstItem?.icon ?? 'Grid',
      entry: firstItem ? moduleHref(nav.code, firstItem.code) : null,
    }
  }),
)

const licensedCount = computed(() => modules.value.filter((module) => module.licensed).length)
const unlicensedCount = computed(() => modules.value.length - licensedCount.value)

const filterOptions = computed(() => [
  { value: 'all', label: `全部（${modules.value.length}）` },
  { value: 'licensed', label: `已授权（${licensedCount.value}）` },
  { value: 'unlicensed', label: `未开通（${unlicensedCount.value}）` },
])

const filteredCards = computed<ModuleCardView[]>(() => {
  if (filter.value === 'licensed') {
    return cards.value.filter((card) => card.licensed)
  }
  if (filter.value === 'unlicensed') {
    return cards.value.filter((card) => !card.licensed)
  }
  return cards.value
})

async function load(): Promise<void> {
  loading.value = true
  try {
    modules.value = await fetchTenantModules()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载模块许可失败')
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <QlxPagePanel
    title="已授权模块"
    subtitle="集团管理员授权的业务模块与功能入口；模块启停由门户许可控制（规范 5.2.4）"
  >
    <template #toolbar>
      <el-radio-group v-model="filter" size="small">
        <el-radio-button
          v-for="option in filterOptions"
          :key="option.value"
          :value="option.value"
        >
          {{ option.label }}
        </el-radio-button>
      </el-radio-group>
    </template>

    <p class="module-note">
      当前企业可使用 <strong class="qlx-numeric">{{ licensedCount }}</strong> /
      <span class="qlx-numeric">{{ modules.length }}</span> 个模块。
      未开通模块如需启用，请联系集团管理员在门户「租户详情 · 模块授权」中开通。
    </p>

    <div v-loading="loading" class="module-grid">
      <article
        v-for="card in filteredCards"
        :key="card.code"
        class="module-card"
        :class="{ 'is-unlicensed': !card.licensed }"
      >
        <header class="module-card__head">
          <span class="module-card__icon">
            <AppIcon :name="card.icon" :size="18" />
          </span>
          <div class="module-card__titles">
            <strong>{{ card.name }}</strong>
            <span class="qlx-numeric">{{ card.version }}</span>
          </div>
          <el-tag
            :type="card.licensed ? 'success' : 'info'"
            size="small"
            effect="light"
            class="module-card__status"
          >
            {{ card.licensed ? '已授权' : '未开通' }}
          </el-tag>
        </header>

        <p class="module-card__desc">{{ card.description }}</p>

        <ul class="module-card__features">
          <li v-for="feature in card.features" :key="feature">{{ feature }}</li>
        </ul>

        <footer class="module-card__foot">
          <span v-if="card.licensed" class="module-card__licensed">
            <AppIcon name="Finished" :size="12" />
            授权于 {{ formatDate(card.licensedAt) }}
          </span>
          <span v-else class="module-card__locked">
            <AppIcon name="Lock" :size="12" />
            未授权 · 请联系集团管理员
          </span>

          <a
            v-if="card.licensed && card.entry"
            class="module-card__entry"
            :href="card.entry"
            target="_blank"
            rel="noopener"
          >
            进入模块
            <AppIcon name="ArrowRight" :size="12" />
          </a>
        </footer>
      </article>

      <el-empty
        v-if="!loading && !filteredCards.length"
        class="module-empty"
        :description="filter === 'unlicensed' ? '全部模块均已授权' : '暂无授权模块'"
      />
    </div>
  </QlxPagePanel>
</template>

<style scoped>
.module-note {
  margin-bottom: var(--qlx-space-4);
  padding: var(--qlx-space-2) var(--qlx-space-3);
  border-left: 3px solid var(--qlx-gold-500);
  background: var(--qlx-canvas);
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

.module-note strong {
  color: var(--qlx-brand-700);
  font-weight: 600;
}

.module-grid {
  display: grid;
  min-height: 200px;
  gap: var(--qlx-space-4);
  grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
}

.module-card {
  display: flex;
  flex-direction: column;
  padding: var(--qlx-space-4);
  border: 1px solid var(--qlx-line);
  border-top: 2px solid var(--qlx-brand-600);
  border-radius: var(--qlx-radius-sm);
  background: var(--qlx-surface);
  transition:
    box-shadow var(--qlx-duration-fast) var(--qlx-ease-out),
    border-color var(--qlx-duration-fast) var(--qlx-ease-out);
}

.module-card:hover {
  box-shadow: var(--qlx-shadow-sm);
}

.module-card.is-unlicensed {
  border-top-color: var(--qlx-ink-200);
  background: var(--qlx-canvas);
}

.module-card__head {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-3);
}

.module-card__icon {
  display: inline-flex;
  width: 38px;
  height: 38px;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  border-radius: var(--qlx-radius-sm);
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-700);
}

.module-card.is-unlicensed .module-card__icon {
  background: var(--qlx-canvas-deep);
  color: var(--qlx-ink-400);
}

.module-card__titles {
  display: flex;
  min-width: 0;
  flex: 1;
  flex-direction: column;
  line-height: 1.4;
}

.module-card__titles strong {
  color: var(--qlx-ink-800);
  font-size: var(--qlx-text-base);
  font-weight: 600;
}

.module-card.is-unlicensed .module-card__titles strong {
  color: var(--qlx-ink-500);
}

.module-card__titles span {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.module-card__status {
  flex: 0 0 auto;
  border: none;
}

.module-card__desc {
  margin-top: var(--qlx-space-3);
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

.module-card__features {
  display: flex;
  margin-top: var(--qlx-space-3);
  flex-wrap: wrap;
  gap: 6px;
}

.module-card__features li {
  padding: 2px 8px;
  border: 1px solid var(--qlx-brand-100);
  border-radius: var(--qlx-radius-full);
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-700);
  font-size: 11px;
  line-height: 18px;
}

.module-card.is-unlicensed .module-card__features li {
  border-color: var(--qlx-ink-100);
  background: var(--qlx-canvas-deep);
  color: var(--qlx-ink-400);
}

.module-card__foot {
  display: flex;
  margin-top: auto;
  padding-top: var(--qlx-space-4);
  align-items: center;
  justify-content: space-between;
  gap: var(--qlx-space-2);
}

.module-card__licensed,
.module-card__locked {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: var(--qlx-text-xs);
}

.module-card__licensed {
  color: var(--qlx-success);
}

.module-card__locked {
  color: var(--qlx-ink-400);
}

.module-card__entry {
  display: inline-flex;
  padding: 4px 10px;
  border: 1px solid var(--qlx-brand-700);
  border-radius: var(--qlx-radius-xs);
  color: var(--qlx-brand-700);
  font-size: var(--qlx-text-xs);
  align-items: center;
  gap: 4px;
  transition: background var(--qlx-duration-fast) var(--qlx-ease-out);
}

.module-card__entry:hover {
  background: var(--qlx-brand-700);
  color: #fff;
}

.module-empty {
  grid-column: 1 / -1;
}
</style>
