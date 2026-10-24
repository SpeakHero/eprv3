<!--
  所属公司：四川淇利馨集团有限公司
  描述：门户管理 · 租户详情——基础信息 / 模块授权（功能点矩阵）/ 授权记录 / 成员（只读）
  说明：成员管理属租户自管理域（规范 5.1），门户侧仅查看
-->
<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import QlxDescriptionList from '@/components/crud/QlxDescriptionList.vue'
import type { DescriptionItem } from '@/components/crud/QlxDescriptionList.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import { licenseRecordColumns } from '@/data/adminColumns'
import { createSseClient } from '@/core/sse'
import type { SseClient } from '@/core/sse'
import { loadSession } from '@/core/session'
import {
  fetchModules,
  fetchTenantDetail,
  fetchTenantMembers,
  fetchTenantUpgradeJob,
  promoteTenantToIsolated,
  retryTenantUpgradeJob,
  saveTenantLicense,
  tenantProvisioningStreamUrl,
} from '@/api/admin'
import {
  databaseModeLabels,
  dbStatusLabels,
  memberLevelLabels,
  memberRoleLabels,
  tenantStatusLabels,
  tenantStatusTones,
} from '@/types/admin'
import type {
  LicenseRecord,
  PortalModule,
  PortalTenant,
  PortalTenantDatabaseSummary,
  TenantMembership,
  TenantUpgradeJobItem,
} from '@/types/admin'
import { formatDateTime } from '@/utils/format'
import {
  clampProvisioningPercent,
  isProvisioningFinished,
  provisioningStepLabel,
} from '@/utils/provisioning'
import type { TenantProvisioningProgressPayload } from '@/utils/provisioning'

const route = useRoute()
const tenantId = route.params.id as string

const tenant = ref<PortalTenant | null>(null)
const database = ref<PortalTenantDatabaseSummary | null>(null)
const upgradeJob = ref<TenantUpgradeJobItem | null>(null)
const modules = ref<PortalModule[]>([])
const records = ref<LicenseRecord[]>([])
const members = ref<TenantMembership[]>([])
const loading = ref(true)

/** 升级进度轮询计时器（Promise 轮询，3 秒一次，Done/Failed/无任务时停）。 */
let upgradePollTimer: number | null = null

/** 数据库预配进度（SSE 实时感知；null 表示尚未收到进度事件）。 */
const provisioning = ref<{ step: string; progress: number; status: string } | null>(null)

/** 预配进度 SSE 客户端（幂等：存在即复用）。 */
let provisioningClient: SseClient | null = null
/** SSE 建立后 8 秒仍未 open 的降级定时器。 */
let provisioningFallbackTimer: number | null = null
/** 实时通道不可用时的 30 秒轮询兜底定时器。 */
let provisioningPollTimer: number | null = null
/** SSE 是否曾成功 open（open 后不再触发降级）。 */
let provisioningSseOpened = false

/** 授权编辑态（进入页面时从租户当前授权初始化）。 */
const selectedModules = ref<string[]>([])
const remark = ref('')
const saving = ref(false)
const activeTab = ref('license')

/** 是否 Shared 模式（仅 Shared 可发起升级）。 */
const isSharedMode = computed(() => tenant.value?.databaseMode === 'Shared')

/** 是否处于数据库预配中（租户待激活或库状态初始化中）。 */
const isProvisioning = computed(
  () => tenant.value?.status === 'Pending' || tenant.value?.dbStatus === 'Provisioning',
)

/** 是否已有进行中或最近升级任务（可重试仅 Failed）。 */
const canRetryUpgrade = computed(
  () => upgradeJob.value?.status === 'Failed' && !isSharedMode.value === false && tenant.value?.databaseMode === 'Shared' ? false : upgradeJob.value?.status === 'Failed',
)

const descriptionItems = computed<DescriptionItem[]>(() => {
  if (!tenant.value) {
    return []
  }
  const items: DescriptionItem[] = [
    { label: '租户名称', value: tenant.value.name },
    { label: '租户编码', value: tenant.value.code },
    { label: '统一社会信用代码', value: tenant.value.taxNo },
    { label: '会员等级', value: memberLevelLabels[tenant.value.memberLevel] },
    {
      label: '会员到期',
      value: tenant.value.memberExpireAt ? formatDateTime(tenant.value.memberExpireAt) : '长期有效（预留）',
    },
    { label: '数据库模式', value: databaseModeLabels[tenant.value.databaseMode] },
    { label: '库状态', value: dbStatusText(tenant.value.dbStatus) },
    { label: '创建时间', value: formatDateTime(tenant.value.createdAt) },
    { label: '最近登录', value: formatDateTime(tenant.value.lastLoginAt) },
  ]
  if (tenant.value.dbError) {
    items.push({ label: '预配错误', value: tenant.value.dbError, full: true })
  }
  return items
})

