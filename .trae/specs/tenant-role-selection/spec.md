# 企业选择页「角色维度」修复与重设计 — 规格说明

> 版本：v1.0（2026-10-23）
> 状态：待审批
> 关联规则：`.trae/rules/global-cache-layering-standard.md`（B-2 后端不得做展示计算）、`.trae/rules/global-csharp-standard.md`

## 1. 问题与目标

### 1.1 现状缺陷
- 企业选择页只展示 `TenantSummaryItem.MemberRole` 只读标签（`TenantAdmin` / `OrdinaryMember`），**无法选择角色**。
- Phase 3 `EnterTenantContextAsync` 只接收 `tenant_id`，权限按成员关联的**全部角色并集**签发，同一账号多角色时无法按角色进入。
- 企业卡片固定 `minmax(340px, 1fr)`，单企业/双企业时右侧大片留白，视觉不均衡。

### 1.2 目标
1. 采用「电子税务局」式两步操作：**先选企业，再选角色**。企业卡片不内嵌角色选择。
   - 点击「进入企业」后才**懒加载**该企业角色；
   - 仅 1 个角色 / TenantAdmin / 无 Bas 角色 → 跳过角色步骤直接进入工作台；
   - 多个角色 → 进入独立「选择角色」步骤，确认后进入工作台。
2. Phase 3 接受 `member_role_id`，按**所选角色**签发权限码（不再全角色并集）。
3. 企业列表布局自适应：企业数 ≤3 时等分铺满一行（1 家全宽 / 2 家各半 / 3 家各 1/3）；>3 时按 3 列栅格换行；<860px 单列。
4. 单租户 + 门户管理员场景：企业卡片与门户管理入口均全宽（自动分列铺满，无需额外占位）。
5. 多租户、不同管理员角色（TenantAdmin / 普通成员 / 门户管理员）下逻辑一致且可兼容。

### 1.3 非目标
- 不改造角色 CRUD / 角色权限分配页面。
- 不在 Phase 1 批量跨租户查询 Bas 角色（Phase 1 无租户上下文，BasTenantDbContext 租户为 0）。
- 不改变租户内角色的权威存储（Bas `bas_roles` / `bas_member_roles` / `bas_role_permissions`）。

## 2. 方案要点

| 维度 | 决策 |
|---|---|
| 角色数据来源 | Bas 模块（租户库），经 `ITenantMemberRoleQuery` 契约读取，禁止 Portal 跨库 Join |
| 拉取时机 | **点击企业后懒加载**：`GET /api/portal/tenants/{tenantId}/member-roles`（身份级 Token），不预取全部企业 |
| 租户上下文切换 | 端点内部临时 `RequestTenantContext.SetTenantId(tenantId)`，`finally` 恢复原值 |
| 权限签发 | 普通成员：`ListRolePermissionCodesAsync(roleId, tenantId)`；无角色/查询未注册：回退 `ListMemberPermissionCodesAsync` |
| TenantAdmin | 零计算旁路（不查角色权限、不写 `qlx_permissions`），前端不展示角色选择 |
| 单角色 | 跳过角色步骤直接进入，前端仍提交该 `role_id`（后端幂等） |
| 多角色 | 进入「选择角色」步骤，必须选择；未选择不允许确认（前端禁用按钮，后端兜底拒绝） |
| 续期一致性 | 租户静默续期从旧 Token 的 `member_role_id` claim 还原所选角色 |

## 3. 后端改动清单

### 3.1 Qlx.Abstractions

**新增** `src/Qlx.Abstractions/Queries/ITenantMemberRoleQuery.cs`
```csharp
namespace Qlx.Abstractions.Queries;

/// <summary>租户成员可进入的角色项（RoleId 进程内为 long，对外由 Portal 混淆）。</summary>
public sealed record TenantMemberRoleItem(long RoleId, string RoleCode, string RoleName, bool IsBuiltin);

/// <summary>租户成员角色只读查询契约；Portal 认证与选择页消费，Bas 实现。</summary>
public interface ITenantMemberRoleQuery : IModuleQuery
{
    Task<IReadOnlyList<TenantMemberRoleItem>> ListMemberRolesAsync(
        long tenantMemberId, long tenantId, CancellationToken cancellationToken);
}
```
约束：实现必须强制 `TenantId == tenantId`；排序在 `Select` 之前、针对实体列（避免 EF 无法翻译）。

