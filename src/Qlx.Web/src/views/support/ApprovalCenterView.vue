<!--
  所属公司：四川淇利馨集团有限公司
  描述：审批中心——待办 / 已办 / 我发起的 / 委托管理（规范 支撑功能 01-审批中心）
  说明：复用审批操作区（ApprovalActionBar）与时间线（ApprovalTimeline）公共组件；
        待办点击进入详情抽屉执行通过 / 驳回 / 加签 / 转办 / 催办；委托支持新建与撤销。
-->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import ApprovalActionBar from '@/components/approval/ApprovalActionBar.vue'
import ApprovalTimeline from '@/components/approval/ApprovalTimeline.vue'
import { convertInstance, realApprovalServiceAdapter } from '@/api/support-approval-adapter'
import {
  createApprovalDelegate,
  fetchApprovalDelegates,
  fetchApprovalDone,
  fetchApprovalFlows,
  fetchApprovalInitiated,
  fetchApprovalInstance,
  fetchApprovalTodos,
  revokeApprovalDelegate,
} from '@/api/support'
import type {
  ApprovalDelegateInfo,
  ApprovalInstanceInfo,
  ApprovalTaskInfo,
  ApprovalUserOption,
  CreateDelegateRequest,
  SupportFlowSummary,
} from '@/types/support'
import {
  APPROVAL_STATUS_LABELS,
  APPROVAL_STATUS_TYPE,
  APPROVAL_TASK_STATUS_LABELS,
  APPROVAL_TASK_STATUS_TYPE,
} from '@/types/support'
import { formatDateTime, labelOf } from '@/utils/format'
import { useAuthStore } from '@/stores/auth'
import type { ApprovalInstanceInfo as CompInstanceInfo } from '@/components/approval/types'

type ApprovalTab = 'todos' | 'done' | 'initiated' | 'delegates'

const activeTab = ref<ApprovalTab>('todos')

/** ============ 列表数据 ============ */
const todos = ref<ApprovalTaskInfo[]>([])
const done = ref<ApprovalTaskInfo[]>([])
const initiated = ref<ApprovalInstanceInfo[]>([])
const delegates = ref<ApprovalDelegateInfo[]>([])
const flows = ref<SupportFlowSummary[]>([])
const loading = ref(false)

/** 候选人（加签 / 转办）——当前用户所在租户成员列表。 */
const candidateUsers = ref<ApprovalUserOption[]>([])

async function loadTodos(): Promise<void> {
  loading.value = true
  try {
    todos.value = await fetchApprovalTodos()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载待办失败')
  } finally {
    loading.value = false
  }
}

async function loadDone(): Promise<void> {
  loading.value = true
  try {
    done.value = await fetchApprovalDone()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载已办失败')
  } finally {
    loading.value = false
  }
}

async function loadInitiated(): Promise<void> {
  loading.value = true
  try {
    initiated.value = await fetchApprovalInitiated()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载发起列表失败')
  } finally {
    loading.value = false
  }
}

async function loadDelegates(): Promise<void> {
  loading.value = true
  try {
    delegates.value = await fetchApprovalDelegates()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载委托失败')
  } finally {
    loading.value = false
  }
}

function switchTab(tab: ApprovalTab): void {
  activeTab.value = tab
  if (tab === 'todos') void loadTodos()
  if (tab === 'done') void loadDone()
  if (tab === 'initiated') void loadInitiated()
  if (tab === 'delegates') void loadDelegates()
}

/* ============ 详情抽屉 ============ */
const detailVisible = ref(false)
const detailLoading = ref(false)
const currentInstance = ref<ApprovalInstanceInfo | null>(null)
const currentTaskId = ref<string>('')

async function openInstance(instanceId: string, taskId = ''): Promise<void> {
  detailVisible.value = true
  detailLoading.value = true
  currentTaskId.value = taskId
  try {
    currentInstance.value = await fetchApprovalInstance(instanceId)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载审批详情失败')
  } finally {
    detailLoading.value = false
  }
}

