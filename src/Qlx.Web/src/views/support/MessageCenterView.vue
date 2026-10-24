<!--
  所属公司：四川淇利馨集团有限公司
  描述：信息中心——收件箱 / 已发送 / 订阅管理（规范 支撑功能 02-信息中心）
  说明：收件箱合并本地消息与全局公告；支持已读 / 全部已读 / 删除 / 全局项确认；
        已发送支持查看渠道投递日志与人工重试；订阅管理支持开关切换（强制订阅不可关闭）。
-->
<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import {
  deleteInboxItem,
  fetchMessageInbox,
  fetchMessageSendLogs,
  fetchMessageSent,
  fetchSubscriptions,
  fetchUnreadCount,
  markAllMessagesRead,
  markGlobalItemState,
  markMessageRead,
  retryMessageDelivery,
  toggleSubscription,
} from '@/api/support'
import type {
  MessageInfo,
  SupportInboxItem,
  SupportPage,
  SupportSendLogItem,
  SupportSubscriptionItem,
} from '@/types/support'
import {
  MESSAGE_CHANNEL_LABELS,
  MESSAGE_PRIORITY_LABELS,
  MESSAGE_PRIORITY_TYPE,
  MESSAGE_SEND_LOG_STATUS_LABELS,
  MESSAGE_STATUS_LABELS,
} from '@/types/support'
import { formatDateTime, labelOf } from '@/utils/format'

type MessageTab = 'inbox' | 'sent' | 'subscriptions'

const activeTab = ref<MessageTab>('inbox')

/* ============ 收件箱 ============ */
const inbox = ref<SupportInboxItem[]>([])
const inboxTotal = ref(0)
const inboxPage = ref(1)
const inboxPageSize = ref(20)
const inboxKeyword = ref('')
const inboxUnreadOnly = ref(false)
const inboxLoading = ref(false)

async function loadInbox(): Promise<void> {
  inboxLoading.value = true
  try {
    const page: SupportPage<SupportInboxItem> = await fetchMessageInbox({
      pageIndex: inboxPage.value,
      pageSize: inboxPageSize.value,
      keyword: inboxKeyword.value || null,
      unreadOnly: inboxUnreadOnly.value,
    })
    inbox.value = page.items
    inboxTotal.value = page.total
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载收件箱失败')
  } finally {
    inboxLoading.value = false
  }
}

const unreadCount = ref(0)
async function refreshUnread(): Promise<void> {
  try {
    unreadCount.value = await fetchUnreadCount()
  } catch {
    /* 忽略未读数失败 */
  }
}

async function handleMarkRead(item: SupportInboxItem): Promise<void> {
  if (item.isGlobal) {
    await markGlobalItemState(item.messageId, 'READ')
  } else if (item.inboxId) {
    await markMessageRead(item.inboxId)
  }
  ElMessage.success('已标记已读')
  void loadInbox()
  void refreshUnread()
}

async function handleMarkAllRead(): Promise<void> {
  try {
    await markAllMessagesRead()
    ElMessage.success('全部已读')
    void loadInbox()
    void refreshUnread()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

async function handleDelete(item: SupportInboxItem): Promise<void> {
  try {
    await ElMessageBox.confirm(`确定删除消息「${item.subject}」？`, '删除消息', { type: 'warning' })
    if (item.isGlobal) {
      await markGlobalItemState(item.messageId, 'DELETED')
    } else if (item.inboxId) {
      await deleteInboxItem(item.inboxId)
    }
    ElMessage.success('已删除')
    void loadInbox()
  } catch {
    /* 用户取消 */
  }
}

/* ============ 已发送 ============ */
const sent = ref<MessageInfo[]>([])
const sentTotal = ref(0)
const sentPage = ref(1)
const sentPageSize = ref(20)
const sentKeyword = ref('')
const sentLoading = ref(false)

async function loadSent(): Promise<void> {
  sentLoading.value = true
  try {
    const page: SupportPage<MessageInfo> = await fetchMessageSent({
      pageIndex: sentPage.value,
      pageSize: sentPageSize.value,
      keyword: sentKeyword.value || null,
    })
    sent.value = page.items
    sentTotal.value = page.total
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载已发送失败')
  } finally {
    sentLoading.value = false
  }
}

/* 发送日志抽屉 */
const logVisible = ref(false)
const logLoading = ref(false)
const sendLogs = ref<SupportSendLogItem[]>([])
const logMessageId = ref('')

async function openLogs(messageId: string): Promise<void> {
  logMessageId.value = messageId
  logVisible.value = true
  logLoading.value = true
  try {
    sendLogs.value = await fetchMessageSendLogs(messageId)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载发送日志失败')
  } finally {
    logLoading.value = false
  }
}

async function handleRetry(messageId: string): Promise<void> {
  try {
    await retryMessageDelivery(messageId)
    ElMessage.success('已触发重试')
    if (logVisible.value) void openLogs(messageId)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '重试失败')
  }
}

/* ============ 订阅管理 ============ */
const subscriptions = ref<SupportSubscriptionItem[]>([])
const subLoading = ref(false)

async function loadSubscriptions(): Promise<void> {
  subLoading.value = true
  try {
    subscriptions.value = await fetchSubscriptions()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载订阅失败')
  } finally {
    subLoading.value = false
  }
}

async function handleToggleSub(item: SupportSubscriptionItem): Promise<void> {
  if (item.locked && item.enabled) {
    ElMessage.warning('该类别为强制订阅，不可关闭')
    return
  }
  try {
    await toggleSubscription(item.msgType, !item.enabled)
    item.enabled = !item.enabled
    ElMessage.success('订阅已更新')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '更新失败')
  }
}