**修改** `src/Qlx.Abstractions/Constants/QlxClaimConstants.cs`
- 新增 `public const string MemberRoleId = "member_role_id";`

### 3.2 Qlx.Modules.Bas

**新增** `src/Qlx.Modules.Bas/Services/BasTenantMemberRoleQuery.cs`
```csharp
internal sealed class BasTenantMemberRoleQuery(BasTenantDbContext db) : ITenantMemberRoleQuery
{
    public async Task<IReadOnlyList<TenantMemberRoleItem>> ListMemberRolesAsync(
        long tenantMemberId, long tenantId, CancellationToken cancellationToken)
    {
        var rows = await (
            from mr in db.Set<MemberRole>().AsNoTracking()
            where mr.TenantMemberId == tenantMemberId && mr.TenantId == tenantId
            join r in db.Set<Role>().AsNoTracking() on mr.RoleId equals r.Id
            where r.TenantId == tenantId && !r.IsDeleted
            orderby r.Name, r.Id
            select new TenantMemberRoleItem(r.Id, r.Code, r.Name, r.IsBuiltin))
            .ToListAsync(cancellationToken);
        return rows;
    }
}
```
**修改** `src/Qlx.Modules.Bas/BasServiceCollectionExtensions.cs`（第 56 行附近）
```csharp
services.AddScoped<Qlx.Abstractions.Queries.ITenantMemberRoleQuery, Services.BasTenantMemberRoleQuery>();
```

### 3.3 Qlx.Modules.Portal — 查询端点

**新增** `src/Qlx.Modules.Portal/Auth/MemberRoleResponses.cs`
```csharp
public sealed record MemberRoleOptionItem(string RoleId, string RoleCode, string RoleName, bool IsBuiltin);
```

**新增** `src/Qlx.Modules.Portal/Services/TenantMemberRoleService.cs`
- 接口 `ITenantMemberRoleService.GetMemberRolesAsync(ClaimsPrincipal principal, string tenantIdRaw, CancellationToken ct)`，返回 `IReadOnlyList<MemberRoleOptionItem>?`（`null` = 无权/不可用）。
- 实现步骤：
  1. `token_kind` 必须为 `identity`（选择页仅持身份级 Token）；`subject` 经 `IIdObfuscator` 解码为 `userId`。
  2. `tenantIdRaw` 解码为 `tenantId`，`<= 0` 视为无效。
  3. 查询 `PortalDbContext.TenantMembers.Include(m => m.Tenant)`，条件 `PortalUserId == userId && TenantId == tenantId`；要求 `Status == Enabled`、`Role != PendingInvite`、租户 `Status ∈ { Active, GracePeriod }`。
  4. `ITenantMemberRoleQuery` 延迟解析（`IServiceProvider.GetService`，未注册返回空列表，不抛错）。
  5. 临时租户上下文：记录原值 → `RequestTenantContext.SetTenantId(tenantId)` → 查询 → `finally` 恢复原值。
  6. `roleId` 经 `IIdObfuscator.Encode` 下发；`TenantAdmin` 直接返回空数组（前端不展示角色区）。
- DI 注册：`services.AddScoped<ITenantMemberRoleService, TenantMemberRoleService>()`（Portal 模块扩展方法内）。

