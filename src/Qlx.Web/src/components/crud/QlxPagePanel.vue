<!--
  所属公司：四川淇利馨集团有限公司
  描述：通用页面面板——标题 + 描述 + 右侧工具按钮 + 内容插槽，管理端各页面复用
-->
<script setup lang="ts">
defineProps<{
  title: string
  subtitle?: string
}>()
</script>

<template>
  <section class="qlx-panel">
    <header class="qlx-panel__head">
      <div class="qlx-panel__titles">
        <h1 class="qlx-panel__title">{{ title }}</h1>
        <p v-if="subtitle" class="qlx-panel__subtitle">{{ subtitle }}</p>
      </div>
      <div class="qlx-panel__toolbar">
        <slot name="toolbar" />
      </div>
    </header>
    <div class="qlx-panel__body">
      <slot />
    </div>
  </section>
</template>

<style scoped>
.qlx-panel {
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
  box-shadow: var(--qlx-shadow-xs);
}

.qlx-panel__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--qlx-space-4);
  padding: var(--qlx-space-4) var(--qlx-space-5);
  border-bottom: 1px solid var(--qlx-line);
}

.qlx-panel__title {
  color: var(--qlx-brand-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-lg);
  font-weight: 600;
  letter-spacing: 0.04em;
}

.qlx-panel__subtitle {
  margin-top: 2px;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.qlx-panel__body {
  padding: var(--qlx-space-4) var(--qlx-space-5) var(--qlx-space-5);
}
</style>
