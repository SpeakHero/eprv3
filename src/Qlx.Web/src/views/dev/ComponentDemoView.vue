<!--
  所属公司：四川淇利馨集团有限公司
  描述：阶段二公共组件演示页——condition-builder / cron-builder / SSE 客户端 / 文件上传预览 / 审批操作区
  说明：全部走 mock 层（可切换真实 base URL / 注入真实服务）；路由 /dev/components 免登录直达。
-->
<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import ConditionBuilder from '@/components/condition-builder/ConditionBuilder.vue'
import CronBuilder from '@/components/cron-builder/CronBuilder.vue'
import FilePreview from '@/components/file/FilePreview.vue'
import FileUploader from '@/components/file/FileUploader.vue'
import ApprovalActionBar from '@/components/approval/ApprovalActionBar.vue'
import ApprovalTimeline from '@/components/approval/ApprovalTimeline.vue'
import { createSseClient, getMockSseBus } from '@/core/sse'
import type { SseClient, SseEventPayload, SseEventType } from '@/core/sse'
import { mockApprovalService, MOCK_CURRENT_USER_ID, resetMockApprovalInstance } from '@/components/approval/approval-mock'
import type { ApprovalInstanceInfo, ApprovalUserOption } from '@/components/approval/types'
import type { FileReferenceInfo } from '@/components/file/types'
import type { ConditionAstNode } from '@/components/condition-builder/types'

// ============ 通用 ============
const activeTab = ref('condition')

// ============ 条件构建器输出 ============
const conditionOutput = ref<{ ast: ConditionAstNode | null; text: string; valid: boolean }>({
  ast: null,
  text: '',
  valid: false,
})

// ============ SSE 演示 ============
const sseState = ref('idle')
const sseDetail = ref('')
const eventLog = ref<SseEventPayload[]>([])
let sseClient: SseClient | null = null
const unsubscribers: Array<() => void> = []

const injectableTypes: SseEventType[] = [
  'TaskProgress',
  'TaskCompleted',
  'ApprovalPending',
  'MessageCreated',
  'SystemAlert',
]

onMounted(() => {
  sseClient = createSseClient({ transport: 'mock' })
  unsubscribers.push(
    sseClient.onStateChange((state, detail) => {
      sseState.value = state
      sseDetail.value = detail ?? ''
    }),
  )
  injectableTypes.forEach((type) => {
    unsubscribers.push(
      sseClient!.subscribe(type, (event) => {
        eventLog.value.unshift(event)
        if (eventLog.value.length > 20) {
          eventLog.value.pop()
        }
      }),
    )
  })
})

onUnmounted(() => {
  unsubscribers.forEach((unsubscribe) => unsubscribe())
  sseClient?.close()
})

function injectEvent(type: SseEventType): void {
  const bus = getMockSseBus()
  switch (type) {
    case 'TaskProgress':
      bus.emit('TaskProgress', { executionId: 'exec-005001', taskCode: 'report.generate', percent: 42, message: '生成中' })
      break
    case 'TaskCompleted':
      bus.emit('TaskCompleted', { executionId: 'exec-005001', taskCode: 'report.generate', result: { url: '/downloads/r1.pdf' } })
      break
    case 'ApprovalPending':
      bus.emit('ApprovalPending', { instanceId: 'apr-7352012345678901234', nodeName: '财务复核' })
      break
    case 'MessageCreated':
      bus.emit('MessageCreated', { messageId: 'msg-1001', subject: '系统维护通知' })
      break
    default:
      bus.emit('SystemAlert', { level: 'warning', text: '共享库连接池水位 85%' })
      break
  }
}

// ============ 文件演示 ============
const demoFiles: FileReferenceInfo[] = [
  {
    fileId: 'fil-900101',
    storageKey: 'tenant-shared/2026/10/demo.png',
    fileName: '发票扫描件.png',
    contentType: 'image/png',
    sizeBytes: 245760,
    hash: '3a7f1c2b9d8e7f6a5b4c3d2e1f0a9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2a',
    uploadedAt: '2026-10-21T14:20:00+08:00',
  },
  {
    fileId: 'fil-900102',
    storageKey: 'tenant-shared/2026/10/demo.pdf',
    fileName: '采购合同盖章版.pdf',
    contentType: 'application/pdf',
    sizeBytes: 1048576,
    hash: 'b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3',
    uploadedAt: '2026-10-21T15:02:00+08:00',
  },
  {
    fileId: 'fil-900103',
    storageKey: 'tenant-shared/2026/10/demo.ofd',
    fileName: '电子发票.ofd',
    contentType: 'application/ofd',
    sizeBytes: 88432,
    hash: 'c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d0',
    uploadedAt: '2026-10-21T16:40:00+08:00',
  },
]