**新增** `src/Qlx.Modules.Portal/Controllers/TenantMemberRoleController.cs`
```csharp
[ApiController]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
[Route("api/portal/tenants")]
public sealed class TenantMemberRoleController(ITenantMemberRoleService service) : ControllerBase
{
    [HttpGet("{tenantId}/member-roles")]
    public async Task<IActionResult> MemberRoles(string tenantId, CancellationToken ct)
    {
        var items = await service.GetMemberRolesAsync(User, tenantId, ct);
        return items is null ? Forbid() : Ok(ApiResponse.Ok(items));
    }
}
```
- 路由参数 `{tenantId}` **禁止**加 `:long` 约束（Hashids 混淆串会导致 404）。
- 响应统一 `ApiResponse.Ok(...)`（与现有 Portal 端点一致）。

### 3.4 Qlx.Modules.Portal — Phase 3 按角色签发

**修改** `src/Qlx.Modules.Portal/PortalConstants.cs` → `Parameters`
```csharp
/// <summary>进入租户上下文：所选租户角色 Id（混淆串，可空）。</summary>
public const string MemberRoleId = "member_role_id";
```

**修改** `src/Qlx.Modules.Portal/Auth/QlxAuthConstants.cs` → `Claims`
```csharp
public const string MemberRoleId = QlxClaimConstants.MemberRoleId;
```

**修改** `src/Qlx.Modules.Portal/Controllers/AuthorizationController.cs`
- `EnterTenantContextAsync` 调用处追加参数 `(string?)request[PortalConstants.Parameters.MemberRoleId]`。

**修改** `src/Qlx.Modules.Portal/Services/PortalAuthenticationService.cs`
1. `IPortalAuthenticationService.EnterTenantContextAsync` 签名新增 `string? memberRoleIdRaw`（置于 `tenantIdRaw` 之后）。
2. `EnterTenantContextAsync`：
   - 解码 `memberRoleIdRaw`：空 → `null`；非空但解码失败 → `InvalidRequest("member_role_id 无效")`。
   - 成员校验通过后调用 `ResolveTenantPermissionsAsync(member, requestedRoleId, ct)`。
   - 将 `requestedRoleId` 传入 `CreateTenantPrincipal` 写入 claim（`TenantAdmin` 不写）。
   - 多角色未指定：`InvalidRequest("该企业下存在多个角色，请选择本次进入的角色")`。
   - 指定角色不属于该成员：`AccessDenied("所选角色不属于当前企业成员")`。
3. `ResolveTenantPermissionsAsync` 签名改为 `(TenantMember member, long? memberRoleId, CancellationToken ct)`：
   - `TenantAdmin` → `([], null, null)`。
   - `ITenantMemberRoleQuery` 未注册 → 回退现有 `ListMemberPermissionCodesAsync`。
   - `memberRoleId.HasValue` → `ListRolePermissionCodesAsync(memberRoleId.Value, member.TenantId, ct)`。
   - 未指定且成员仅 1 个角色 → 取该角色（自动锁定）。
   - 未指定且成员 0 个角色 → 回退 `ListMemberPermissionCodesAsync`（兼容历史数据）。
   - 未指定且成员多角色 → 由第 2 步提前拒绝（本方法不会走到）。
4. `CreateTenantPrincipal` 新增可选参数 `long? memberRoleId = null`；非空且非 `TenantAdmin` 时写入 `member_role_id` claim（混淆串）。
5. `RenewTenantTokenAsync`：读取 `QlxAuthConstants.Claims.MemberRoleId` → 解码为 `long?` → 传入 `ResolveTenantPermissionsAsync` 与 `CreateTenantPrincipal`，保证续期不丢所选角色。

### 3.5 后端数据校验（V-1/V-2）
- 角色归属、成员状态、租户状态全部后端复核，不信任前端传参。
- `member_role_id` 混淆串解码失败 / 越权 → 4xx，且不泄露内部 Id。
- 端点仅允许身份级 Token（选择页阶段），不接受租户级 Token 调用。

## 4. 前端改动清单（`src/Qlx.Web`）

### 4.1 类型 `src/types/auth.ts`
```ts
/** 企业下当前账号可进入的角色项。 */
export interface MemberRoleOption {
  roleId: string
  roleCode: string
  roleName: string
  isBuiltin: boolean
}
```

