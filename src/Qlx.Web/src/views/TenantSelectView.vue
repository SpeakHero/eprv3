<!--
  所属公司：四川淇利馨集团有限公司
  描述：企业选择页——两步式（先选企业，再选角色），角色在点击企业后懒加载
-->
<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Search } from '@element-plus/icons-vue'
import BrandMark from '@/components/BrandMark.vue'
import PortalFooter from '@/components/PortalFooter.vue'
import { formatLastLogin } from '@/data/workbench'
import { useAuthStore } from '@/stores/auth'
import { memberRoleLabel } from '@/types/auth'
import type { MemberRoleOption, TenantSummary } from '@/types/auth'

const auth = useAuthStore()
const router = useRouter()

const keyword = ref('')
const enteringId = ref('')
const enteringPortal = ref(false)

/** 当前视图：企业列表 / 选择角色步骤。 */
const view = ref<'tenant' | 'role'>('tenant')
/** 角色步骤对应的企业。 */
const selectedTenant = ref<TenantSummary | null>(null)
/** 角色选项（点击企业后懒加载）。 */
const roles = ref<MemberRoleOption[]>([])
const rolesLoading = ref(false)
const rolesError = ref('')
const selectedRoleId = ref('')
const roleHeading = ref<HTMLElement | null>(null)

const filteredTenants = computed<TenantSummary[]>(() => {
  const value = keyword.value.trim().toLowerCase()
  if (!value) {
    return auth.tenants
  }
  return auth.tenants.filter(
    (tenant) =>
      tenant.displayName.toLowerCase().includes(value) ||
      tenant.tenantCode.toLowerCase().includes(value),
  )
})

/** 企业栅格列数：≤3 等分铺满，≥4 三列换行。 */
const gridClass = computed(() => {
  const count = filteredTenants.value.length
  if (count === 0) {
    return ''
  }
  return count >= 4 ? 'tenant__grid--many' : `tenant__grid--${count}`
})

/** 企业卡片首字（中文取首个汉字，英文取首字母）。 */
function initialOf(tenant: TenantSummary): string {
  return tenant.displayName.slice(0, 1)
}

function errorText(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback
}

/** 打开角色步骤并定位焦点。 */
async function openRoleStep(tenant: TenantSummary, list: MemberRoleOption[]): Promise<void> {
  selectedTenant.value = tenant
  roles.value = list
  selectedRoleId.value = list[0]?.roleId ?? ''
  rolesError.value = ''
  view.value = 'role'
  await nextTick()
  roleHeading.value?.focus()
}

/** 实际执行 Phase 3 进入（调用方负责 enteringId 状态）。 */
async function doEnter(tenant: TenantSummary, roleId?: string): Promise<void> {
  await auth.enterTenant(tenant, roleId)
  await router.replace('/workbench')
}

/**
 * 进入企业：懒加载角色。
 * 单角色 / 无角色 / 企业管理员 → 直接进入；多角色 → 角色步骤。
 */
async function beginEnter(tenant: TenantSummary): Promise<void> {
  if (enteringId.value) {
    return
  }
  enteringId.value = tenant.tenantId
  try {
    const list = await auth.loadMemberRoles(tenant.tenantId)
    if (list.length <= 1) {
      await doEnter(tenant, list[0]?.roleId)
      return
    }
    await openRoleStep(tenant, list)
  } catch (error) {
    ElMessage.error(errorText(error, '获取企业角色失败，请稍后重试'))
  } finally {
    enteringId.value = ''
  }
}

/** 角色步骤确认进入。 */
async function confirmRole(): Promise<void> {
  if (!selectedTenant.value || !selectedRoleId.value || enteringId.value) {
    return
  }
  enteringId.value = selectedTenant.value.tenantId
  try {
    await doEnter(selectedTenant.value, selectedRoleId.value)
  } catch (error) {
    ElMessage.error(errorText(error, '进入企业失败，请稍后重试'))
  } finally {
    enteringId.value = ''
  }
}

