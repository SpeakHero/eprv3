<!--
  所属公司：四川淇利馨集团有限公司
  描述：门户页脚——单位信息 / 服务热线 / 版本与备案，登录页、企业选择页、工作台复用
-->
<template>
  <footer class="portal-footer">
    <div class="qlx-container portal-footer__inner">
      <div class="portal-footer__group">
        <span class="portal-footer__strong">四川淇利馨集团有限公司</span>
        <span class="portal-footer__sep" aria-hidden="true">|</span>
        <span>服务热线 0830-0000000</span>
        <span class="portal-footer__sep" aria-hidden="true">|</span>
        <span>工作日 08:30–18:00</span>
      </div>
      <div class="portal-footer__group">
        <span>平台版本 v0.1.0</span>
        <span class="portal-footer__sep" aria-hidden="true">|</span>
        <span>川 ICP 备 2026000000 号</span>
        <span class="portal-footer__sep" aria-hidden="true">|</span>
        <span>建议使用 Chrome / Edge 浏览器</span>
      </div>
    </div>
  </footer>
</template>

<style scoped>
.portal-footer {
  border-top: 1px solid var(--qlx-line);
  background: var(--qlx-surface);
}

.portal-footer__inner {
  display: flex;
  min-height: 52px;
  align-items: center;
  justify-content: space-between;
  gap: var(--qlx-space-3);
  padding-top: var(--qlx-space-2);
  padding-bottom: var(--qlx-space-2);
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
}

.portal-footer__group {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}

.portal-footer__strong {
  color: var(--qlx-ink-700);
  font-weight: 500;
}

.portal-footer__sep {
  color: var(--qlx-ink-200);
}

@media (max-width: 860px) {
  .portal-footer__inner {
    flex-direction: column;
    align-items: flex-start;
    gap: 2px;
    padding-top: var(--qlx-space-3);
    padding-bottom: var(--qlx-space-3);
  }
}
</style>