### 4.2 API `src/api/portal.ts`
- 新增 `fetchMemberRoles(identityToken: string, tenantId: string): Promise<MemberRoleOption[]>`：
  `GET /api/portal/tenants/${tenantId}/member-roles`，`Authorization: Bearer <identityToken>`；兼容 `ApiResponse` 包装（`data.data ?? data`）。
- `loginTenant(identityToken, tenantId, roleId?)`：`roleId` 存在时 body 追加 `member_role_id`。

### 4.3 Store `src/stores/auth.ts`
- state 新增：`memberRoleId: ''`、`tenantRoles: {} as Record<string, MemberRoleOption[]>`。
- action 新增 `loadMemberRoles(tenantId: string): Promise<MemberRoleOption[]>`：命中缓存直接返回；同企业并发请求复用同一 Promise；失败不写缓存并抛出。
- action 修改 `enterTenant(tenant: TenantSummary, roleId?: string)`：透传 `roleId` 给 `loginTenant`；`applyTenantTokenClaims` 解析 `member_role_id` 写入 `memberRoleId`。
- `reset()` / `persist()` / `session` 同步字段补齐 `memberRoleId`、`tenantRoles`（`tenantRoles` 仅内存缓存，不落 session）。
- `signIn` 单企业自动进入时懒加载角色：单角色 / TenantAdmin / 无角色 → 直接进入；多角色 → 进选择页并直接落在「选择角色」步骤。

### 4.4 页面 `src/views/TenantSelectView.vue` 重设计（两步式）

**步骤条**：`1 身份认证 → 2 选择企业 → 3 选择角色 → 4 进入工作台`。
角色步骤仅在当前企业存在多角色时出现；单角色/无角色时保持 3 步并直达工作台。

**视图 A：企业列表（默认）**
```
article.tenant-card
├─ header: seal + 名称 + 编码 + 成员角色标签 + 最近进入
└─ footer: 「进入企业」按钮（加载角色中显示 loading）
```
- 卡片由 `<button>` 改为 `<article>` + 独立「进入企业」按钮，避免嵌套交互元素。
- 点击进入 → 调用 `auth.loadMemberRoles(tenant.tenantId)`（此刻才请求，懒加载、按企业缓存）：
  - `TenantAdmin` / 角色数为 0 → 直接 `enterTenant(tenant)`；
  - 角色数为 1 → 直接 `enterTenant(tenant, roles[0].roleId)`；
  - 角色数 ≥2 → 切换到视图 B，并默认选中第一个角色。

**视图 B：选择角色（电子税务局式独立步骤）**
```
section.role-step
├─ 返回：← 更换企业（回到视图 A，保留搜索词）
├─ 企业摘要：seal + 企业名称 + 编码 + 最近进入
├─ fieldset/legend「选择进入角色」
│   ├─ loading：骨架行（首次渲染）
│   ├─ 错误：role="alert" + 重试
│   └─ role-option × N：自定义 radio 卡片（角色名 + 角色编码 + 内置标签）
└─ footer: 「确认进入」按钮（未选 / 提交中 → disabled）
```
- 视图 B 全宽单列，选项为行式卡片（`grid` 单列，宽度 100%），更贴近电子税务局的角色列表形态。
- 角色切换、失败重试不影响企业选择状态。
- 浏览器后退：监听 `popstate` 或提供「更换企业」按钮回到视图 A（同一路由内状态切换，刷新回企业列表）。

