/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：应用入口——装配 Pinia / 路由 / Element Plus / 权限指令，恢复会话并开启跨标签页同步
 */

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import 'element-plus/dist/index.css'
import '@/styles/index.css'
import App from './App.vue'
import router from './router'
import { useAuthStore } from '@/stores/auth'
import { vPermission } from '@/directives/permission'

const app = createApp(App)
const pinia = createPinia()

app.use(pinia)
app.use(router)
app.use(ElementPlus, { locale: zhCn })

// v1.1：注册按钮级权限指令 v-permission="'portal.role.edit'"
app.directive('permission', vPermission)

const auth = useAuthStore(pinia)
auth.restore()
auth.startSync(router)

app.mount('#app')
