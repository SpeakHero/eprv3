<!--
  所属公司：四川淇利馨集团有限公司
  描述：审批操作区——通过 / 驳回 / 加签 / 转办 / 撤回 / 催办 + 意见输入
  请求形态：对齐 Qlx.Abstractions.Approvals（Approve/Reject/Withdraw/AddSigner/Transfer/UrgeRequest）
-->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import { mockApprovalService } from './approval-mock'
import type {
  ApprovalInstanceInfo,
  ApprovalServiceApi,
  ApprovalTaskInfo,
  ApprovalUserOption,
} from './types'

const props = defineProps<{
  /** 实例详情（父级持有；动作后经 updated 事件回传最新实例）。 */
  instance: ApprovalInstanceInfo | null
  /** 审批服务（默认 mock；联调注入 realApprovalService）。 */
  service?: ApprovalServiceApi
  /** 当前操作人（雪花 Id 字符串）。 */
  currentUserId: string
}>()

const emit = defineEmits<{ updated: [instance: ApprovalInstanceInfo] }>()

const api = (): ApprovalServiceApi => props.service ?? mockApprovalService

type ActionType = 'approve' | 'reject' | 'addSigner' | 'transfer' | 'withdraw' | 'urge'

const ACTION_META: Record<ActionType, { label: string; requireComment: boolean; danger?: boolean; pickUser?: 'addSigner' | 'transfer' }> = {
  approve: { label: '通过', requireComment: false },
  reject: { label: '驳回', requireComment: true, danger: true },
  addSigner: { label: '加签', requireComment: false, pickUser: 'addSigner' },
  transfer: { label: '转办', requireComment: false, pickUser: 'transfer' },
  withdraw: { label: '撤回', requireComment: false, danger: true },
  urge: { label: '催办', requireComment: false },
}

const action = ref<ActionType>('approve')
const dialogVisible = ref(false)
const comment = ref('')
const targetUserId = ref('')
const isBeforeCurrent = ref(true)
const submitting = ref(false)
const users = ref<ApprovalUserOption[]>([])

onMounted(async () => {
  users.value = await api().listUsers()
})

/** 当前用户的待办任务。 */
const myPendingTask = computed<ApprovalTaskInfo | null>(() => {
  if (!props.instance || props.instance.status !== 'Pending') {
    return null
  }
  return (
    props.instance.tasks.find(
      (task) => task.status === 'Pending' && task.assigneeUserId === props.currentUserId,
    ) ?? null
  )
})

const canWithdraw = computed(
  () => props.instance?.status === 'Pending' && props.instance.initiatorUserId === props.currentUserId,
)

function openDialog(type: ActionType): void {
  action.value = type
  comment.value = ''
  targetUserId.value = ''
  isBeforeCurrent.value = true
  dialogVisible.value = true
}

async function submit(): Promise<void> {
  if (!props.instance) {
    return
  }
  const meta = ACTION_META[action.value]
  if (meta.requireComment && !comment.value.trim()) {
    ElMessage.warning(`${meta.label}必须填写意见`)
    return
  }
  if (meta.pickUser && !targetUserId.value) {
    ElMessage.warning('请选择目标人员')
    return
  }
  submitting.value = true
  try {
    const instanceId = props.instance.instanceId
    let updated: ApprovalInstanceInfo
    switch (action.value) {
      case 'approve':
        updated = await api().approve({
          instanceId,
          approverUserId: props.currentUserId,
          comment: comment.value.trim() || null,
        })
        break
      case 'reject':
        updated = await api().reject({
          instanceId,
          approverUserId: props.currentUserId,
          reason: comment.value.trim(),
        })
        break
      case 'addSigner':
        updated = await api().addSigner({
          instanceId,
          taskId: myPendingTask.value!.taskId,
          approverUserId: props.currentUserId,
          addSignerUserId: targetUserId.value,
          reason: comment.value.trim() || null,
          isBeforeCurrent: isBeforeCurrent.value,
        })
        break
      case 'transfer':
        updated = await api().transfer({
          instanceId,
          taskId: myPendingTask.value!.taskId,
          fromUserId: props.currentUserId,
          toUserId: targetUserId.value,
          reason: comment.value.trim() || null,
        })
        break
      case 'withdraw':
        updated = await api().withdraw({
          instanceId,
          initiatorUserId: props.currentUserId,
          reason: comment.value.trim() || null,
        })
        break
      default:
        await api().urge({
          instanceId,
          requestorUserId: props.currentUserId,
          message: comment.value.trim() || null,
        })
        ElMessage.success('催办已发送')
        dialogVisible.value = false
        return
    }
    emit('updated', updated)
    ElMessage.success(`${meta.label}操作成功`)
    dialogVisible.value = false
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="approval-actions">
    <!-- 审批人待办 -->
    <template v-if="myPendingTask">
      <el-button type="primary" :disabled="submitting" @click="openDialog('approve')">
        <AppIcon name="Check" :size="14" /> 通过
      </el-button>
      <el-button type="danger" plain :disabled="submitting" @click="openDialog('reject')">
        <AppIcon name="Close" :size="14" /> 驳回
      </el-button>
      <el-button :disabled="submitting" @click="openDialog('addSigner')">
        <AppIcon name="User" :size="14" /> 加签
      </el-button>
      <el-button :disabled="submitting" @click="openDialog('transfer')">
        <AppIcon name="Position" :size="14" /> 转办
      </el-button>
    </template>

    <!-- 发起人撤回 / 催办 -->
    <el-button v-if="canWithdraw" type="warning" plain :disabled="submitting" @click="openDialog('withdraw')">
      <AppIcon name="RefreshLeft" :size="14" /> 撤回
    </el-button>
    <el-button v-if="instance && instance.status === 'Pending'" text :disabled="submitting" @click="openDialog('urge')">
      <AppIcon name="Bell" :size="14" /> 催办
    </el-button>

    <p v-if="instance && instance.status !== 'Pending'" class="approval-actions__closed">
      实例已结束（{{ instance.status }}），仅可查看时间线。
    </p>

    <!-- 操作对话框 -->
    <el-dialog
      v-model="dialogVisible"
      :title="`${ACTION_META[action].label}审批`"
      width="480px"
      destroy-on-close
    >
      <el-form label-position="top" @submit.prevent="submit">
        <el-form-item
          :label="action === 'reject' ? '驳回原因（必填）' : '审批意见'"
          :required="ACTION_META[action].requireComment"
        >
          <el-input
            v-model="comment"
            type="textarea"
            :rows="4"
            :placeholder="action === 'reject' ? '请填写驳回原因' : '可填写审批意见'"
            maxlength="500"
            show-word-limit
          />
        </el-form-item>

        <el-form-item v-if="ACTION_META[action].pickUser" :label="action === 'addSigner' ? '加签人' : '被转办人'" required>
          <el-select v-model="targetUserId" filterable placeholder="选择人员">
            <el-option
              v-for="user in users.filter((item) => item.userId !== currentUserId)"
              :key="user.userId"
              :label="`${user.userName}（${user.departmentName ?? ''}）`"
              :value="user.userId"
            />
          </el-select>
        </el-form-item>

        <el-form-item v-if="action === 'addSigner'">
          <el-checkbox v-model="isBeforeCurrent">加签人须先审批，原审批人后审（前加签）</el-checkbox>
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="submit">确定{{ ACTION_META[action].label }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.approval-actions {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  flex-wrap: wrap;
}

.approval-actions__closed {
  margin: 0;
  font-size: var(--qlx-text-xs);
  color: var(--qlx-ink-400);
}
</style>