// ============ 审批演示 ============
const approvalInstance = ref<ApprovalInstanceInfo | null>(null)
const approvalUsers = ref<ApprovalUserOption[]>([])

async function loadApproval(): Promise<void> {
  approvalInstance.value = resetMockApprovalInstance()
  approvalUsers.value = await mockApprovalService.listUsers()
}

onMounted(() => {
  void loadApproval()
})
</script>

<template>
  <div class="demo-page">
    <header class="demo-page__head">
      <h1 class="demo-page__title">阶段二公共组件演示</h1>
      <p class="demo-page__desc">
        条件构建器 / CRON 构建器 / SSE 客户端 / 文件上传预览 / 审批操作区——当前全部由 mock 层驱动，
        业务接入时注入真实服务（base URL 可切换）即可，组件契约不变。
      </p>
    </header>

    <el-tabs v-model="activeTab" class="demo-page__tabs">
      <!-- 1. 条件构建器 -->
      <el-tab-pane label="条件构建器" name="condition">
        <ConditionBuilder
          @change="(payload) => (conditionOutput = payload)"
        />
        <el-alert
          class="demo-page__note"
          type="info"
          :closable="false"
          :title="`回调输出：${conditionOutput.valid ? '合法（AST JSON 可被 IConditionExpressionEngine.Parse 消费）' : '尚未完成'} · ${conditionOutput.text}`"
        />
      </el-tab-pane>

      <!-- 2. CRON 构建器 -->
      <el-tab-pane label="CRON 构建器" name="cron">
        <CronBuilder />
        <el-alert
          class="demo-page__note"
          type="info"
          :closable="false"
          title="预览与后端 SixFieldCronSchedule 同语义：UTC 逐秒搜索、a/n 步进到上限、日/周同限取或、0 与 7 均为周日；预览展示本地时间。"
        />
      </el-tab-pane>

      <!-- 3. SSE 客户端 -->
      <el-tab-pane label="SSE 客户端" name="sse">
        <div class="demo-panel">
          <div class="demo-panel__head">
            <span>连接状态：</span>
            <el-tag :type="sseState === 'open' ? 'success' : sseState === 'closed' ? 'info' : 'warning'" size="small">
              {{ sseState }}
            </el-tag>
            <span v-if="sseDetail" class="demo-panel__detail">{{ sseDetail }}</span>
          </div>
          <p class="demo-panel__hint">
            真实模式（transport='fetch'）：SharedWorker 单连接（3 标签页后端仅 1 条连接）+ BroadcastChannel 多标签分发
            + fetch 流解析（携带 Authorization 与 Last-Event-ID 头）+ 指数重连 1s→2s→4s 封顶 30s + 45s 心跳超时强制重连。
            下方按钮经 Mock 事件总线注入事件，验证按 SseEventType 路由到 handler。
          </p>
          <div class="demo-panel__actions">
            <el-button v-for="type in injectableTypes" :key="type" size="small" @click="injectEvent(type)">
              注入 {{ type }}
            </el-button>
          </div>
          <div class="demo-panel__log">
            <div v-for="event in eventLog" :key="event.eventId" class="demo-panel__event">
              <el-tag size="small">{{ event.type }}</el-tag>
              <span class="demo-panel__event-id">#{{ event.eventId }}</span>
              <code class="demo-panel__event-data">{{ JSON.stringify(event.data) }}</code>
            </div>
            <p v-if="eventLog.length === 0" class="demo-panel__hint">暂无事件，点击上方按钮注入。</p>
          </div>
        </div>
      </el-tab-pane>

      <!-- 4. 文件上传与预览 -->
      <el-tab-pane label="文件上传与预览" name="file">
        <FileUploader biz-type="demo" />
        <div class="demo-panel">
          <div class="demo-panel__head"><span>预览示例（图片 / PDF 内嵌，OFD 占位跳转）</span></div>
          <div class="demo-panel__files">
            <div v-for="file in demoFiles" :key="file.fileId" class="demo-panel__file">
              <AppIcon name="Document" :size="14" />
              <span>{{ file.fileName }}</span>
              <FilePreview :file="file" preview-url="#/dev/mock-preview" />
            </div>
          </div>
        </div>
      </el-tab-pane>

      <!-- 5. 审批操作区 -->
      <el-tab-pane label="审批操作区" name="approval">
        <div v-if="approvalInstance" class="demo-panel">
          <div class="demo-panel__head">
            <span class="demo-panel__title">{{ approvalInstance.title }}</span>
            <el-tag size="small">{{ approvalInstance.status }}</el-tag>
            <el-button size="small" text @click="loadApproval">重置演示</el-button>
          </div>
          <div class="demo-panel__biz">
            单据号：{{ approvalInstance.bizNo }} · 定义：{{ approvalInstance.approvalDefinitionCode }} ·
            发起人：{{ approvalUsers.find((user) => user.userId === approvalInstance!.initiatorUserId)?.userName ?? approvalInstance.initiatorUserId }}
          </div>
          <div class="demo-panel__divider" />
          <div class="demo-panel__head">
            <span>审批操作（当前操作人：{{ approvalUsers.find((user) => user.userId === MOCK_CURRENT_USER_ID)?.userName }}）</span>
          </div>
          <ApprovalActionBar
            :instance="approvalInstance"
            :current-user-id="MOCK_CURRENT_USER_ID"
            @updated="(instance) => (approvalInstance = instance)"
          />
          <div class="demo-panel__divider" />
          <div class="demo-panel__head"><span>审批时间线</span></div>
          <ApprovalTimeline :instance="approvalInstance" :users="approvalUsers" />
        </div>
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<style scoped>
.demo-page {
  max-width: var(--qlx-content-max);
  margin: 0 auto;
  padding: var(--qlx-space-6) var(--qlx-space-5) var(--qlx-space-12);
}

