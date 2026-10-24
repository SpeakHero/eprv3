<!--
  所属公司：四川淇利馨集团有限公司
  描述：后台任务中心——任务定义 / 执行历史 / 执行日志（规范 支撑功能 03-后台任务中心）
  说明：任务定义支持启停；执行历史支持按任务编码 / 状态 / 时间过滤；
        点击执行记录查看日志（时间升序）；支持触发任务与取消。
-->
<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import {
  cancelJob,
  fetchJobDefinitions,
  fetchJobHistory,
  fetchJobLogs,
  toggleJobDefinition,
  triggerJob,
} from '@/api/support'
import type {
  JobDefinitionItem,
  JobExecutionItem,
  JobExecutionLogEntry,
  JobSubmitResult,
  SupportPage,
} from '@/types/support'
import { JOB_STATUS_LABELS, JOB_STATUS_TYPE } from '@/types/support'
import { formatDateTime, labelOf } from '@/utils/format'

type JobTab = 'definitions' | 'history'

const activeTab = ref<JobTab>('definitions')

/* ============ 任务定义 ============ */
const definitions = ref<JobDefinitionItem[]>([])
const defLoading = ref(false)

async function loadDefinitions(): Promise<void> {
  defLoading.value = true
  try {
    definitions.value = await fetchJobDefinitions()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载任务定义失败')
  } finally {
    defLoading.value = false
  }
}

async function handleToggleDef(item: JobDefinitionItem): Promise<void> {
  try {
    await toggleJobDefinition(item.taskCode, !item.enabled)
    item.enabled = !item.enabled
    ElMessage.success(item.enabled ? '已启用' : '已停用')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

/* 触发任务 */
const triggerVisible = ref(false)
const triggerForm = ref({ taskCode: '', mode: 'sync' as 'sync' | 'async', parameters: '' })

function openTrigger(): void {
  triggerForm.value = { taskCode: '', mode: 'sync', parameters: '' }
  triggerVisible.value = true
}

async function submitTrigger(): Promise<void> {
  if (!triggerForm.value.taskCode) {
    ElMessage.warning('请选择任务编码')
    return
  }
  try {
    let params: Record<string, unknown> | null = null
    if (triggerForm.value.parameters.trim()) {
      params = JSON.parse(triggerForm.value.parameters) as Record<string, unknown>
    }
    const result: JobSubmitResult = await triggerJob({
      taskCode: triggerForm.value.taskCode,
      mode: triggerForm.value.mode,
      parameters: params,
    })
    ElMessage.success(
      result.mode === 'sync' ? `执行完成：${JOB_STATUS_LABELS[result.status]}` : `已入队，执行 ID：${result.executionId}`,
    )
    triggerVisible.value = false
    if (activeTab.value === 'history') void loadHistory()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '触发失败')
  }
}

/* ============ 执行历史 ============ */
const history = ref<JobExecutionItem[]>([])
const historyTotal = ref(0)
const historyPage = ref(1)
const historyPageSize = ref(20)
const filterTaskCode = ref('')
const filterStatus = ref('')
const historyLoading = ref(false)

async function loadHistory(): Promise<void> {
  historyLoading.value = true
  try {
    const page: SupportPage<JobExecutionItem> = await fetchJobHistory({
      taskCode: filterTaskCode.value || null,
      status: filterStatus.value || null,
      from: null,
      to: null,
      pageIndex: historyPage.value,
      pageSize: historyPageSize.value,
    })
    history.value = page.items
    historyTotal.value = page.total
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载执行历史失败')
  } finally {
    historyLoading.value = false
  }
}

async function handleCancel(executionId: string): Promise<void> {
  try {
    await cancelJob(executionId)
    ElMessage.success('已取消')
    void loadHistory()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '取消失败')
  }
}

/* 执行日志抽屉 */
const logVisible = ref(false)
const logLoading = ref(false)
const logs = ref<JobExecutionLogEntry[]>([])

async function openLogs(executionId: string): Promise<void> {
  logVisible.value = true
  logLoading.value = true
  try {
    logs.value = await fetchJobLogs(executionId)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载日志失败')
  } finally {
    logLoading.value = false
  }
}