/** 组件实例（字符串状态，供 ApprovalActionBar / ApprovalTimeline 使用）。 */
const compInstance = computed<CompInstanceInfo | null>(() =>
  currentInstance.value ? convertInstance(currentInstance.value) : null,
)

const auth = useAuthStore()

/** 当前操作人：取当前登录用户 Id（后端动作以此为准，不接受前端传入）。 */
const currentUserId = computed(() => auth.user?.userId ?? currentInstance.value?.initiatorUserId ?? '')

function onInstanceUpdated(_instance: CompInstanceInfo): void {
  // 组件动作成功后回传最新实例（字符串状态）；重新拉取后端实例以刷新视图
  if (currentInstance.value) {
    void openInstance(currentInstance.value.instanceId)
  }
  if (activeTab.value === 'todos') void loadTodos()
  if (activeTab.value === 'initiated') void loadInitiated()
}

/* ============ 委托管理 ============ */
const delegateDialogVisible = ref(false)
const delegateForm = ref<CreateDelegateRequest>({
  agentUserId: '',
  bizTypes: null,
  startAt: '',
  endAt: '',
  reason: null,
})

function openDelegateDialog(): void {
  delegateForm.value = { agentUserId: '', bizTypes: null, startAt: '', endAt: '', reason: null }
  delegateDialogVisible.value = true
}

async function submitDelegate(): Promise<void> {
  if (!delegateForm.value.agentUserId) {
    ElMessage.warning('请选择代理人')
    return
  }
  if (!delegateForm.value.startAt || !delegateForm.value.endAt) {
    ElMessage.warning('请选择委托有效期')
    return
  }
  try {
    await createApprovalDelegate(delegateForm.value)
    ElMessage.success('委托已创建')
    delegateDialogVisible.value = false
    void loadDelegates()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '创建委托失败')
  }
}

async function revokeDelegate(delegateId: string): Promise<void> {
  try {
    await ElMessageBox.confirm('确定撤销该委托？撤销后新审批任务将不再转代理人。', '撤销委托', {
      type: 'warning',
    })
    await revokeApprovalDelegate(delegateId)
    ElMessage.success('委托已撤销')
    void loadDelegates()
  } catch {
    /* 用户取消 */
  }
}

/* ============ 业务单号 → 跳转 ============ */
function bizHref(_instance: ApprovalInstanceInfo): string {
  // 业务单号跳转由各业务模块路由承接；此处仅展示，占位跳转工作台
  return '/workbench'
}

/* ============ 发起人撤回 ============ */
async function handleWithdraw(payload: { instanceId: string; reason: string }): Promise<void> {
  try {
    await ElMessageBox.confirm('确定撤回该审批？', '撤回审批', { type: 'warning' })
    await realApprovalServiceAdapter.withdraw({
      instanceId: payload.instanceId,
      initiatorUserId: currentUserId.value,
      reason: payload.reason,
    })
    ElMessage.success('已撤回')
    void loadInitiated()
  } catch (error) {
    if (error !== 'cancel') {
      ElMessage.error(error instanceof Error ? error.message : '撤回失败')
    }
  }
}

onMounted(() => {
  void loadTodos()
  void fetchApprovalFlows().then((list) => (flows.value = list)).catch(() => undefined)
})
</script>