.demo-page__head {
  margin-bottom: var(--qlx-space-4);
}

.demo-page__title {
  margin: 0 0 var(--qlx-space-2);
  font-size: var(--qlx-text-xl);
  color: var(--qlx-brand-900);
}

.demo-page__desc {
  margin: 0;
  font-size: var(--qlx-text-sm);
  color: var(--qlx-ink-500);
  line-height: 1.7;
}

.demo-page__tabs {
  background: var(--qlx-surface);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-lg);
  padding: var(--qlx-space-4) var(--qlx-space-5) var(--qlx-space-5);
}

.demo-page__note {
  margin-top: var(--qlx-space-3);
}

.demo-panel {
  display: grid;
  gap: var(--qlx-space-3);
}

.demo-panel__head {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  flex-wrap: wrap;
  font-size: var(--qlx-text-sm);
  font-weight: 600;
  color: var(--qlx-ink-700);
}

.demo-panel__title {
  color: var(--qlx-brand-800);
}

.demo-panel__detail {
  font-size: var(--qlx-text-xs);
  font-weight: 400;
  color: var(--qlx-ink-400);
}

.demo-panel__biz {
  font-size: var(--qlx-text-xs);
  color: var(--qlx-ink-500);
}

.demo-panel__divider {
  border-top: 1px solid var(--qlx-line);
}

.demo-panel__hint {
  margin: 0;
  font-size: var(--qlx-text-xs);
  color: var(--qlx-ink-400);
  line-height: 1.7;
}

.demo-panel__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--qlx-space-2);
}

.demo-panel__log {
  display: grid;
  gap: var(--qlx-space-1);
  max-height: 280px;
  overflow: auto;
  background: var(--qlx-canvas);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-sm);
  padding: var(--qlx-space-2);
}

.demo-panel__event {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  font-size: var(--qlx-text-xs);
}

.demo-panel__event-id {
  color: var(--qlx-ink-400);
  font-family: var(--qlx-font-numeric);
}

.demo-panel__event-data {
  font-family: var(--qlx-font-mono);
  color: var(--qlx-ink-700);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.demo-panel__files {
  display: grid;
  gap: var(--qlx-space-2);
}

.demo-panel__file {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  font-size: var(--qlx-text-sm);
  color: var(--qlx-ink-700);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-sm);
  padding: var(--qlx-space-2) var(--qlx-space-3);
}
</style>