/** 角色加载失败重试（失败不写缓存，可直接重请求）。 */
async function retryRoles(): Promise<void> {
  if (!selectedTenant.value || rolesLoading.value) {
    return
  }
  rolesLoading.value = true
  rolesError.value = ''
  try {
    const list = await auth.loadMemberRoles(selectedTenant.value.tenantId)
    roles.value = list
    selectedRoleId.value = list[0]?.roleId ?? ''
    if (list.length <= 1) {
      await doEnter(selectedTenant.value, list[0]?.roleId)
    }
  } catch (error) {
    rolesError.value = errorText(error, '角色加载失败，请稍后重试')
  } finally {
    rolesLoading.value = false
  }
}

/** 返回企业列表（保留搜索词）。 */
function backToTenants(): void {
  view.value = 'tenant'
  selectedTenant.value = null
  roles.value = []
  rolesError.value = ''
  selectedRoleId.value = ''
}

/** 进入门户管理（门户管理员专享，凭身份级 Token）。 */
async function enterPortal(): Promise<void> {
  if (enteringPortal.value) {
    return
  }
  enteringPortal.value = true
  try {
    await auth.enterPortalAdmin()
    await router.replace('/workbench')
  } catch (error) {
    ElMessage.error(errorText(error, '进入门户管理失败，请稍后重试'))
  } finally {
    enteringPortal.value = false
  }
}

async function switchAccount(): Promise<void> {
  await auth.signOut()
  await router.replace('/login')
}

// 登录后单企业多角色：直接落在角色步骤
onMounted(async () => {
  const pendingId = auth.pendingRoleTenantId
  if (!pendingId) {
    return
  }
  const tenant = auth.tenants.find((item) => item.tenantId === pendingId)
  auth.pendingRoleTenantId = ''
  if (!tenant) {
    return
  }

  rolesLoading.value = true
  await openRoleStep(tenant, [])
  try {
    const list = await auth.loadMemberRoles(tenant.tenantId)
    roles.value = list
    selectedRoleId.value = list[0]?.roleId ?? ''
    if (list.length <= 1) {
      await doEnter(tenant, list[0]?.roleId)
    }
  } catch (error) {
    rolesError.value = errorText(error, '角色加载失败，请稍后重试')
  } finally {
    rolesLoading.value = false
  }
})
</script>

