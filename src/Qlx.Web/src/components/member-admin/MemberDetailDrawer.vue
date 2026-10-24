<!--
  所属公司：四川淇利馨集团有限公司
  描述：成员详情抽屉——QlxDescriptionList + 登录日志分页表（el-drawer）
  说明：受控展示组件；登录日志数据经 fetchLogs fetcher 注入（页码 → Promise<分页结果>），
        组件不自持 API，保持可测；打开 / 切换成员时自动加载第 1 页
-->
<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import QlxDescriptionList from '@/components/crud/QlxDescriptionList.vue'
import type { DescriptionItem } from '@/components/crud/QlxDescriptionList.vue'
import type { PagedResult } from '@/types/admin'
import type { MemberItem, MemberLoginLogItem } from '@/types/org-admin'
import { memberRoleLabels } from '@/types/admin'
import { entityStatusLabels, loginResultLabels } from '@/types/tenant'
import { formatDateTime } from '@/utils/format'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    member: MemberItem | null
    loading?: boolean
    /** 登录日志分页 fetcher（页面接线适配层 fetchLoginLogs）。 */
    fetchLogs?: (page: number) => Promise<PagedResult<MemberLoginLogItem>>
    /** 组织 Id → 显示名。 */
    resolveOrgName?: (orgId: string | null) => string
    /** 角色码 → 显示名。 */
    resolveRoleName?: (code: string) => string
  }>(),
  {
    loading: false,
    fetchLogs: undefined,
    resolveOrgName: (orgId: string | null) => orgId ?? '—',
    resolveRoleName: (code: string) => code,
  },
)

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
}>()

const visible = computed({
  get: () => props.modelValue,
  set: (open) => emit('update:modelValue', open),
})

const items = computed<DescriptionItem[]>(() => {
  const member = props.member
  if (!member) {
    return []
  }
  return [
    { label: '成员名（本企业）', value: member.displayName },
    { label: '登录账号', value: member.loginAccount },
    { label: '所属组织', value: props.resolveOrgName(member.orgId) },
    { label: '成员角色', value: memberRoleLabels[member.memberRole] },
    { label: '状态', value: entityStatusLabels[member.memberStatus] },
    { label: '手机号', value: member.phone || '—' },
    { label: '邮箱', value: member.email || '—' },
    { label: '加入时间', value: formatDateTime(member.joinAt) },
    { label: '最近登录', value: formatDateTime(member.lastLoginAt) },
    {
      label: '角色',
      value: member.roleCodes.map((code) => props.resolveRoleName(code)).join('、') || '—',
      full: true,
    },
  ]
})

/** 模板取值辅助（el-table 行插槽为宽松类型）。 */
function loginResultLabel(result: string): string {
  return result === 'Failure' ? loginResultLabels.Failure : loginResultLabels.Success
}

/** ============ 登录日志分页 ============ */

const logs = ref<MemberLoginLogItem[]>([])
const logsTotal = ref(0)
const logsPage = ref(1)
const logsPageSize = 20
const logsLoading = ref(false)

async function loadLogs(page: number): Promise<void> {
  if (!props.fetchLogs || !props.member) {
    logs.value = []
    logsTotal.value = 0
    return
  }
  logsLoading.value = true
  try {
    const result = await props.fetchLogs(page)
    logs.value = result.items
    logsTotal.value = result.totalCount
    logsPage.value = result.page
  } catch (error) {
    logs.value = []
    logsTotal.value = 0
    ElMessage.error(error instanceof Error ? error.message : '加载登录日志失败')
  } finally {
    logsLoading.value = false
  }
}

watch(
  () => [props.modelValue, props.member?.id] as const,
  ([open, id]) => {
    if (open && id) {
      void loadLogs(1)
    }
    if (!open) {
      logs.value = []
      logsTotal.value = 0
      logsPage.value = 1
    }
  },
)
</script>

<template>
  <el-drawer v-model="visible" title="成员详情" size="640px" append-to-body class="member-detail-drawer">
    <div v-loading="loading" class="member-detail">
      <template v-if="member">
        <QlxDescriptionList :items="items" :column="2" />

        <section class="member-detail__logs">
          <header>
            <h3>登录记录</h3>
            <span class="qlx-numeric">共 {{ logsTotal }} 条</span>
          </header>
          <el-table v-loading="logsLoading" :data="logs" size="small" border>
            <el-table-column label="时间" width="170">
              <template #default="{ row }">{{ formatDateTime(row.at) }}</template>
            </el-table-column>
            <el-table-column label="IP 地址" width="140" prop="ipAddress">
              <template #default="{ row }">{{ row.ipAddress || '—' }}</template>
            </el-table-column>
            <el-table-column label="结果" width="90" align="center">
              <template #default="{ row }">
                <el-tag
                  :type="row.result === 'Success' ? 'success' : 'danger'"
                  size="small"
                  effect="light"
                >
                  {{ loginResultLabel(row.result) }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="失败原因" min-width="140">
              <template #default="{ row }">{{ row.failureReason ?? '—' }}</template>
            </el-table-column>
            <template #empty>
              <div class="member-detail__empty">暂无登录记录</div>
            </template>
          </el-table>
          <div v-if="logsTotal > logsPageSize" class="member-detail__pager">
            <el-pagination
              :total="logsTotal"
              :current-page="logsPage"
              :page-size="logsPageSize"
              layout="prev, pager, next"
              background
              @current-change="(page: number) => loadLogs(page)"
            />
          </div>
          <p class="member-detail__note">
            登录记录统一写入租户日志库（log_login），此处按成员登录账号维度分页查询。
          </p>
        </section>
      </template>
    </div>
  </el-drawer>
</template>

<style scoped>
.member-detail {
  min-height: 200px;
}

.member-detail__logs {
  margin-top: var(--qlx-space-6);
}

.member-detail__logs header {
  display: flex;
  margin-bottom: var(--qlx-space-3);
  align-items: baseline;
  justify-content: space-between;
}

.member-detail__logs h3 {
  padding-left: var(--qlx-space-3);
  border-left: 3px solid var(--qlx-gold-500);
  color: var(--qlx-brand-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-md);
  font-weight: 600;
}

.member-detail__logs header span {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.member-detail__pager {
  display: flex;
  margin-top: var(--qlx-space-3);
  justify-content: flex-end;
}

.member-detail__empty {
  padding: var(--qlx-space-6) 0;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.member-detail__note {
  margin-top: var(--qlx-space-2);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}
</style>

<style>
/* 平板竖屏 / 手机（<768px）：抽屉收敛为全宽（方案 §9 断点规范）。
   el-drawer 经 teleport 挂载到 body，scoped 不可达，故用专属类全局样式 */
@media (max-width: 767px) {
  .member-detail-drawer .el-drawer {
    width: 100% !important;
  }
}
</style>