const LOG_LEVEL_COLOR: Record<string, string> = {
  Debug: 'var(--qlx-ink-400)',
  Info: 'var(--qlx-brand-700)',
  Warning: 'var(--qlx-warning)',
  Error: 'var(--qlx-danger)',
}

function switchTab(tab: JobTab): void {
  activeTab.value = tab
  if (tab === 'definitions') void loadDefinitions()
  if (tab === 'history') void loadHistory()
}

onMounted(() => {
  void loadDefinitions()
})
</script>

<template>
  <QlxPagePanel
    title="后台任务中心"
    subtitle="任务定义启停 / 执行历史查询 / 执行日志查看——所有耗时任务经 IJobScheduler 统一调度（SYNC 阻塞 / ASYNC 入队）"
  >
    <template #toolbar>
      <el-button type="primary" @click="openTrigger">触发任务</el-button>
    </template>

    <el-tabs v-model="activeTab" @tab-change="(name: string) => switchTab(name as JobTab)">
      <!-- 任务定义 -->
      <el-tab-pane label="任务定义" name="definitions">
        <el-table :data="definitions" v-loading="defLoading" stripe style="width: 100%">
          <el-table-column prop="taskCode" label="任务编码" min-width="220" />
          <el-table-column prop="name" label="名称" min-width="160" />
          <el-table-column prop="moduleCode" label="所属模块" width="120" />
          <el-table-column prop="effectiveCron" label="生效 CRON" min-width="180" />
          <el-table-column prop="defaultMode" label="默认模式" width="100" />
          <el-table-column label="状态" width="100">
            <template #default="{ row }">
              <el-tag v-if="row.paused" type="warning" size="small">熔断暂停</el-tag>
              <el-tag v-else :type="row.enabled ? 'success' : 'info'" size="small">
                {{ row.enabled ? '启用' : '停用' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="nextRunAt" label="下次执行" width="170">
            <template #default="{ row }">{{ formatDateTime(row.nextRunAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="120" fixed="right">
            <template #default="{ row }">
              <el-switch :model-value="row.enabled" @change="handleToggleDef(row)" />
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <!-- 执行历史 -->
      <el-tab-pane label="执行历史" name="history">
        <div class="job-toolbar">
          <el-input
            v-model="filterTaskCode"
            placeholder="任务编码"
            clearable
            style="width: 200px"
            @keyup.enter="loadHistory"
          />
          <el-select v-model="filterStatus" placeholder="状态" clearable style="width: 140px" @change="loadHistory">
            <el-option label="已登记" value="Pending" />
            <el-option label="已入队" value="Queued" />
            <el-option label="执行中" value="Running" />
            <el-option label="成功" value="Success" />
            <el-option label="失败" value="Failed" />
            <el-option label="超时" value="Timeout" />
            <el-option label="已取消" value="Canceled" />
          </el-select>
          <el-button type="primary" plain @click="loadHistory">查询</el-button>
        </div>
        <el-table :data="history" v-loading="historyLoading" stripe style="width: 100%">
          <el-table-column prop="executionId" label="执行 ID" width="200">
            <template #default="{ row }">
              <span class="mono">{{ row.executionId }}</span>
            </template>
          </el-table-column>
          <el-table-column prop="taskCode" label="任务编码" min-width="180" />
          <el-table-column label="模式" width="80">
            <template #default="{ row }">{{ row.mode }}</template>
          </el-table-column>
          <el-table-column label="状态" width="100">
            <template #default="{ row }">
              <el-tag :type="labelOf(JOB_STATUS_TYPE, row.status, 'info')">
                {{ labelOf(JOB_STATUS_LABELS, row.status) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="retryCount" label="重试" width="70" />
          <el-table-column prop="triggerSource" label="触发来源" width="100" />
          <el-table-column prop="queuedAt" label="登记时间" width="170">
            <template #default="{ row }">{{ formatDateTime(row.queuedAt) }}</template>
          </el-table-column>
          <el-table-column prop="durationMs" label="耗时(ms)" width="110">
            <template #default="{ row }">{{ row.durationMs ?? '—' }}</template>
          </el-table-column>
          <el-table-column label="操作" width="160" fixed="right">
            <template #default="{ row }">
              <el-button size="small" link @click="openLogs(row.executionId)">日志</el-button>
              <el-button
                v-if="row.status === 'Pending' || row.status === 'Queued' || row.status === 'Running'"
                size="small"
                type="danger"
                link
                @click="handleCancel(row.executionId)"
              >
                取消
              </el-button>
            </template>
          </el-table-column>
        </el-table>
        <el-pagination
          class="job-pager"
          v-model:current-page="historyPage"
          v-model:page-size="historyPageSize"
          :total="historyTotal"
          :page-sizes="[10, 20, 50]"
          layout="total, sizes, prev, pager, next"
          @current-change="loadHistory"
          @size-change="loadHistory"
        />
      </el-tab-pane>
    </el-tabs>
  </QlxPagePanel>

  <!-- 触发任务弹窗 -->
  <el-dialog v-model="triggerVisible" title="触发任务" width="520px">
    <el-form label-width="100px">
      <el-form-item label="任务编码">
        <el-select v-model="triggerForm.taskCode" filterable placeholder="选择任务" style="width: 100%">
          <el-option v-for="def in definitions" :key="def.taskCode" :label="`${def.name}（${def.taskCode}）`" :value="def.taskCode" />
        </el-select>
      </el-form-item>
      <el-form-item label="执行模式">
        <el-radio-group v-model="triggerForm.mode">
          <el-radio value="sync">同步（阻塞等待）</el-radio>
          <el-radio value="async">异步（入队 + SSE）</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="参数(JSON)">
        <el-input v-model="triggerForm.parameters" type="textarea" :rows="4" placeholder='{"key": "value"}' />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="triggerVisible = false">取消</el-button>
      <el-button type="primary" @click="submitTrigger">触发</el-button>
    </template>
  </el-dialog>

  <!-- 执行日志抽屉 -->
  <el-drawer v-model="logVisible" title="执行日志" size="700px" direction="rtl">
    <div v-loading="logLoading" class="job-logs">
      <div v-for="(log, idx) in logs" :key="idx" class="job-log__line">
        <span class="job-log__time">{{ formatDateTime(log.logAt) }}</span>
        <span class="job-log__level" :style="{ color: LOG_LEVEL_COLOR[log.level] ?? 'inherit' }">[{{ log.level }}]</span>
        <span class="job-log__msg">{{ log.message }}</span>
        <span v-if="log.detail" class="job-log__detail">{{ log.detail }}</span>
      </div>
      <p v-if="logs.length === 0 && !logLoading" class="job-log__empty">暂无日志</p>
    </div>
  </el-drawer>
</template>

<style scoped>
.mono {
  font-family: var(--qlx-font-mono);
  font-size: var(--qlx-text-xs);
}
.job-toolbar {
  display: flex;
  gap: var(--qlx-space-2);
  margin-bottom: var(--qlx-space-3);
}
.job-pager {
  margin-top: var(--qlx-space-4);
  justify-content: flex-end;
}
.job-logs {
  font-family: var(--qlx-font-mono);
  font-size: var(--qlx-text-xs);
  line-height: 1.8;
}
.job-log__line {
  display: flex;
  gap: var(--qlx-space-2);
  padding: 2px 0;
  border-bottom: 1px dashed var(--qlx-line);
}
.job-log__time {
  color: var(--qlx-ink-400);
  flex-shrink: 0;
}
.job-log__level {
  font-weight: 600;
  flex-shrink: 0;
}
.job-log__msg {
  color: var(--qlx-ink-700);
  flex: 1;
  word-break: break-all;
}
.job-log__detail {
  color: var(--qlx-ink-500);
  font-size: 11px;
}
.job-log__empty {
  color: var(--qlx-ink-400);
  text-align: center;
  padding: var(--qlx-space-6);
}
</style>