<template>
  <div class="tenant">
    <header class="tenant__top">
      <div class="qlx-container tenant__top-inner">
        <BrandMark compact />
        <div class="tenant__account">
          <span class="tenant__account-label">当前账号</span>
          <strong>{{ auth.displayName || auth.loginAccount }}</strong>
          <button type="button" class="tenant__switch" @click="switchAccount">退出登录</button>
        </div>
      </div>
    </header>

    <main class="tenant__main">
      <div class="qlx-container">
        <ol class="tenant__steps" aria-label="登录进度">
          <li class="is-done"><span>1</span>身份认证</li>
          <li :class="view === 'role' ? 'is-done' : 'is-current'"><span>2</span>选择企业</li>
          <template v-if="view === 'role'">
            <li class="is-current"><span>3</span>选择角色</li>
            <li><span>4</span>进入工作台</li>
          </template>
          <li v-else><span>3</span>进入工作台</li>
        </ol>

        <!-- ============ 视图 A：企业列表 ============ -->
        <template v-if="view === 'tenant'">
          <div class="tenant__heading">
            <div>
              <p class="qlx-eyebrow">SELECT ENTERPRISE</p>
              <h1>选择进入的企业</h1>
              <p class="tenant__sub">
                <template v-if="auth.tenants.length">
                  当前账号共关联
                  <strong class="qlx-numeric">{{ auth.tenants.length }}</strong>
                  家企业，请选择本次办理的经营主体；进入后可在顶部随时切换。
                </template>
                <template v-else>
                  当前账号为门户管理员，尚未关联企业，可从下方进入门户管理开展平台运营。
                </template>
              </p>
            </div>
            <el-input
              v-model="keyword"
              class="tenant__search"
              placeholder="搜索企业名称或编码"
              clearable
              :prefix-icon="Search"
            />
          </div>

          <button
            v-if="auth.isPortalAdminAccount"
            type="button"
            class="portal-entry"
            :disabled="enteringPortal"
            @click="enterPortal"
          >
            <span class="portal-entry__seal" aria-hidden="true">管</span>
            <span class="portal-entry__body">
              <span class="portal-entry__name">门户管理</span>
              <span class="portal-entry__desc">租户开通 · 模块许可 · 系统参数（平台运营功能）</span>
            </span>
            <span class="portal-entry__action">
              {{ enteringPortal ? '正在进入…' : '进入门户管理' }}
              <span class="tenant-card__arrow" aria-hidden="true">→</span>
            </span>
          </button>

          <div v-if="filteredTenants.length" class="tenant__grid" :class="gridClass">
            <article
              v-for="tenant in filteredTenants"
              :key="tenant.tenantId"
              class="tenant-card"
              :class="{ 'is-entering': enteringId === tenant.tenantId }"
            >
              <header class="tenant-card__head">
                <span class="tenant-card__seal" aria-hidden="true">{{ initialOf(tenant) }}</span>
                <div class="tenant-card__body">
                  <h2 class="tenant-card__name">{{ tenant.displayName }}</h2>
                  <span class="tenant-card__code">{{ tenant.taxNo || tenant.tenantCode }}</span>
                  <span class="tenant-card__meta">
                    <span
                      class="tenant-card__role"
                      :class="{ 'is-admin': tenant.memberRole === 'TenantAdmin' }"
                    >
                      {{ memberRoleLabel(tenant.memberRole) }}
                    </span>
                    <span class="tenant-card__last">
                      {{
                        tenant.lastLoginAt
                          ? `最近进入 ${formatLastLogin(tenant.lastLoginAt)}`
                          : '首次进入本企业'
                      }}
                    </span>
                  </span>
                </div>
              </header>
              <footer class="tenant-card__foot">
                <button
                  type="button"
                  class="tenant-card__enter"
                  :disabled="Boolean(enteringId)"
                  @click="beginEnter(tenant)"
                >
                  {{ enteringId === tenant.tenantId ? '加载角色…' : '进入企业' }}
                  <span class="tenant-card__arrow" aria-hidden="true">→</span>
                </button>
              </footer>
            </article>
          </div>

          <el-empty
            v-else
            :description="auth.tenants.length ? '未找到匹配的企业，请调整搜索关键词' : '当前账号尚未关联任何企业'"
          />
        </template>

        <!-- ============ 视图 B：选择角色 ============ -->
        <template v-else>
          <button type="button" class="role-step__back" @click="backToTenants">
            <span aria-hidden="true">←</span> 更换企业
          </button>

          <div v-if="selectedTenant" class="role-step__tenant">
            <span class="tenant-card__seal" aria-hidden="true">{{ initialOf(selectedTenant) }}</span>
            <div class="tenant-card__body">
              <span class="role-step__tenant-name">{{ selectedTenant.displayName }}</span>
              <span class="tenant-card__code">
                {{ selectedTenant.taxNo || selectedTenant.tenantCode }}
              </span>
            </div>
          </div>

          <div class="tenant__heading">
            <div>
              <p class="qlx-eyebrow">SELECT ROLE</p>
              <h1 ref="roleHeading" tabindex="-1">选择进入角色</h1>
              <p class="tenant__sub">
                当前企业在您的账号下关联
                <strong class="qlx-numeric">{{ roles.length }}</strong>
                个角色，请选择本次办理的岗位角色；不同角色对应不同权限范围。
              </p>
            </div>
          </div>

          <p v-if="rolesError" class="role-step__error" role="alert">
            {{ rolesError }}
            <button type="button" class="role-step__retry" @click="retryRoles">重试</button>
          </p>

          <fieldset v-else class="role-step__group" :aria-busy="rolesLoading">
            <legend class="role-step__legend">可选角色</legend>

            <div v-if="rolesLoading" class="role-skeleton" aria-hidden="true">
              <span></span>
              <span></span>
            </div>

            <label
              v-for="role in roles"
              v-else
              :key="role.roleId"
              class="role-option"
              :class="{ 'is-selected': selectedRoleId === role.roleId }"
            >
              <input
                v-model="selectedRoleId"
                class="role-option__input"
                type="radio"
                name="member-role"
                :value="role.roleId"
              />
              <span class="role-option__mark" aria-hidden="true"></span>
              <span class="role-option__body">
                <span class="role-option__name">
                  {{ role.roleName }}
                  <span v-if="role.isBuiltin" class="role-option__tag">内置</span>
                </span>
                <span class="role-option__code">{{ role.roleCode }}</span>
              </span>
            </label>
          </fieldset>

          <div class="role-step__footer">
            <button
              type="button"
              class="role-step__confirm"
              :disabled="!selectedRoleId || Boolean(enteringId) || rolesLoading || Boolean(rolesError)"
              @click="confirmRole"
            >
              {{ enteringId ? '正在进入…' : '确认进入' }}
              <span class="tenant-card__arrow" aria-hidden="true">→</span>
            </button>
          </div>
        </template>
      </div>
    </main>

    <PortalFooter />
  </div>
