# 企业选择页「角色维度」修复 — 验收清单

> 关联规格：[spec.md](./spec.md) ｜ 任务：[tasks.md](./tasks.md)
> 勾选依据：代码核对 + 构建/单测 + 接口冒烟 + 浏览器实测

## 一、后端契约与实现

- [x] `ITenantMemberRoleQuery` 契约与 `TenantMemberRoleItem` 记录类型存在，实现强制 `TenantId` 过滤
- [x] Bas 实现先 `orderby` 再 `Select`（EF 可翻译），并已在 `BasServiceCollectionExtensions` 注册
- [x] `QlxClaimConstants.MemberRoleId` / `PortalConstants.Parameters.MemberRoleId` / `QlxAuthConstants.Claims.MemberRoleId` 三处常量一致
- [x] `GET api/portal/tenants/{tenantId}/member-roles` 路由无 `:long` 约束，返回 `ApiResponse.Ok`
- [x] 端点仅接受身份级 Token；非成员 / 租户不可用返回 403 统一响应体（非 500）
- [x] 企业管理员返回空数组（不查 Bas），不回退全角色并集

## 二、Phase 3 与续期

- [x] `EnterTenantContextAsync` 接受 `member_role_id`，解码失败返回 `invalid_request`
- [x] 指定角色不属于该成员时拒绝（`access_denied`）
- [x] 多角色未指定角色时拒绝并提示「请选择本次进入的角色」
- [x] 单角色未指定时自动采用该角色；无角色时回退成员全角色并集
- [x] 租户级 Token 写入 `member_role_id` claim（企业管理员不写）
- [x] 静默续期从旧 Token 还原 `member_role_id`，权限保持一致
- [x] 查询 Bas 前临时 `SetTenantId` 并在 `finally` 恢复（无跨租户污染）

## 三、前端类型 / API / Store

- [x] `MemberRoleOption` 类型、`fetchMemberRoles`、`loginTenant(roleId?)` 已就位
- [x] `loadMemberRoles` 命中缓存直接返回，同企业并发请求复用同一 Promise，失败不写缓存
- [x] `memberRoleId` 与会话持久化/恢复一致（`tenantRoles` 仅内存）
- [x] 单企业登录：单角色/无角色/企业管理员直接进入；多角色直接落在角色步骤

## 四、页面交互与布局

- [x] 企业卡片为 `article` + 独立「进入企业」按钮，卡片内无角色选择控件
- [x] 点击企业后才发起 `member-roles` 请求（懒加载），同一企业重复点击复用缓存
- [x] 多角色进入「选择角色」步骤：`← 更换企业`、企业摘要、radio 行式选项、内置标签
- [x] 默认选中第一个角色；未选 / 加载中 / 出错时「确认进入」禁用；出错可重试
- [x] 步骤条随场景在 3 步与 4 步间切换
- [x] 布局：1 家全宽、2 家各半、3 家各 1/3、≥4 三列换行、<860px 单列（门户管理入口同宽置顶）
- [x] 可访问性：`fieldset/legend`、`aria-busy`、`role="alert"`、视图切换聚焦标题、`prefers-reduced-motion`

## 五、构建与验证

- [x] `dotnet build -c Release` 0 错误 0 警告
- [x] `dotnet test` 通过（802 项全绿）
- [x] `npm run build` 通过；`npm run test` 全绿（44 项）
- [x] 接口冒烟：`member-roles` 200/403、Phase 3 带角色 200、越权角色拒绝、续期角色保持
- [x] 浏览器实测：3 家企业三列、搜索过滤为 1 家时全宽、<860px 单列
- [x] 浏览器实测：点击企业 → 角色步骤（2 角色，默认选中，确认进入后工作台顶部显示企业 + 普通成员）
- [x] 浏览器实测：门户管理员入口进入 `/workbench` 门户管理模式
- [x] 回归：单企业 + 多角色 → 直落角色步骤；单企业 + 企业管理员 → 直进工作台；退出登录回 `/login`
- [x] 冒烟测试数据已清理，`qlxadmin` 门户管理员标记与成员状态已还原
