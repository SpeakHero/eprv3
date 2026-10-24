import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// 开发期后端 Host 默认 http://localhost:5002（OpenIddict Token 端点 + Portal API）
const backend = process.env.QLX_API_TARGET ?? 'http://localhost:5002'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: backend, changeOrigin: true },
      '/connect': { target: backend, changeOrigin: true },
    },
  },
  build: {
    // 骨架阶段整包引入 Element Plus，按依赖拆分便于长期缓存
    chunkSizeWarningLimit: 1200,
    rollupOptions: {
      output: {
        manualChunks: {
          'element-plus': ['element-plus', '@element-plus/icons-vue'],
          'vue-vendor': ['vue', 'vue-router', 'pinia', 'axios'],
        },
      },
    },
  },
})