function dbStatusText(status: PortalTenant['dbStatus']): string {
  return dbStatusLabels[status]
}

/** 升级任务状态中文标签。 */
function upgradeStatusLabel(status: string | null): string {
  if (!status) return '暂无任务'
  return status
}

/** 升级任务是否仍在运行（Preparing/Copying/Verifying/Cutover 属于进行中）。 */
const isUpgradeRunning = computed(() => {
  const s = upgradeJob.value?.status
  return s === 'Preparing' || s === 'Copying' || s === 'Verifying' || s === 'Cutover'
})

/** 升级流水线整体进度百分比（按阶段加权：Pending=0 Preparing=15 Copying=50 Verifying=75 Cutover=90 Done=100 Failed 保留上值）。 */
const upgradeProgressPercent = computed(() => {
  if (!upgradeJob.value) return 0
  const map: Record<string, number> = {
    Pending: 5,
    Preparing: 15,
    Copying: 50,
    Verifying: 75,
    Cutover: 90,
    Done: 100,
  }
  return map[upgradeJob.value.status] ?? 0
})

async function load(silent = false): Promise<void> {
  if (!silent) {
    loading.value = true
  }
  try {
    const [detail, modulesList, membersList, job] = await Promise.all([
      fetchTenantDetail(tenantId),
      fetchModules(),
      fetchTenantMembers(tenantId),
      fetchTenantUpgradeJob(tenantId),
    ])
    tenant.value = detail.tenant
    database.value = detail.database
    records.value = detail.records
    modules.value = modulesList
    members.value = membersList
    upgradeJob.value = job
    selectedModules.value = [...detail.tenant.licensedModuleCodes]
    if (isUpgradeRunning.value) {
      startUpgradePolling()
    }
    if (isProvisioning.value) {
      startProvisioningWatch()
    } else {
      stopProvisioningWatch()
      provisioning.value = null
    }
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载租户详情失败')
  } finally {
    if (!silent) {
      loading.value = false
    }
  }
}

/** 启动升级进度轮询（3 秒一次；Done/Failed/无任务时自动停）。 */
function startUpgradePolling(): void {
  stopUpgradePolling()
  upgradePollTimer = window.setInterval(async () => {
    try {
      const job = await fetchTenantUpgradeJob(tenantId)
      upgradeJob.value = job
      if (job && (job.status === 'Done' || job.status === 'Failed')) {
        stopUpgradePolling()
        // 任务结束后整页刷新（租户 databaseMode / dbStatus 可能已变）
        await load()
      } else if (!job) {
        stopUpgradePolling()
        // 任务被清理掉了——降级整页刷新
        await load()
      }
    } catch {
      // 轮询静默失败，等下一轮
    }
  }, 3000)
}

function stopUpgradePolling(): void {
  if (upgradePollTimer !== null) {
    clearInterval(upgradePollTimer)
    upgradePollTimer = null
  }
}

/** 启动数据库预配进度感知（SSE 实时通道 + 8 秒未连通降级为 30 秒轮询；幂等）。 */
function startProvisioningWatch(): void {
  if (provisioningClient) {
    return
  }
  provisioningSseOpened = false
  const client = createSseClient({
    transport: 'fetch',
    url: tenantProvisioningStreamUrl(tenantId),
    getToken: () => loadSession().identityToken,
    heartbeatTimeoutMs: 45000,
  })
  provisioningClient = client

  client.subscribe('TenantProvisioningProgress', (event) => {
    const data = event.data as TenantProvisioningProgressPayload
    provisioning.value = {
      step: data.step ?? '',
      progress: clampProvisioningPercent(data.progress),
      status: data.status ?? '',
    }
    if (isProvisioningFinished(data.status)) {
      stopProvisioningWatch()
      void load(true)
    }
  })

  client.onStateChange((state) => {
    // 已主动关闭 / 已被新一轮监听替换：忽略迟到的状态回调
    if (provisioningClient !== client) {
      return
    }
    if (state === 'open') {
      provisioningSseOpened = true
      clearProvisioningFallbackTimer()
      stopProvisioningPolling()
    } else if (state === 'closed' && isProvisioning.value) {
      startProvisioningPolling()
    }
  })

  provisioningFallbackTimer = window.setTimeout(() => {
    provisioningFallbackTimer = null
    if (!provisioningSseOpened && isProvisioning.value) {
      startProvisioningPolling()
    }
  }, 8000)
}

