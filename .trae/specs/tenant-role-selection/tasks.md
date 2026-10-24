# 企业选择页「角色维度」修复 — 任务清单

> 关联规格：[spec.md](./spec.md)
> 执行原则：每完成一项立即编译/校验，通过后再进入下一项。

## 阶段 A：后端契约与 Bas 实现

- [x] A1 新增 `src/Qlx.Abstractions/Queries/ITenantMemberRoleQuery.cs`
  - `TenantMemberRoleItem(long RoleId, string RoleCode, string RoleName, bool IsBuiltin)`
  - `ListMemberRolesAsync(long tenantMemberId, long tenantId, CancellationToken)`
- [x] A2 `QlxClaimConstants` 新增 `MemberRoleId = "member_role_id"`
- [x] A3 新增 `src/Qlx.Modules.Bas/Services/BasTenantMemberRoleQuery.cs`
  - join `MemberRole` + `Role`，过滤 `TenantId` / `IsDeleted`，`orderby` 在 `Select` 前
- [x] A4 `BasServiceCollectionExtensions` 注册 `ITenantMemberRoleQuery`
- [x] A5 `dotnet build` 通过

## 阶段 B：Portal 查询端点

- [x] B1 新增 `Auth/MemberRoleResponses.cs`（`MemberRoleOptionItem`）
- [x] B2 新增 `Services/TenantMemberRoleService.cs`
  - 身份级 Token 校验、成员/租户状态校验
  - 临时租户上下文（`SetTenantId` + `finally` 恢复）
  - 角色 Id 混淆、TenantAdmin 返回空数组
- [x] B3 新增 `Controllers/TenantMemberRoleController.cs`
  - `GET api/portal/tenants/{tenantId}/member-roles`，禁用 `:long` 约束
- [x] B4 Portal DI 注册 `ITenantMemberRoleService`
- [x] B5 `dotnet build` 通过

## 阶段 C：Phase 3 按角色签发

- [x] C1 `PortalConstants.Parameters.MemberRoleId = "member_role_id"`
- [x] C2 `QlxAuthConstants.Claims.MemberRoleId`
- [x] C3 `AuthorizationController` 传递 `member_role_id`
- [x] C4 `IPortalAuthenticationService.EnterTenantContextAsync` 新增参数
- [x] C5 `EnterTenantContextAsync` 角色解析/校验/拒绝分支
- [x] C6 `ResolveTenantPermissionsAsync` 支持按角色取权限（含回退）
- [x] C7 `CreateTenantPrincipal` 写入 `member_role_id` claim
- [x] C8 `RenewTenantTokenAsync` 还原角色并保持一致
- [x] C9 `dotnet build` + `dotnet test` 通过

## 阶段 D：前端类型 / API / Store

- [x] D1 `types/auth.ts` 新增 `MemberRoleOption`
- [x] D2 `api/portal.ts` 新增 `fetchMemberRoles`，`loginTenant` 支持 `roleId`
- [x] D3 `stores/auth.ts`
  - `memberRoleId`、`tenantRoles` 状态
  - `loadMemberRoles`（缓存 + 并发去重）
  - `enterTenant(tenant, roleId?)`、claims 解析 `member_role_id`
  - 单企业自动进入时的角色判定（单角色/无角色/TenantAdmin 直进，多角色进角色步骤）
- [x] D4 `npm run build` 通过

## 阶段 E：选择页两步式重设计

- [x] E1 卡片结构改为 `article` + 独立「进入企业」按钮
- [x] E2 点击企业后才懒加载角色（缓存 + 并发去重）
- [x] E3 单角色 / 无角色 / TenantAdmin → 直接 `enterTenant`
- [x] E4 多角色 → 切换「选择角色」视图（电子税务局式）
  - 步骤条扩展为 4 步；返回「更换企业」；默认选中第一个
  - fieldset/legend、radio 行式卡片、未选禁用「确认进入」
  - 加载骨架 / 错误重试 / 提交中禁用
- [x] E5 企业列表自适应栅格（1/2/3 等分铺满，≥4 三列换行，<860px 单列）
- [x] E6 可访问性与焦点管理（aria-busy、role=alert、视图切换聚焦标题）
- [x] E7 `npm run build` + `npm test` 通过（44 tests / 4 files 全绿）

## 阶段 F：联调与验证

- [x] F1 启动 Host，冒烟 `member-roles` 端点（成功 / 非成员 403）
- [x] F2 冒烟 Phase 3 带 `member_role_id`（正确角色权限）
- [x] F3 冒烟越权角色拒绝
- [x] F4 冒烟静默续期角色保持
- [x] F5 浏览器验证：懒加载时机、两步式角色步骤、1/2/3/4+ 企业布局、单租户+门户管理员
- [x] F6 回归：门户入口、单企业自动进入、退出登录、搜索过滤
- [x] F7 汇总改动清单与验证结果（不新建多余文档）

## 遗留项（不在本次范围）

- Bas 角色 `DefaultDataScope` / `DataScopeDeptId` 尚未纳入 `ITenantRolePermissionQuery` 返回结构，Phase 3 数据范围仍为 `null`。
- 角色切换后的前端工作台数据重载策略（当前为整页重载）。
