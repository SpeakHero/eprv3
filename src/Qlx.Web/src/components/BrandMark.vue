<!--
  所属公司：四川淇利馨集团有限公司
  描述：品牌标识（徽记 + 中英文名），登录页 / 企业选择 / 工作台 / 新标签页复用
-->
<script setup lang="ts">
withDefaults(
  defineProps<{
    /** light：浅底深字；dark：深底白字。 */
    theme?: 'light' | 'dark'
    /** 是否展示英文副标题。 */
    showSubtitle?: boolean
    /** 紧凑模式（顶栏内使用）。 */
    compact?: boolean
  }>(),
  { theme: 'light', showSubtitle: true, compact: false },
)
</script>

<template>
  <div class="brand" :class="[`brand--${theme}`, { 'brand--compact': compact }]">
    <span class="brand__seal" aria-hidden="true">淇</span>
    <span class="brand__text">
      <strong class="brand__name">淇利馨集团</strong>
      <span v-if="showSubtitle" class="brand__sub">QILIXIN GROUP · 数智经营管理平台</span>
    </span>
  </div>
</template>

<style scoped>
.brand {
  display: inline-flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

.brand__seal {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  flex: 0 0 auto;
  border: 1px solid var(--qlx-gold-500);
  border-radius: var(--qlx-radius-xs);
  background: var(--qlx-brand-700);
  color: #fff;
  font-family: var(--qlx-font-serif);
  font-size: 22px;
  font-weight: 600;
  line-height: 1;
  box-shadow: inset 0 0 0 3px rgb(255 255 255 / 10%);
}

.brand__text {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 2px;
}

.brand__name {
  color: var(--qlx-ink-900);
  font-family: var(--qlx-font-serif);
  font-size: 19px;
  font-weight: 600;
  letter-spacing: 0.08em;
  line-height: 1.2;
  white-space: nowrap;
}

.brand__sub {
  color: var(--qlx-ink-500);
  font-family: var(--qlx-font-mono);
  font-size: 10px;
  letter-spacing: 0.06em;
  line-height: 1.2;
  white-space: nowrap;
}

/* 深色底 */
.brand--dark .brand__seal {
  background: transparent;
  color: var(--qlx-gold-400);
}

.brand--dark .brand__name {
  color: #fff;
}

.brand--dark .brand__sub {
  color: rgb(255 255 255 / 62%);
}

/* 紧凑：用于 60px 顶栏 */
.brand--compact .brand__seal {
  width: 34px;
  height: 34px;
  font-size: 19px;
}

.brand--compact .brand__name {
  font-size: 17px;
}

.brand--compact .brand__sub {
  font-size: 9px;
}

@media (max-width: 560px) {
  .brand__sub {
    display: none;
  }
}
</style>