/** 停止预配进度感知（保留已展示进度，避免轮询停止时进度条闪回 0）。 */
function stopProvisioningWatch(): void {
  const client = provisioningClient
  provisioningClient = null
  client?.close()
  clearProvisioningFallbackTimer()
  stopProvisioningPolling()
  provisioningSseOpened = false
}

/** 启动 30 秒静默回刷（实时通道不可用时兜底）。 */
function startProvisioningPolling(): void {
  if (provisioningPollTimer !== null) {
    return
  }
  void load(true)
  provisioningPollTimer = window.setInterval(() => {
    void load(true)
  }, 30000)
}

function stopProvisioningPolling(): void {
  if (provisioningPollTimer !== null) {
    clearInterval(provisioningPollTimer)
    provisioningPollTimer = null
  }
}

function clearProvisioningFallbackTimer(): void {
  if (provisioningFallbackTimer !== null) {
    clearTimeout(provisioningFallbackTimer)
    provisioningFallbackTimer = null
  }
}

async function onPromote(): Promise<void> {
  if (!tenant.value) return
  try {
    await ElMessageBox.confirm(
      `升级后「${tenant.value.name}」将从共享库切换为独立库，数据复制与校验由后台流水线异步执行（预计 1-10 分钟）。\n\n⚠ 升级期间租户业务仍正常，但切换瞬间会有约 5 秒短暂停机。\n⚠ 升级完成后独立库无法回退为共享库。\n⚠ 共享库残留数据会在完成 30 天后自动清理。\n\n确认发起升级？`,
      '发起升级确认',
      { confirmButtonText: '确认发起', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    const job = await promoteTenantToIsolated(tenantId)
    upgradeJob.value = job
    activeTab.value = 'upgrade'
    ElMessage.success('升级任务已登记，后台执行中')
    startUpgradePolling()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '发起升级失败')
  }
}

async function onRetryUpgrade(): Promise<void> {
  if (!upgradeJob.value) return
  try {
    const job = await retryTenantUpgradeJob(tenantId)
    upgradeJob.value = job
    ElMessage.success('升级任务已重新进入队列')
    startUpgradePolling()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '重试失败')
  }
}

function toggleModule(code: string, checked: boolean): void {
  selectedModules.value = checked
    ? [...selectedModules.value, code]
    : selectedModules.value.filter((item) => item !== code)
}

async function onSaveLicense(): Promise<void> {
  if (!tenant.value) {
    return
  }
  saving.value = true
  try {
    await saveTenantLicense({
      tenantId: tenant.value.id,
      moduleCodes: [...selectedModules.value],
      remark: remark.value,
    })
    remark.value = ''
    ElMessage.success('模块授权已保存，许可变更即时生效（路由/菜单/任务/事件随许可联动）')
    await load()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存授权失败')
  } finally {
    saving.value = false
  }
}

const recordTableColumns = licenseRecordColumns

const memberColumns = [
  { prop: 'displayNameInTenant', label: '成员名（租户内）', width: 160 },
  { prop: 'loginAccount', label: '登录账号', width: 140 },
  { prop: 'memberRole', label: '角色', type: 'enum' as const, width: 110, enumLabels: memberRoleLabels },
  { prop: 'memberStatus', label: '成员状态', width: 96, align: 'center' as const },
  { prop: 'joinAt', label: '加入时间', type: 'time' as const, width: 150 },
  { prop: 'lastLoginAt', label: '最近登录', type: 'time' as const, width: 150 },
]

const memberRows = computed(() =>
  members.value.map((member) => ({
    ...member,
    memberStatus: member.memberStatus === 'Enabled' ? '启用' : '已停用',
  })),
)

const recordRows = computed(() => records.value)

onMounted(load)
onBeforeUnmount(() => {
  stopUpgradePolling()
  stopProvisioningWatch()
})
</script>