</template>

<style scoped>
.tenant {
  display: flex;
  min-height: 100%;
  flex-direction: column;
  background:
    radial-gradient(900px 360px at 82% -6%, var(--qlx-brand-50), transparent 72%),
    var(--qlx-canvas);
}

.tenant__top {
  border-bottom: 1px solid var(--qlx-line);
  background: var(--qlx-surface);
}

.tenant__top-inner {
  display: flex;
  min-height: 64px;
  align-items: center;
  justify-content: space-between;
  gap: var(--qlx-space-4);
}

.tenant__account {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  font-size: var(--qlx-text-sm);
}

.tenant__account-label {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.tenant__switch {
  padding: 0 0 0 var(--qlx-space-3);
  border: 0;
  border-left: 1px solid var(--qlx-line);
  background: transparent;
  color: var(--qlx-brand-700);
  font-size: var(--qlx-text-sm);
}

.tenant__switch:hover {
  color: var(--qlx-brand-500);
  text-decoration: underline;
}

.tenant__main {
  flex: 1;
  padding: var(--qlx-space-8) 0 var(--qlx-space-12);
}

/* ============ 步骤条 ============ */
.tenant__steps {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-6);
  margin-bottom: var(--qlx-space-8);
}

.tenant__steps li {
  display: flex;
  align-items: center;
  gap: 6px;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  letter-spacing: 0.06em;
}

.tenant__steps li span {
  display: inline-flex;
  width: 18px;
  height: 18px;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--qlx-ink-200);
  border-radius: var(--qlx-radius-full);
  font-family: var(--qlx-font-mono);
  font-size: 10px;
}

.tenant__steps li.is-done {
  color: var(--qlx-ink-600);
}

.tenant__steps li.is-done span {
  border-color: var(--qlx-brand-300);
  color: var(--qlx-brand-700);
}

.tenant__steps li.is-current {
  color: var(--qlx-brand-800);
  font-weight: 500;
}

.tenant__steps li.is-current span {
  border-color: var(--qlx-brand-700);
  background: var(--qlx-brand-700);
  color: #fff;
}

/* ============ 标题与搜索 ============ */
.tenant__heading {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: var(--qlx-space-6);
  margin-bottom: var(--qlx-space-6);
}

.tenant__heading h1 {
  margin-top: var(--qlx-space-1);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-2xl);
  font-weight: 600;
  letter-spacing: 0.06em;
}

