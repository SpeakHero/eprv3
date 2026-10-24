<!--
  所属公司：四川淇利馨集团有限公司
  描述：模块功能页占位——骨架搭建阶段仅有此页，真实功能开发后替换内容
  提示：业务功能页必须原生新标签页打开，此处仅作占位骨架，不实现具体业务
-->
<script setup lang="ts">
import { useRoute } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'
import PortalFooter from '@/components/PortalFooter.vue'
import SiteHeader from '@/components/SiteHeader.vue'
import { findNavItem, findModule } from '@/data/navigation'

const route = useRoute()
const moduleCode = route.params.moduleCode as string
const itemCode = route.params.itemCode as string
const module = findModule(moduleCode)
const item = findNavItem(moduleCode, itemCode)
</script>

<template>
  <div class="module-page">
    <SiteHeader />

    <main class="module-page__main">
      <div class="qlx-container">
        <div class="module-placeholder">
          <div class="module-placeholder__icon">
            <AppIcon name="Grid" :size="32" />
          </div>
          <h1>
            {{ item?.name ?? itemCode }}
            <span class="module-placeholder__note">正在建设中</span>
          </h1>
          <p>
            当前为前端骨架演示占位页，<br />
            后端模块开发完成后会接入真实业务内容。
          </p>
          <p class="module-placeholder__path qlx-numeric">
            {{ module?.name ?? moduleCode }} / {{ item?.name ?? itemCode }}
          </p>
        </div>
      </div>
    </main>

    <PortalFooter />
  </div>
</template>

<style scoped>
.module-page {
  display: flex;
  min-height: 100%;
  flex-direction: column;
  background: var(--qlx-canvas);
}

.module-page__main {
  flex: 1;
  padding: var(--qlx-space-8) 0;
}

.module-placeholder {
  padding: var(--qlx-space-12) var(--qlx-space-6);
  border: 1px dashed var(--qlx-ink-200);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
  text-align: center;
}

.module-placeholder__icon {
  display: inline-flex;
  width: 68px;
  height: 68px;
  align-items: center;
  justify-content: center;
  border: 2px solid var(--qlx-ink-200);
  border-radius: var(--qlx-radius-lg);
  color: var(--qlx-ink-300);
}

.module-placeholder h1 {
  margin-top: var(--qlx-space-4);
  color: var(--qlx-ink-700);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-2xl);
  font-weight: 500;
  letter-spacing: 0.08em;
}

.module-placeholder__note {
  margin-left: 8px;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-sm);
  font-weight: 400;
}

.module-placeholder p {
  margin-top: var(--qlx-space-2);
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-base);
  line-height: 1.8;
}

.module-placeholder__path {
  margin-top: var(--qlx-space-4);
  color: var(--qlx-ink-300);
  font-size: var(--qlx-text-xs);
}
</style>