<template>
  <QlxPagePanel v-loading="loading" title="租户详情" subtitle="基础信息与模块许可（授权变更即时生效并写入授权记录）">
    <template #toolbar>
      <el-tag
        v-if="tenant"
        :type="tenantStatusTones[tenant.status]"
        size="large"
        effect="light"
        class="status-tag"
      >
        {{ tenantStatusLabels[tenant.status] }}
      </el-tag>
      <a class="back-link" href="/portal/tenants" target="_blank" rel="noopener">
        <AppIcon name="ArrowLeft" :size="14" />
        返回列表
      </a>
    </template>

    <QlxDescriptionList v-if="tenant" :items="descriptionItems" />

    <!-- ============ 数据库卡片（模式 + 摘要 + 操作） ============ -->
    <section v-if="database" class="db-card">
      <div class="db-card__head">
        <div>
          <h3 class="db-card__title">数据库</h3>
          <el-tag
            :type="tenant?.databaseMode === 'Isolated' ? 'success' : 'primary'"
            effect="dark"
            size="small"
          >
            {{ databaseModeLabels[tenant?.databaseMode ?? 'Shared'] }}
          </el-tag>
          <el-tag class="db-card__status-tag" effect="plain" size="small">
            {{ dbStatusText(tenant?.dbStatus ?? 'Ready') }}
          </el-tag>
        </div>
        <template v-if="isSharedMode && tenant?.status === 'Enabled'">
          <el-button
            type="primary"
            size="small"
            :loading="isUpgradeRunning"
            @click="onPromote"
          >
            {{ isUpgradeRunning ? '升级进行中' : '升级为独立库' }}
          </el-button>
        </template>
      </div>
      <div class="db-card__grid">
        <div><span class="qlx-label">数据库名</span><code>{{ database.dbName }}</code></div>
        <div><span class="qlx-label">主机</span><code>{{ database.dbHost }}:{{ database.dbPort }}</code></div>
        <div><span class="qlx-label">连接池</span><code>{{ database.poolCode }}</code></div>
        <div><span class="qlx-label">模式</span>{{ database.mode === 'Isolated' ? '独立库（按租户隔离）' : '租户共享库' }}</div>
      </div>
      <p v-if="tenant?.databaseMode === 'Shared'" class="db-card__hint">
        当前租户数据存储在共享库中，切换独立库可获得更强隔离性与独立资源；
        升级由后台流水线自动完成，支持断点续跑与失败重试。
      </p>
    </section>

    <!-- ============ 数据库预配进度（SSE 实时 + 30 秒轮询兜底） ============ -->
    <section v-if="isProvisioning" class="provisioning-card">
      <div class="provisioning-card__head">
        <div>
          <h3 class="provisioning-card__title">数据库预配进度</h3>
          <el-tag type="warning" effect="dark" size="small">初始化中</el-tag>
          <span class="provisioning-card__step">{{ provisioning ? provisioningStepLabel(provisioning.step) : '等待预配进度' }}</span>
        </div>
        <span class="provisioning-card__percent qlx-numeric">{{ provisioning?.progress ?? 0 }}%</span>
      </div>
      <el-progress :percentage="provisioning?.progress ?? 0" :show-text="false" :stroke-width="10" class="provisioning-card__bar" />
      <p class="provisioning-card__hint">
        预配由后台流水线自动完成（建库 → 按模块迁移 → 激活），完成后本页自动刷新；实时通道不可用时每 30 秒自动刷新一次。
      </p>
    </section>

    <el-tabs v-model="activeTab" class="detail-tabs">
      <!-- ============ 模块授权 ============ -->
      <el-tab-pane label="模块授权" name="license">
        <div class="license-grid">
          <label
            v-for="module in modules"
            :key="module.code"
            class="license-card"
            :class="{ 'is-checked': selectedModules.includes(module.code) }"
          >
            <input
              type="checkbox"
              class="license-card__check"
              :checked="selectedModules.includes(module.code)"
              @change="toggleModule(module.code, ($event.target as HTMLInputElement).checked)"
            />
            <div class="license-card__body">
              <div class="license-card__head">
                <strong>{{ module.name }}</strong>
                <span class="qlx-numeric">{{ module.version }}</span>
              </div>
              <p class="license-card__desc">{{ module.description }}</p>
              <p class="license-card__features">{{ module.features.join(' · ') }}</p>
            </div>
          </label>
        </div>

        <div class="license-actions">
          <el-input
            v-model="remark"
            placeholder="授权变更说明（写入授权记录，可空）"
            maxlength="120"
            style="width: 360px"
          />
          <el-button type="primary" :loading="saving" @click="onSaveLicense">保存授权</el-button>
          <span class="license-actions__count qlx-numeric">
            已选 {{ selectedModules.length }} / {{ modules.length }} 个模块
          </span>
        </div>
      </el-tab-pane>

      <!-- ============ 授权记录 ============ -->
      <el-tab-pane :label="`授权记录（${records.length}）`" name="records">
        <QlxDataTable
          :items="recordRows"
          :columns="recordTableColumns"
          :total="recordRows.length"
          :page-size="10"
          empty-text="暂无授权变更记录"
        />
      </el-tab-pane>

      <!-- ============ 成员（只读） ============ -->
      <el-tab-pane :label="`租户成员（${members.length}）`" name="members">
        <p class="member-note">
          门户不参与租户内部成员管理（规范 5.1 职责边界），此处仅展示成员关系概览；
          成员的组织归属、角色与权限由租户管理员自行维护。
        </p>
        <QlxDataTable :items="memberRows" :columns="memberColumns" :total="memberRows.length" :page-size="10" empty-text="暂无成员" />
      </el-tab-pane>

      <!-- ============ 升级进度 ============ -->
      <el-tab-pane label="升级为独立库" name="upgrade">
        <div v-if="!upgradeJob" class="upgrade-empty">
          <el-empty description="暂无升级任务" />
          <p class="upgrade-empty__hint">
            仅共享库租户可发起升级，升级完成后共享库残留数据将在 30 天后自动清理。
          </p>
          <el-button
            v-if="isSharedMode && tenant?.status === 'Enabled'"
            type="primary"
            @click="onPromote"
          >
            发起升级
          </el-button>
        </div>

        <div v-else class="upgrade-progress">
          <div class="upgrade-progress__head">
            <div>
              <el-tag
                :type="upgradeJob.status === 'Done' ? 'success' : upgradeJob.status === 'Failed' ? 'danger' : 'warning'"
                effect="dark"
              >
                {{ upgradeJob.status }}
              </el-tag>
              <span class="upgrade-progress__meta">
                {{ upgradeStatusLabel(upgradeJob.status) }}
                <template v-if="upgradeJob.currentTable"> · 当前表 <code>{{ upgradeJob.currentTable }}</code></template>
                <template v-if="upgradeJob.moduleCode"> · 模块 <code>{{ upgradeJob.moduleCode }}</code></template>
              </span>
            </div>
            <div class="upgrade-progress__ops">
              <el-button
                v-if="canRetryUpgrade"
                type="warning"
                size="small"
                @click="onRetryUpgrade"
              >
                重试
              </el-button>
              <el-button
                v-if="isUpgradeRunning"
                size="small"
                :loading="isUpgradeRunning"
                @click="load()"
              >
                刷新
              </el-button>
            </div>
          </div>

          <el-progress
            :percentage="upgradeProgressPercent"
            :status="upgradeJob.status === 'Done' ? 'success' : upgradeJob.status === 'Failed' ? 'exception' : ''"
            :stroke-width="12"
            class="upgrade-progress__bar"
          />

          <div class="upgrade-progress__stats">
            <div><span>已复制行数</span><strong class="qlx-numeric">{{ upgradeJob.rowsCopied.toLocaleString() }}</strong></div>
            <div><span>重试次数</span><strong>{{ upgradeJob.retryCount }}</strong></div>
            <div v-if="upgradeJob.startedAt"><span>创建时间</span><strong>{{ formatDateTime(upgradeJob.startedAt) }}</strong></div>
            <div v-if="upgradeJob.completedAt"><span>完成时间</span><strong>{{ formatDateTime(upgradeJob.completedAt) }}</strong></div>
          </div>

          <p v-if="upgradeJob.errorMessage" class="upgrade-progress__error">
            ⚠ {{ upgradeJob.errorMessage }}
          </p>
        </div>
      </el-tab-pane>
    </el-tabs>
  </QlxPagePanel>