.tenant__heading h1:focus {
  outline: none;
}

.tenant__sub {
  margin-top: var(--qlx-space-2);
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-sm);
}

.tenant__sub strong {
  color: var(--qlx-brand-700);
  font-size: var(--qlx-text-md);
}

.tenant__search {
  width: 280px;
  flex: 0 0 auto;
}

/* ============ 门户管理入口（门户管理员专享，置顶展示） ============ */
.portal-entry {
  display: flex;
  width: 100%;
  align-items: center;
  gap: var(--qlx-space-4);
  margin-bottom: var(--qlx-space-4);
  padding: var(--qlx-space-4) var(--qlx-space-5);
  border: 1px solid var(--qlx-gold-500);
  border-radius: var(--qlx-radius-md);
  background: linear-gradient(96deg, var(--qlx-gold-100), var(--qlx-surface) 58%);
  color: inherit;
  text-align: left;
  transition:
    box-shadow var(--qlx-duration-fast) var(--qlx-ease-out),
    transform var(--qlx-duration-fast) var(--qlx-ease-out);
}

.portal-entry:hover:not(:disabled) {
  box-shadow: var(--qlx-shadow-md);
  transform: translateY(-2px);
}

.portal-entry:disabled {
  cursor: progress;
}

.portal-entry__seal {
  display: inline-flex;
  width: 42px;
  height: 42px;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--qlx-gold-500);
  border-radius: var(--qlx-radius-xs);
  background: var(--qlx-seal);
  color: #fff;
  font-family: var(--qlx-font-serif);
  font-size: 20px;
  font-weight: 600;
  line-height: 1;
}

.portal-entry__body {
  display: flex;
  min-width: 0;
  flex: 1;
  flex-direction: column;
  gap: 2px;
}

.portal-entry__name {
  color: var(--qlx-ink-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-md);
  font-weight: 600;
  letter-spacing: 0.06em;
}

.portal-entry__desc {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.portal-entry__action {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  gap: 4px;
  color: var(--qlx-brand-700);
  font-size: var(--qlx-text-sm);
  white-space: nowrap;
}

/* ============ 企业栅格（≤3 等分铺满 / ≥4 三列换行） ============ */
.tenant__grid {
  display: grid;
  gap: var(--qlx-space-4);
}

.tenant__grid--1 {
  grid-template-columns: minmax(0, 1fr);
}

.tenant__grid--2 {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}

.tenant__grid--3 {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.tenant__grid--many {
  grid-template-columns: repeat(auto-fill, minmax(340px, 1fr));
}

/* ============ 企业卡片 ============ */
.tenant-card {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: var(--qlx-space-4);
  padding: var(--qlx-space-5);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
  transition:
    border-color var(--qlx-duration-fast) var(--qlx-ease-out),
    box-shadow var(--qlx-duration-fast) var(--qlx-ease-out),
    transform var(--qlx-duration-fast) var(--qlx-ease-out);
}

.tenant-card::before {
  position: absolute;
  top: 0;
  left: 0;
  width: 3px;
  height: 100%;
  border-radius: var(--qlx-radius-md) 0 0 var(--qlx-radius-md);
  background: var(--qlx-brand-700);
  content: '';
  opacity: 0;
  transition: opacity var(--qlx-duration-fast) var(--qlx-ease-out);
}

.tenant-card:hover {
  border-color: var(--qlx-brand-300);
  box-shadow: var(--qlx-shadow-md);
  transform: translateY(-2px);
}

.tenant-card:hover::before {
  opacity: 1;
}

.tenant-card.is-entering {
  border-color: var(--qlx-brand-500);
}

.tenant-card__head {
  display: flex;
  align-items: flex-start;
  gap: var(--qlx-space-4);
}

.tenant-card__seal {
  display: inline-flex;
  width: 46px;
  height: 46px;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--qlx-gold-500);
  border-radius: var(--qlx-radius-xs);
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-800);
  font-family: var(--qlx-font-serif);
  font-size: 22px;
  font-weight: 600;
  line-height: 1;
}