**布局（企业列表）**
```css
.tenant__grid { display: grid; gap: var(--qlx-space-4); }
.tenant__grid--1 { grid-template-columns: minmax(0, 1fr); }
.tenant__grid--2 { grid-template-columns: repeat(2, minmax(0, 1fr)); }
.tenant__grid--3 { grid-template-columns: repeat(3, minmax(0, 1fr)); }
.tenant__grid--many { grid-template-columns: repeat(auto-fill, minmax(340px, 1fr)); }
@media (max-width: 860px) { .tenant__grid { grid-template-columns: minmax(0, 1fr); } }
```
- 列数类由 `Math.min(filteredTenants.length, 3)` 计算：`1/2/3` 用等分铺满，`>=4` 用 `--many`。
- 门户管理入口保持全宽置顶，与企业栅格同宽（`--qlx-container` 内）。
- 单租户 + 门户管理员：`--1` 全宽，自然铺满，无需额外占位或分栏。

**可访问性 / 状态**
- 角色步骤使用 `fieldset + legend`；radio 支持方向键与 Space；选项整体可点击。
- 加载态 `aria-busy`；错误态 `role="alert"`；提交禁用 + 文案。
- 视图切换后焦点移至角色组标题；返回按钮可用键盘访问。
- 尊重 `prefers-reduced-motion`（已有全局过渡可复用）。

## 5. 兼容性

| 场景 | 行为 |
|---|---|
| 单租户 + TenantAdmin | 不展示角色，自动进入（登录页直接进工作台，不经选择页） |
| 单租户 + 普通成员单角色 | 自动进入并带 roleId，不出现角色步骤 |
| 单租户 + 普通成员多角色 | 进选择页，必须选择 |
| 多租户 | 点击某企业时才加载该企业角色，不影响其他卡片 |
| 门户管理员 | 门户管理入口全宽置顶；其关联企业卡片正常展示 |
| Bas 角色未初始化 | 端点返回空数组，跳过角色步骤直接进入，Phase 3 回退旧并集逻辑 |
| 旧版前端未传 roleId | 单角色自动采用；多角色返回明确错误（需选择） |
| 租户 Token 静默续期 | 从 `member_role_id` claim 还原，权限保持一致 |

## 6. 验证要求

1. **编译**：`dotnet build`（解决方案全量，0 error；新增文件无警告）。
2. **单测**：`dotnet test` 全量通过；如已有认证测试覆盖 Phase 3，补充「按角色签发 / 越权角色拒绝」用例。
3. **前端**：`npm run build`、`npm run test:unit` 通过。
4. **接口冒烟**（Host 启动后）：
   - Phase 1 登录 → 取身份 Token；
   - `GET /api/portal/tenants/{tenantId}/member-roles` → 200 且角色数组正确；非成员 → 403；
   - Phase 3 带 `member_role_id` → 200，解码租户 Token 校验 `member_role_id` 与 `permissions` 仅含该角色权限；
   - 传入不属于该成员的角色 → 4xx；
   - 静默续期后 `member_role_id` 不变。
5. **页面验证**：`agent-browser` 或本地浏览器检查
   - 1/2/3/4+ 企业布局与 <860px 单列；
   - 点击企业后才发起 `member-roles` 请求（懒加载）；
   - 单角色/无角色/TenantAdmin 直接进入；多角色进入「选择角色」步骤；
   - 角色步骤返回「更换企业」、未选禁用、键盘操作与焦点转移。
6. **回归**：门户管理员入口、单企业自动进入、退出登录、搜索过滤。

## 7. 风险与回滚

- 风险：`RequestTenantContext` 写入/恢复不当会污染同请求后续查询 → 严格 `try/finally`，端点仅做该次查询。
- 风险：多角色未指定的旧客户端会收到 400 → 属预期（要求显式选择），前端已同步。
- 回滚：移除端点与角色选择 UI，Phase 3 忽略 `member_role_id` 即恢复并集逻辑（后端参数可空，天然向后兼容）。

## 8. 审批

请确认本规格后再进入编码。确认项：
1. 采用「先选企业 → 再选角色」两步式，角色在点击企业后才懒加载。
2. 单角色 / 无角色 / TenantAdmin 跳过角色步骤直接进入；多角色**必须显式选择**（不接受默认全部并集）。
3. 布局采用「≤3 等分铺满 / ≥4 三列换行」；单租户 + 门户管理员全宽即可，无需额外分列占位。
