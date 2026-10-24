<!--
  所属公司：四川淇利馨集团有限公司
  描述：登录页——账号密码 + 企业标识（选填），版式参照电子税务局政务门户
-->
<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import { Lock, OfficeBuilding, User } from '@element-plus/icons-vue'
import BrandMark from '@/components/BrandMark.vue'
import PortalFooter from '@/components/PortalFooter.vue'
import { useAuthStore } from '@/stores/auth'

const REMEMBER_KEY = 'qlx.login.account'

const auth = useAuthStore()
const router = useRouter()

const formRef = ref<FormInstance>()
const loading = ref(false)
const remember = ref(false)

const rememberedAccount = localStorage.getItem(REMEMBER_KEY) ?? ''
remember.value = Boolean(rememberedAccount)

const form = reactive({
  loginAccount: rememberedAccount,
  password: '',
  tenantCode: '',
})

const rules: FormRules<typeof form> = {
  loginAccount: [
    { required: true, message: '请输入登录账号', trigger: 'blur' },
    { min: 3, max: 64, message: '账号长度为 3–64 个字符', trigger: 'blur' },
  ],
  password: [
    { required: true, message: '请输入登录密码', trigger: 'blur' },
    { min: 6, message: '密码长度不少于 6 位', trigger: 'blur' },
  ],
}

async function submit(): Promise<void> {
  if (loading.value || !formRef.value) {
    return
  }
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) {
    return
  }

  loading.value = true
  try {
    const result = await auth.signIn(form.loginAccount.trim(), form.password, form.tenantCode)
    if (remember.value) {
      localStorage.setItem(REMEMBER_KEY, form.loginAccount.trim())
    } else {
      localStorage.removeItem(REMEMBER_KEY)
    }
    if (result.notice) {
      ElMessage.warning(result.notice)
    }
    await router.replace(result.path)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '登录失败，请稍后重试')
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login">
    <header class="login__top">
      <div class="qlx-container login__top-inner">
        <BrandMark />
        <span class="login__top-note">统一账号 · 多企业切换 · 全链路留痕</span>
      </div>
    </header>

    <main class="login__main">
      <div class="login__shell">
        <section class="login__intro">
          <p class="login__eyebrow">QILIXIN · DIGITAL OPERATIONS PLATFORM</p>
          <h1 class="login__title">一网通办<br />集团经营管理</h1>
          <p class="login__desc">
            采购、销售、库存、财务、票据、人事与项目全链路在线办理；数据一次录入，集团全域贯通。
          </p>
          <ul class="login__features">
            <li>
              <span class="login__feature-no">01</span>
              <span>
                <strong>一次登录，多企业通行</strong>
                <em>集团统一账号关联多家企业，按需切换经营主体。</em>
              </span>
            </li>
            <li>
              <span class="login__feature-no">02</span>
              <span>
                <strong>业务与财务一体贯通</strong>
                <em>单据驱动台账与凭证，全链路双向穿透可追溯。</em>
              </span>
            </li>
            <li>
              <span class="login__feature-no">03</span>
              <span>
                <strong>操作留痕，权限到人</strong>
                <em>登录锁定、审批留痕、数据授权三重安全防护。</em>
              </span>
            </li>
          </ul>
          <p class="login__intro-foot">四川淇利馨集团有限公司 · 数智经营管理平台</p>
        </section>

        <section class="login__panel">
          <div class="login__panel-head">
            <p class="qlx-eyebrow">USER LOGIN</p>
            <h2>用户登录</h2>
            <p class="login__panel-sub">请使用集团统一账号登录</p>
          </div>

          <el-form
            ref="formRef"
            :model="form"
            :rules="rules"
            label-position="top"
            size="large"
            @submit.prevent="submit"
          >
            <el-form-item label="登录账号" prop="loginAccount">
              <el-input
                v-model="form.loginAccount"
                placeholder="请输入登录账号"
                autocomplete="username"
                :prefix-icon="User"
              />
            </el-form-item>

            <el-form-item label="登录密码" prop="password">
              <el-input
                v-model="form.password"
                type="password"
                show-password
                placeholder="请输入登录密码"
                autocomplete="current-password"
                :prefix-icon="Lock"
                @keyup.enter="submit"
              />
            </el-form-item>

            <el-form-item label="企业名称 / 税号（选填）" prop="tenantCode">
              <el-input
                v-model="form.tenantCode"
                placeholder="填写企业名称或税号，可直接进入该企业"
                autocomplete="off"
                :prefix-icon="OfficeBuilding"
                @keyup.enter="submit"
              />
            </el-form-item>

            <div class="login__options">
              <el-checkbox v-model="remember">记住账号</el-checkbox>
              <span class="login__help">忘记密码请联系集团管理员</span>
            </div>

            <el-button
              type="primary"
              native-type="submit"
              class="login__submit"
              :loading="loading"
            >
              {{ loading ? '正在验证身份…' : '登 录' }}
            </el-button>
          </el-form>

          <div class="login__security">
            <span class="login__security-seal" aria-hidden="true">安</span>
            <p>
              本平台已启用密码错误锁定与操作留痕。请勿共享账号，离开时请及时退出登录。
            </p>
          </div>
        </section>
      </div>
    </main>

    <PortalFooter />
  </div>
</template>

<style scoped>
.login {
  display: flex;
  min-height: 100%;
  flex-direction: column;
  background:
    radial-gradient(1200px 420px at 18% -10%, var(--qlx-brand-50), transparent 70%),
    var(--qlx-canvas);
}

/* ============ 顶部品牌条 ============ */
.login__top {
  border-bottom: 1px solid var(--qlx-line);
  background: var(--qlx-surface);
}