.tenant-card__body {
  display: flex;
  min-width: 0;
  flex: 1;
  flex-direction: column;
  gap: 3px;
}

.tenant-card__name {
  margin: 0;
  overflow: hidden;
  color: var(--qlx-ink-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-md);
  font-weight: 600;
  letter-spacing: 0.03em;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.tenant-card__code {
  color: var(--qlx-ink-400);
  font-family: var(--qlx-font-mono);
  font-size: var(--qlx-text-xs);
  letter-spacing: 0.08em;
}

.tenant-card__meta {
  display: flex;
  margin-top: var(--qlx-space-2);
  flex-wrap: wrap;
  align-items: center;
  gap: var(--qlx-space-2);
}

.tenant-card__role {
  display: inline-flex;
  padding: 1px 8px;
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-xs);
  background: var(--qlx-canvas);
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-xs);
}

.tenant-card__role.is-admin {
  border-color: var(--qlx-gold-500);
  background: var(--qlx-gold-100);
  color: var(--qlx-gold-600);
}

.tenant-card__last {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.tenant-card__foot {
  display: flex;
  margin-top: auto;
  align-items: center;
  justify-content: flex-end;
  padding-top: var(--qlx-space-3);
  border-top: 1px dashed var(--qlx-line);
}

.tenant-card__enter {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 6px 14px;
  border: 1px solid var(--qlx-brand-700);
  border-radius: var(--qlx-radius-xs);
  background: transparent;
  color: var(--qlx-brand-700);
  font-size: var(--qlx-text-sm);
  transition:
    background-color var(--qlx-duration-fast) var(--qlx-ease-out),
    color var(--qlx-duration-fast) var(--qlx-ease-out);
}

.tenant-card__enter:hover:not(:disabled) {
  background: var(--qlx-brand-700);
  color: #fff;
}

.tenant-card__enter:disabled {
  cursor: progress;
  opacity: 0.6;
}

.tenant-card__arrow {
  transition: transform var(--qlx-duration-fast) var(--qlx-ease-out);
}

.tenant-card__enter:hover:not(:disabled) .tenant-card__arrow {
  transform: translateX(3px);
}

/* ============ 视图 B：选择角色 ============ */
.role-step__back {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  margin-bottom: var(--qlx-space-5);
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--qlx-brand-700);
  font-size: var(--qlx-text-sm);
}

.role-step__back:hover {
  text-decoration: underline;
}

.role-step__tenant {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-4);
  margin-bottom: var(--qlx-space-6);
  padding: var(--qlx-space-4) var(--qlx-space-5);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
}

.role-step__tenant-name {
  color: var(--qlx-ink-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-md);
  font-weight: 600;
  letter-spacing: 0.03em;
}

.role-step__error {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-3);
  margin: 0 0 var(--qlx-space-4);
  padding: var(--qlx-space-4);
  border: 1px solid var(--qlx-danger-300, #f0b4b4);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-danger-50, #fdf2f2);
  color: var(--qlx-danger-700, #b42318);
  font-size: var(--qlx-text-sm);
}

.role-step__retry {
  padding: 2px 10px;
  border: 1px solid currentColor;
  border-radius: var(--qlx-radius-xs);
  background: transparent;
  color: inherit;
  font-size: var(--qlx-text-xs);
}

.role-step__group {
  display: flex;
  margin: 0;
  padding: 0;
  border: 0;
  flex-direction: column;
  gap: var(--qlx-space-3);
}

.role-step__legend {
  margin-bottom: var(--qlx-space-2);
  padding: 0;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  letter-spacing: 0.08em;
}

.role-option {
  position: relative;
  display: flex;
  width: 100%;
  align-items: center;
  gap: var(--qlx-space-4);
  padding: var(--qlx-space-4) var(--qlx-space-5);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
  cursor: pointer;
  transition:
    border-color var(--qlx-duration-fast) var(--qlx-ease-out),
    box-shadow var(--qlx-duration-fast) var(--qlx-ease-out),
    background-color var(--qlx-duration-fast) var(--qlx-ease-out);
}

.role-option:hover {
  border-color: var(--qlx-brand-300);
}

.role-option:focus-within {
  border-color: var(--qlx-brand-500);
  box-shadow: 0 0 0 3px var(--qlx-brand-50);
}

.role-option.is-selected {
  border-color: var(--qlx-brand-700);
  background: var(--qlx-brand-50);
}

.role-option__input {
  position: absolute;
  width: 1px;
  height: 1px;
  opacity: 0;
}

.role-option__mark {
  display: inline-flex;
  width: 18px;
  height: 18px;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--qlx-ink-300);
  border-radius: var(--qlx-radius-full);
  background: var(--qlx-surface);
}