function switchTab(tab: MessageTab): void {
  activeTab.value = tab
  if (tab === 'inbox') void loadInbox()
  if (tab === 'sent') void loadSent()
  if (tab === 'subscriptions') void loadSubscriptions()
}

onMounted(() => {
  void loadInbox()
  void refreshUnread()
})
</script>

<template>
  <QlxPagePanel
    title="信息中心"
    subtitle="收件箱合并本地消息与全局公告；已发送支持渠道投递日志与重试；订阅管理控制消息推送渠道"
  >
    <el-tabs v-model="activeTab" @tab-change="(name: string) => switchTab(name as MessageTab)">
      <!-- 收件箱 -->
      <el-tab-pane name="inbox">
        <template #label>
          <span>收件箱 <el-tag v-if="unreadCount > 0" type="danger" size="small" round>{{ unreadCount }}</el-tag></span>
        </template>
        <div class="msg-toolbar">
          <el-input
            v-model="inboxKeyword"
            placeholder="搜索标题 / 正文"
            clearable
            style="width: 260px"
            @keyup.enter="loadInbox"
          />
          <el-checkbox v-model="inboxUnreadOnly" @change="loadInbox">仅未读</el-checkbox>
          <el-button type="primary" plain @click="loadInbox">搜索</el-button>
          <el-button @click="handleMarkAllRead">全部已读</el-button>
        </div>
        <el-table :data="inbox" v-loading="inboxLoading" stripe style="width: 100%">
          <el-table-column width="60">
            <template #default="{ row }">
              <el-icon v-if="!row.readAt" color="var(--qlx-brand-700)"><CircleFilled /></el-icon>
              <el-icon v-else color="var(--qlx-ink-300)"><Circle /></el-icon>
            </template>
          </el-table-column>
          <el-table-column prop="subject" label="标题" min-width="220" show-overflow-tooltip />
          <el-table-column prop="msgType" label="类别" width="120" />
          <el-table-column label="优先级" width="90">
            <template #default="{ row }">
              <el-tag :type="labelOf(MESSAGE_PRIORITY_TYPE, row.priority, 'info')" size="small">
                {{ labelOf(MESSAGE_PRIORITY_LABELS, row.priority) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="状态" width="100">
            <template #default="{ row }">{{ labelOf(MESSAGE_STATUS_LABELS, row.status) }}</template>
          </el-table-column>
          <el-table-column label="全局" width="70">
            <template #default="{ row }">
              <el-tag v-if="row.isGlobal" type="warning" size="small">全局</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="createdAt" label="时间" width="170">
            <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="180" fixed="right">
            <template #default="{ row }">
              <el-button size="small" link @click="handleMarkRead(row)">标记已读</el-button>
              <el-button
                v-if="!row.isGlobal || !row.forceAccept"
                size="small"
                type="danger"
                link
                @click="handleDelete(row)"
              >
                删除
              </el-button>
            </template>
          </el-table-column>
        </el-table>
        <el-pagination
          class="msg-pager"
          v-model:current-page="inboxPage"
          v-model:page-size="inboxPageSize"
          :total="inboxTotal"
          :page-sizes="[10, 20, 50]"
          layout="total, sizes, prev, pager, next"
          @current-change="loadInbox"
          @size-change="loadInbox"
        />
      </el-tab-pane>

      <!-- 已发送 -->
      <el-tab-pane label="已发送" name="sent">
        <div class="msg-toolbar">
          <el-input
            v-model="sentKeyword"
            placeholder="搜索标题"
            clearable
            style="width: 260px"
            @keyup.enter="loadSent"
          />
          <el-button type="primary" plain @click="loadSent">搜索</el-button>
        </div>
        <el-table :data="sent" v-loading="sentLoading" stripe style="width: 100%">
          <el-table-column prop="subject" label="标题" min-width="220" show-overflow-tooltip />
          <el-table-column label="渠道" width="100">
            <template #default="{ row }">{{ labelOf(MESSAGE_CHANNEL_LABELS, row.channel) }}</template>
          </el-table-column>
          <el-table-column label="优先级" width="90">
            <template #default="{ row }">
              <el-tag :type="labelOf(MESSAGE_PRIORITY_TYPE, row.priority, 'info')" size="small">
                {{ labelOf(MESSAGE_PRIORITY_LABELS, row.priority) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="状态" width="100">
            <template #default="{ row }">{{ labelOf(MESSAGE_STATUS_LABELS, row.status) }}</template>
          </el-table-column>
          <el-table-column prop="createdAt" label="发送时间" width="170">
            <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="180" fixed="right">
            <template #default="{ row }">
              <el-button size="small" link @click="openLogs(row.messageId)">投递日志</el-button>
              <el-button
                v-if="row.status === 6"
                size="small"
                type="warning"
                link
                @click="handleRetry(row.messageId)"
              >
                重试
              </el-button>
            </template>
          </el-table-column>
        </el-table>
        <el-pagination
          class="msg-pager"
          v-model:current-page="sentPage"
          v-model:page-size="sentPageSize"
          :total="sentTotal"
          :page-sizes="[10, 20, 50]"
          layout="total, sizes, prev, pager, next"
          @current-change="loadSent"
          @size-change="loadSent"
        />
      </el-tab-pane>

      <!-- 订阅管理 -->
      <el-tab-pane label="订阅管理" name="subscriptions">
        <el-table :data="subscriptions" v-loading="subLoading" stripe style="width: 100%">
          <el-table-column prop="msgType" label="消息类别" min-width="160" />
          <el-table-column label="推送渠道" min-width="200">
            <template #default="{ row }">
              <el-tag
                v-for="bit in [0, 1, 2, 3]"
                v-show="(row.channelsMask & (1 << bit)) !== 0"
                :key="bit"
                size="small"
                style="margin-right: 4px"
              >
                {{ MESSAGE_CHANNEL_LABELS[bit as 0 | 1 | 2 | 3] }}
              </el-tag>
              <span v-if="row.channelsMask === 0" style="color: var(--qlx-ink-400)">无</span>
            </template>
          </el-table-column>
          <el-table-column label="状态" width="120">
            <template #default="{ row }">
              <el-switch
                :model-value="row.enabled"
                :disabled="row.locked && row.enabled"
                @change="handleToggleSub(row)"
              />
              <el-tag v-if="row.locked" type="danger" size="small" style="margin-left: 8px">强制</el-tag>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>
    </el-tabs>
  </QlxPagePanel>

  <!-- 发送日志抽屉 -->
  <el-drawer v-model="logVisible" title="渠道投递日志" size="600px" direction="rtl">
    <el-table :data="sendLogs" v-loading="logLoading" stripe>
      <el-table-column label="渠道" width="100">
        <template #default="{ row }">{{ labelOf(MESSAGE_CHANNEL_LABELS, row.channel) }}</template>
      </el-table-column>
      <el-table-column prop="recipient" label="收件目标" min-width="160" />
      <el-table-column label="状态" width="100">
        <template #default="{ row }">{{ labelOf(MESSAGE_SEND_LOG_STATUS_LABELS, row.status) }}</template>
      </el-table-column>
      <el-table-column prop="retryCount" label="重试序号" width="90" />
      <el-table-column prop="failReason" label="失败原因" min-width="160" show-overflow-tooltip />
      <el-table-column prop="sentAt" label="完成时间" width="170">
        <template #default="{ row }">{{ formatDateTime(row.sentAt) }}</template>
      </el-table-column>
    </el-table>
    <div class="log-footer">
      <el-button type="warning" @click="handleRetry(logMessageId)">人工重试</el-button>
    </div>
  </el-drawer>
</template>

<style scoped>
.msg-toolbar {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  margin-bottom: var(--qlx-space-3);
}
.msg-pager {
  margin-top: var(--qlx-space-4);
  justify-content: flex-end;
}
.log-footer {
  margin-top: var(--qlx-space-4);
  text-align: right;
}
</style>