</template>

<style scoped>
.status-tag {
  border: none;
}

.back-link {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: var(--qlx-brand-600);
  font-size: var(--qlx-text-sm);
}

.back-link:hover {
  text-decoration: underline;
}

.detail-tabs {
  margin-top: var(--qlx-space-5);
}

/* ============ 模块授权矩阵 ============ */
.license-grid {
  display: grid;
  gap: var(--qlx-space-3);
  grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
}

.license-card {
  display: flex;
  gap: var(--qlx-space-3);
  padding: var(--qlx-space-3) var(--qlx-space-4);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-sm);
  background: var(--qlx-surface);
  cursor: pointer;
  transition: border-color var(--qlx-duration-fast) var(--qlx-ease-out), box-shadow var(--qlx-duration-fast) var(--qlx-ease-out);
}

.license-card:hover {
  border-color: var(--qlx-brand-300);
}

.license-card.is-checked {
  border-color: var(--qlx-brand-600);
  box-shadow: inset 0 0 0 1px var(--qlx-brand-600);
}

.license-card__check {
  margin-top: 3px;
  accent-color: var(--qlx-brand-600);
}

.license-card__head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--qlx-space-2);
}

.license-card__head strong {
  color: var(--qlx-ink-800);
  font-size: var(--qlx-text-base);
}