.login__top-inner {
  display: flex;
  min-height: 68px;
  align-items: center;
  justify-content: space-between;
  gap: var(--qlx-space-4);
}

.login__top-note {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  letter-spacing: 0.12em;
}

/* ============ 主体 ============ */
.login__main {
  display: flex;
  flex: 1;
  align-items: center;
  justify-content: center;
  padding: var(--qlx-space-10) var(--qlx-space-6);
}

.login__shell {
  display: grid;
  width: 100%;
  max-width: 1060px;
  overflow: hidden;
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
  box-shadow: var(--qlx-shadow-md);
  grid-template-columns: minmax(0, 1.05fr) minmax(0, 1fr);
}

/* ============ 左侧介绍 ============ */
.login__intro {
  position: relative;
  display: flex;
  overflow: hidden;
  flex-direction: column;
  padding: var(--qlx-space-12) var(--qlx-space-10);
  background:
    linear-gradient(155deg, var(--qlx-brand-900) 0%, var(--qlx-brand-800) 58%, var(--qlx-brand-700) 100%);
  color: #fff;
}

/* 细密网格纹理，克制不喧宾夺主 */
.login__intro::before {
  position: absolute;
  inset: 0;
  background-image:
    linear-gradient(rgb(255 255 255 / 5%) 1px, transparent 1px),
    linear-gradient(90deg, rgb(255 255 255 / 5%) 1px, transparent 1px);
  background-size: 44px 44px;
  content: '';
  mask-image: linear-gradient(180deg, rgb(0 0 0 / 70%), transparent 78%);
}

/* 右上角鎏金弧线 */
.login__intro::after {
  position: absolute;
  top: -120px;
  right: -110px;
  width: 320px;
  height: 320px;
  border: 1px solid rgb(191 155 48 / 45%);
  border-radius: 50%;
  box-shadow:
    0 0 0 26px rgb(191 155 48 / 8%),
    0 0 0 56px rgb(191 155 48 / 5%);
  content: '';
}

.login__eyebrow {
  position: relative;
  color: var(--qlx-gold-400);
  font-family: var(--qlx-font-mono);
  font-size: 11px;
  letter-spacing: 0.18em;
}

.login__title {
  position: relative;
  margin-top: var(--qlx-space-5);
  font-family: var(--qlx-font-serif);
  font-size: 40px;
  font-weight: 600;
  letter-spacing: 0.08em;
  line-height: 1.32;
}

.login__desc {
  position: relative;
  max-width: 34em;
  margin-top: var(--qlx-space-4);
  color: rgb(255 255 255 / 72%);
  font-size: var(--qlx-text-sm);
  line-height: 1.9;
}

.login__features {
  position: relative;
  display: flex;
  margin-top: var(--qlx-space-8);
  flex-direction: column;
  gap: var(--qlx-space-4);
}

.login__features li {
  display: flex;
  align-items: flex-start;
  gap: var(--qlx-space-3);
}

.login__feature-no {
  flex: 0 0 auto;
  padding-top: 2px;
  color: var(--qlx-gold-400);
  font-family: var(--qlx-font-mono);
  font-size: var(--qlx-text-xs);
  letter-spacing: 0.06em;
}

.login__features strong {
  display: block;
  font-size: var(--qlx-text-base);
  font-weight: 500;
  letter-spacing: 0.04em;
}

.login__features em {
  display: block;
  margin-top: 2px;
  color: rgb(255 255 255 / 58%);
  font-size: var(--qlx-text-xs);
  font-style: normal;
  line-height: 1.7;
}

.login__intro-foot {
  position: relative;
  margin-top: auto;
  padding-top: var(--qlx-space-8);
  color: rgb(255 255 255 / 42%);
  font-size: var(--qlx-text-xs);
  letter-spacing: 0.06em;
}

/* ============ 右侧登录表单 ============ */
.login__panel {
  padding: var(--qlx-space-12) var(--qlx-space-10);
}

.login__panel-head {
  margin-bottom: var(--qlx-space-6);
}

.login__panel-head h2 {
  margin-top: var(--qlx-space-2);
  color: var(--qlx-ink-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-xl);
  font-weight: 600;
  letter-spacing: 0.1em;
}

.login__panel-sub {
  margin-top: 2px;
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
}

.login__options {
  display: flex;
  margin: calc(var(--qlx-space-1) * -1) 0 var(--qlx-space-4);
  align-items: center;
  justify-content: space-between;
}

.login__help {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.login__submit {
  width: 100%;
  height: 42px;
  font-size: var(--qlx-text-base);
  letter-spacing: 0.24em;
}

.login__security {
  display: flex;
  margin-top: var(--qlx-space-6);
  align-items: flex-start;
  gap: var(--qlx-space-2);
  padding-top: var(--qlx-space-4);
  border-top: 1px dashed var(--qlx-ink-100);
}

.login__security-seal {
  display: inline-flex;
  width: 20px;
  height: 20px;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--qlx-seal);
  border-radius: var(--qlx-radius-xs);
  color: var(--qlx-seal);
  font-family: var(--qlx-font-serif);
  font-size: 12px;
  line-height: 1;
}

.login__security p {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

@media (max-width: 900px) {
  .login__shell {
    grid-template-columns: minmax(0, 1fr);
  }

  .login__intro {
    padding: var(--qlx-space-8) var(--qlx-space-6);
  }

  .login__title {
    font-size: 30px;
  }

  .login__features,
  .login__intro-foot {
    display: none;
  }

  .login__panel {
    padding: var(--qlx-space-8) var(--qlx-space-6);
  }
}

@media (max-width: 560px) {
  .login__top-note {
    display: none;
  }

  .login__main {
    padding: var(--qlx-space-6) var(--qlx-space-4);
  }
}
</style>
