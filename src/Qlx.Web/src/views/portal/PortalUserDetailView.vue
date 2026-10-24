<!--
  所属公司：四川淇利馨集团有限公司
  描述：门户管理 · 用户详情——自然人全局信息 + 关联租户成员关系（一人在多租户）+ 登录日志
-->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import QlxDescriptionList from '@/components/crud/QlxDescriptionList.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import { fetchUserDetail } from '@/api/admin'
import { accountStatusLabels, memberRoleLabels } from '@/types/admin'
import type { LoginLog, PortalUser, TenantMembership } from '@/types/admin'
import { formatDateTime, yesNo } from '@/utils/format'

const route = useRoute()
const userId = route.params.id as string

const user = ref<PortalUser | null>(null)
const memberships = ref<TenantMembership[]>([])
const logs = ref<LoginLog[]>([])
const loading = ref(true)

const descriptionItems = computed(() => {
  if (!user.value) {
    return []
  }
  return [
    { label: '登录账号', value: user.value.loginAccount },
    { label: '显示名', value: `${user.value.displayName}（非唯一，仅展示）` },
    { label: '手机号', value: user.value.phone },
    { label: '邮箱', value: user.value.email },
    { label: '实名认证', value: yesNo(user.value.realNameVerified) },
    { label: '门户管理员', value: yesNo(user.value.isPortalAdmin) },
    { label: '全局状态', value: accountStatusLabels[user.value.status] },
    { label: '最近登录', value: formatDateTime(user.value.lastLoginAt) },
    { label: '创建时间', value: formatDateTime(user.value.createdAt), full: true },
  ]
})

const membershipColumns = [
  { prop: 'tenantName', label: '租户', minWidth: 220 },
  { prop: 'tenantCode', label: '租户编码', type: 'code' as const, width: 130 },
  { prop: 'displayNameInTenant', label: '租户内展示名', width: 140 },
  { prop: 'memberRole', label: '角色', type: 'enum' as const, width: 110, align: 'center' as const, enumLabels: memberRoleLabels },
  { prop: 'memberStatus', label: '租户内状态', type: 'tag' as const, width: 104, align: 'center' as const, tagText: (row: TenantMembership) => accountStatusLabels[row.memberStatus], tagTone: (row: TenantMembership) => (row.memberStatus === 'Enabled' ? ('success' as const) : ('danger' as const)) },
  { prop: 'joinAt', label: '加入时间', type: 'time' as const, width: 150 },
  { prop: 'lastLoginAt', label: '最近登录', type: 'time' as const, width: 150 },
]

const logColumns = [
  { prop: 'at', label: '时间', type: 'time' as const, width: 160 },
  { prop: 'tenantName', label: '登录上下文', minWidth: 200 },
  { prop: 'result', label: '结果', type: 'tag' as const, width: 90, align: 'center' as const, tagText: (row: LoginLog) => (row.result === 'Success' ? '成功' : '失败'), tagTone: (row: LoginLog) => (row.result === 'Success' ? ('success' as const) : ('danger' as const)) },
  { prop: 'ipAddress', label: 'IP 地址', type: 'code' as const, width: 150 },
]

const membershipRows = computed(() =>
  memberships.value.map((membership) => ({
    ...membership,
    memberStatusText: accountStatusLabels[membership.memberStatus],
  })),
)

async function load(): Promise<void> {
  loading.value = true
  try {
    const detail = await fetchUserDetail(userId)
    user.value = detail.user
    memberships.value = detail.memberships
    logs.value = detail.logs
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载用户详情失败')
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <QlxPagePanel
    v-loading="loading"
    title="用户详情"
    subtitle="自然人是全局身份（类似电子税务局个人账号），一个自然人可关联多个租户成员关系"
  >
    <template #toolbar>
      <el-tag
        v-if="user"
        :type="user.status === 'Enabled' ? 'success' : 'danger'"
        size="large"
        effect="light"
      >
        {{ accountStatusLabels[user.status] }}
      </el-tag>
      <a class="back-link" href="/portal/users" target="_blank" rel="noopener">
        <AppIcon name="ArrowLeft" :size="14" />
        返回列表
      </a>
    </template>

    <QlxDescriptionList v-if="user" :items="descriptionItems" />

    <section class="detail-section">
      <h2 class="detail-section__title">关联租户（{{ memberships.length }}）</h2>
      <QlxDataTable
        :items="membershipRows"
        :columns="membershipColumns"
        :total="membershipRows.length"
        :page-size="10"
        empty-text="该自然人尚未关联任何租户"
      />
      <p class="detail-section__note">
        实际可访问 = portal_users 全局启用 且 租户启用 且 成员在本租户内启用。
      </p>
    </section>

    <section class="detail-section">
      <h2 class="detail-section__title">登录日志（{{ logs.length }}）</h2>
      <QlxDataTable
        :items="logs"
        :columns="logColumns"
        :total="logs.length"
        :page-size="10"
        empty-text="暂无登录记录"
      />
    </section>
  </QlxPagePanel>
</template>

<style scoped>
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

.detail-section {
  margin-top: var(--qlx-space-6);
}

.detail-section__title {
  margin-bottom: var(--qlx-space-3);
  padding-left: var(--qlx-space-3);
  border-left: 3px solid var(--qlx-gold-500);
  color: var(--qlx-brand-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-md);
  font-weight: 600;
  letter-spacing: 0.04em;
}

.detail-section__note {
  margin-top: var(--qlx-space-2);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}
</style>
