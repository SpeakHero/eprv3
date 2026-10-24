/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：Vitest 配置——复用 vite 别名与插件，仅承载组件单测（不影响业务构建）
 */

import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    include: ['src/**/__tests__/**/*.spec.ts'],
    environment: 'node',
  },
})