.license-card__head span {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.license-card__desc {
  margin-top: 2px;
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
}

.license-card__features {
  margin-top: var(--qlx-space-2);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.6;
}

.license-actions {
  display: flex;
  margin-top: var(--qlx-space-4);
  padding-top: var(--qlx-space-4);
  border-top: 1px dashed var(--qlx-ink-200);
  align-items: center;
  gap: var(--qlx-space-3);
}

.license-actions__count {
  margin-left: auto;
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-sm);
}

/* ============ 成员 ============ */
.member-note {
  margin-bottom: var(--qlx-space-3);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

/* ============ 数据库卡片 ============ */
.db-card {
  margin-top: var(--qlx-space-5);
  padding: var(--qlx-space-4);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-sm);
  background: var(--qlx-surface);
}

.db-card__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--qlx-space-3);
}

.db-card__title {
  margin: 0;
  margin-right: var(--qlx-space-2);
  font-size: var(--qlx-text-base);
  font-weight: 600;
  color: var(--qlx-ink-800);
}

.db-card__status-tag {
  margin-left: var(--qlx-space-2);
}

.db-card__grid {
  display: grid;
  margin-top: var(--qlx-space-3);
  gap: var(--qlx-space-2) var(--qlx-space-4);
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
}

.db-card__grid > div {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  font-size: var(--qlx-text-sm);
  color: var(--qlx-ink-700);
}

.db-card__grid code {
  padding: 1px 6px;
  border-radius: 4px;
  background: var(--qlx-ink-100);
  font-size: var(--qlx-text-xs);
}

.db-card__hint {
  margin: var(--qlx-space-3) 0 0;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.6;
}

/* ============ 数据库预配进度 ============ */
.provisioning-card {
  margin-top: var(--qlx-space-5);
  padding: var(--qlx-space-4);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-sm);
  background: var(--qlx-surface);
}

.provisioning-card__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--qlx-space-3);
}

.provisioning-card__title {
  margin: 0;
  margin-right: var(--qlx-space-2);
  font-size: var(--qlx-text-base);
  font-weight: 600;
  color: var(--qlx-ink-800);
}

.provisioning-card__step {
  margin-left: var(--qlx-space-2);
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
}

.provisioning-card__percent {
  color: var(--qlx-warning);
  font-size: var(--qlx-text-base);
  font-weight: 600;
}

.provisioning-card__bar {
  margin-top: var(--qlx-space-3);
}

.provisioning-card__hint {
  margin: var(--qlx-space-3) 0 0;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.6;
}

/* ============ 升级进度 ============ */
.upgrade-empty {
  padding: var(--qlx-space-5);
  text-align: center;
}

.upgrade-empty__hint {
  margin: var(--qlx-space-3) 0;
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-sm);
}

.upgrade-progress__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--qlx-space-3);
}

.upgrade-progress__meta {
  margin-left: var(--qlx-space-2);
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
}

.upgrade-progress__meta code {
  padding: 1px 5px;
  border-radius: 4px;
  background: var(--qlx-ink-100);
  font-size: var(--qlx-text-xs);
}

.upgrade-progress__ops {
  display: flex;
  gap: var(--qlx-space-2);
}

.upgrade-progress__bar {
  margin-top: var(--qlx-space-4);
}

.upgrade-progress__stats {
  display: grid;
  margin-top: var(--qlx-space-3);
  gap: var(--qlx-space-2) var(--qlx-space-4);
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
}

.upgrade-progress__stats > div {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
}

.upgrade-progress__stats span {
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
}

.upgrade-progress__error {
  margin: var(--qlx-space-3) 0 0;
  padding: var(--qlx-space-2) var(--qlx-space-3);
  border-radius: var(--qlx-radius-sm);
  background: rgba(var(--qlx-danger-rgb), 0.08);
  color: var(--qlx-danger);
  font-size: var(--qlx-text-sm);
}
</style>