<template>
  <QlxPagePanel
    title="审批中心"
    subtitle="待办 / 已办 / 我发起的 / 委托管理——审批动作统一经 /api/support/approvals 提交，操作人身份由服务端取当前登录人"
  >
    <el-tabs v-model="activeTab" @tab-change="(name: string) => switchTab(name as ApprovalTab)">
      <!-- 待办 -->
      <el-tab-pane label="待办" name="todos">
        <el-table :data="todos" v-loading="loading" stripe style="width: 100%">
          <el-table-column prop="nodeName" label="审批节点" min-width="140" />
          <el-table-column prop="instanceId" label="实例号" width="200">
            <template #default="{ row }">
              <span class="mono">{{ row.instanceId }}</span>
            </template>
          </el-table-column>
          <el-table-column label="状态" width="100">
            <template #default="{ row }">
              <el-tag :type="labelOf(APPROVAL_TASK_STATUS_TYPE, row.status, 'info')">
                {{ labelOf(APPROVAL_TASK_STATUS_LABELS, row.status) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="createdAt" label="到达时间" width="170">
            <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="120" fixed="right">
            <template #default="{ row }">
              <el-button size="small" type="primary" link @click="openInstance(row.instanceId, row.taskId)">
                处理
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <!-- 已办 -->
      <el-tab-pane label="已办" name="done">
        <el-table :data="done" v-loading="loading" stripe style="width: 100%">
          <el-table-column prop="nodeName" label="审批节点" min-width="140" />
          <el-table-column prop="instanceId" label="实例号" width="200">
            <template #default="{ row }">
              <span class="mono">{{ row.instanceId }}</span>
            </template>
          </el-table-column>
          <el-table-column label="处理结果" width="100">
            <template #default="{ row }">
              <el-tag :type="labelOf(APPROVAL_TASK_STATUS_TYPE, row.status, 'info')">
                {{ labelOf(APPROVAL_TASK_STATUS_LABELS, row.status) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="comment" label="意见" min-width="160" show-overflow-tooltip />
          <el-table-column prop="completedAt" label="处理时间" width="170">
            <template #default="{ row }">{{ formatDateTime(row.completedAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="120" fixed="right">
            <template #default="{ row }">
              <el-button size="small" link @click="openInstance(row.instanceId)">查看</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <!-- 我发起的 -->
      <el-tab-pane label="我发起的" name="initiated">
        <el-table :data="initiated" v-loading="loading" stripe style="width: 100%">
          <el-table-column prop="title" label="标题" min-width="180" show-overflow-tooltip />
          <el-table-column prop="bizNo" label="业务单号" width="180" />
          <el-table-column prop="approvalDefinitionCode" label="流程" width="160" />
          <el-table-column label="状态" width="100">
            <template #default="{ row }">
              <el-tag :type="labelOf(APPROVAL_STATUS_TYPE, row.status, 'info')">
                {{ labelOf(APPROVAL_STATUS_LABELS, row.status) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="startedAt" label="发起时间" width="170">
            <template #default="{ row }">{{ formatDateTime(row.startedAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="160" fixed="right">
            <template #default="{ row }">
              <el-button size="small" link @click="openInstance(row.instanceId)">详情</el-button>
              <el-button
                v-if="row.status === 1"
                size="small"
                link
                type="warning"
                @click="handleWithdraw({ instanceId: row.instanceId, reason: '发起人撤回' })"
              >
                撤回
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <!-- 委托管理 -->
      <el-tab-pane label="委托管理" name="delegates">
        <template #label>
          <div class="tab-with-action">
            <span>委托管理</span>
            <el-button size="small" type="primary" link @click="openDelegateDialog">
              <AppIcon name="Plus" :size="12" /> 新建委托
            </el-button>
          </div>
        </template>
        <el-table :data="delegates" v-loading="loading" stripe style="width: 100%">
          <el-table-column prop="delegateeUserId" label="代理人" width="200">
            <template #default="{ row }">
              <span class="mono">{{ row.delegateeUserId }}</span>
            </template>
          </el-table-column>
          <el-table-column label="有效期" min-width="320">
            <template #default="{ row }">
              {{ formatDateTime(row.startAt) }} 至 {{ formatDateTime(row.endAt) }}
            </template>
          </el-table-column>
          <el-table-column prop="reason" label="原因" min-width="160" show-overflow-tooltip />
          <el-table-column label="操作" width="120" fixed="right">
            <template #default="{ row }">
              <el-button size="small" type="danger" link @click="revokeDelegate(row.delegateId)">撤销</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>
    </el-tabs>
  </QlxPagePanel>

  <!-- 审批详情抽屉 -->
  <el-drawer v-model="detailVisible" title="审批详情" size="640px" direction="rtl">
    <div v-loading="detailLoading" class="approval-detail">
      <template v-if="currentInstance">
        <div class="approval-detail__head">
          <h3>{{ currentInstance.title }}</h3>
          <el-tag :type="APPROVAL_STATUS_TYPE[currentInstance.status]">
            {{ APPROVAL_STATUS_LABELS[currentInstance.status] }}
          </el-tag>
        </div>
        <el-descriptions :column="2" border size="small" class="approval-detail__meta">
          <el-descriptions-item label="业务单号">
            <el-link :href="bizHref(currentInstance)" type="primary">{{ currentInstance.bizNo }}</el-link>
          </el-descriptions-item>
          <el-descriptions-item label="流程定义">{{ currentInstance.approvalDefinitionCode }}</el-descriptions-item>
          <el-descriptions-item label="发起人">
            <span class="mono">{{ currentInstance.initiatorUserId }}</span>
          </el-descriptions-item>
          <el-descriptions-item label="发起时间">{{ formatDateTime(currentInstance.startedAt) }}</el-descriptions-item>
        </el-descriptions>

        <el-divider content-position="left">审批操作</el-divider>
        <ApprovalActionBar
          :instance="compInstance"
          :service="realApprovalServiceAdapter"
          :current-user-id="currentUserId"
          @updated="onInstanceUpdated"
        />

        <el-divider content-position="left">审批时间线</el-divider>
        <ApprovalTimeline :instance="compInstance" :users="[]" />
      </template>
    </div>
  </el-drawer>

  <!-- 新建委托弹窗 -->
  <el-dialog v-model="delegateDialogVisible" title="新建审批委托" width="520px">
    <el-form label-width="90px">
      <el-form-item label="代理人">
        <el-select v-model="delegateForm.agentUserId" filterable placeholder="选择代理人" style="width: 100%">
          <el-option
            v-for="user in candidateUsers"
            :key="user.userId"
            :label="`${user.userName}（${user.departmentName ?? ''}）`"
            :value="user.userId"
          />
        </el-select>
      </el-form-item>
      <el-form-item label="有效期">
        <el-date-picker
          v-model="delegateForm.startAt"
          type="datetime"
          placeholder="开始时间"
          style="width: 45%"
        />
        <span class="form-sep">至</span>
        <el-date-picker
          v-model="delegateForm.endAt"
          type="datetime"
          placeholder="结束时间"
          style="width: 45%"
        />
      </el-form-item>
      <el-form-item label="限制单据">
        <el-select
          v-model="delegateForm.bizTypes"
          multiple
          clearable
          placeholder="不选=全部单据类型"
          style="width: 100%"
        >
          <el-option
            v-for="flow in flows"
            :key="flow.defCode"
            :label="`${flow.defName}（${flow.bizType}）`"
            :value="flow.bizType"
          />
        </el-select>
      </el-form-item>
      <el-form-item label="委托原因">
        <el-input v-model="delegateForm.reason" type="textarea" :rows="2" placeholder="可空" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="delegateDialogVisible = false">取消</el-button>
      <el-button type="primary" @click="submitDelegate">确认委托</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.mono {
  font-family: var(--qlx-font-mono);
  font-size: var(--qlx-text-xs);
}
.tab-with-action {
  display: inline-flex;
  align-items: center;
  gap: var(--qlx-space-2);
}
.approval-detail {
  padding: 0 var(--qlx-space-2);
}
.approval-detail__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: var(--qlx-space-3);
}
.approval-detail__head h3 {
  margin: 0;
  font-size: var(--qlx-text-lg);
  color: var(--qlx-brand-900);
}
.approval-detail__meta {
  margin-bottom: var(--qlx-space-2);
}
.form-sep {
  display: inline-block;
  width: 4%;
  text-align: center;
  color: var(--qlx-ink-400);
}
</style>