.role-option.is-selected .role-option__mark {
  border-color: var(--qlx-brand-700);
}

.role-option.is-selected .role-option__mark::after {
  width: 8px;
  height: 8px;
  border-radius: var(--qlx-radius-full);
  background: var(--qlx-brand-700);
  content: '';
}

.role-option__body {
  display: flex;
  min-width: 0;
  flex: 1;
  flex-direction: column;
  gap: 3px;
}

.role-option__name {
  display: inline-flex;
  align-items: center;
  gap: var(--qlx-space-2);
  color: var(--qlx-ink-900);
  font-size: var(--qlx-text-md);
  font-weight: 600;
}

.role-option__tag {
  padding: 0 6px;
  border: 1px solid var(--qlx-gold-500);
  border-radius: var(--qlx-radius-xs);
  background: var(--qlx-gold-100);
  color: var(--qlx-gold-600);
  font-size: 10px;
  font-weight: 400;
  letter-spacing: 0.04em;
}

.role-option__code {
  color: var(--qlx-ink-400);
  font-family: var(--qlx-font-mono);
  font-size: var(--qlx-text-xs);
  letter-spacing: 0.08em;
}

.role-skeleton {
  display: flex;
  flex-direction: column;
  gap: var(--qlx-space-3);
}

.role-skeleton span {
  height: 64px;
  border-radius: var(--qlx-radius-md);
  background: linear-gradient(
    90deg,
    var(--qlx-canvas) 25%,
    var(--qlx-surface) 37%,
    var(--qlx-canvas) 63%
  );
  background-size: 400% 100%;
  animation: role-skeleton 1.4s ease infinite;
}

@keyframes role-skeleton {
  0% {
    background-position: 100% 50%;
  }

  100% {
    background-position: 0 50%;
  }
}

.role-step__footer {
  display: flex;
  margin-top: var(--qlx-space-6);
  justify-content: flex-end;
}

.role-step__confirm {
  display: inline-flex;
  min-width: 148px;
  align-items: center;
  justify-content: center;
  gap: 4px;
  padding: 10px 22px;
  border: 1px solid var(--qlx-brand-700);
  border-radius: var(--qlx-radius-xs);
  background: var(--qlx-brand-700);
  color: #fff;
  font-size: var(--qlx-text-sm);
  transition: background-color var(--qlx-duration-fast) var(--qlx-ease-out);
}

.role-step__confirm:hover:not(:disabled) {
  background: var(--qlx-brand-800);
}

.role-step__confirm:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

@media (max-width: 860px) {
  .tenant__heading {
    flex-direction: column;
    align-items: stretch;
  }

  .tenant__search {
    width: 100%;
  }

  .tenant__grid {
    grid-template-columns: minmax(0, 1fr) !important;
  }

  .tenant__steps {
    gap: var(--qlx-space-3);
  }
}

@media (prefers-reduced-motion: reduce) {
  .role-skeleton span {
    animation: none;
  }

  .tenant-card:hover,
  .portal-entry:hover:not(:disabled) {
    transform: none;
  }
}
</style>
