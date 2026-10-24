# 后端管理 API 接口规范（门户角色 + 权限分配 + 菜单）

> **所属阶段**：阶段二（管理域收口：门户角色/权限/菜单 API）
> **日期**：2026-10-24（初版 2026-10-23）
> **基础**：`docs/权限体系与动态菜单重构设计方案.md` + 代码实际实现
> **状态**：**§1/§2/§3 全部已实现并上线**；§4「角色权限字典 HTTP 端点」**未实现**（改为服务层契约 `IPortalRolePermissionQuery`，见 §4）；§9 列出 4 项已知契约缺口（G-1~G-4）
> **对齐说明（以代码为准，共 6 类偏差）**：① **路由前缀全部更正**为 `/api/portal/admin/...`（原文 `/api/portal/...` 无 `/admin` 段）；② **角色编码改为后端自动生成**（`IEncodingGenerator`，`portal.` 前缀 + 拼音首字母，冲突追加 001/002），`CreatePortalRoleRequest` 已无 `RoleCode` 字段，原「RoleCode 正则 + 必填」校验条款删除；③ **菜单 API 无 `ApiResponse` 封装**，直接返回 `{ navGroups, menus }`（原文 `success/data/timestamp` 错误）；④ **缓存键/失效前缀按实际代码更正**（`_portal:role:`、`_portal:perm:role-permissions`；keyword 查询不走缓存）；⑤ **审计写入口径更正**为 `IAuditService.CaptureOperationAsync` + `PortalConstants.AuditOperations` 常量（`PORTAL_ROLE_CREATE` 等），非特性标注；⑥ **前端对接文件名单更新**（原三页面已删除，统一为 `PortalRoleAdminView.vue` + 适配器）
> **契约校正补充**：响应包统一为实际 `ApiResponse` 契约 `{ success, data?, message?, errors?, timestamp }`；分页字段为 `totalCount/totalPages/isLastPage`；错误码表区分 HTTP 状态码与字符串业务码；§1.1/§2.1/§2.8 响应示例同步补齐 `success/timestamp`；§0.5 已补后端公共层复用/缓存/审计集成章节并对齐全局缓存强制规则 `.trae/rules/global-cache-layering-standard.md`

---

## §0.5 后端公共层复用 + 缓存/审计集成（强制执行）

> **全局铁律**：缓存键必须经 `Qlx.BuildingBlocks.Caching.ICacheService`（Cache-Aside）构建；禁止散装字符串拼 key；禁止业务代码直引 StackExchange.Redis / IDistributedCache / IMemoryCache。详见 `.trae/rules/global-cache-layering-standard.md`（C-x 系列）。

### 后端已落地公共层能力（全部复用，禁止重复实现）

| 能力 | 路径/DI 契约 | 复用方式 | 禁止事项 |
|---|---|---|---|
| 缓存 Cache-Aside | `ICacheService` → `GetOrCreateAsync / RemoveAsync / RemoveByPrefixAsync` | Controller/Service 注入 | ❌ `IMemoryCache.SetAsync(...)` / `Redis.StringSetAsync(...)` |
| 缓存键构建 | `QlxCacheKey.BuildTenantKey(...)` / `QlxCacheKey.BuildPortalKey(...)` | | ❌ `"portal:role:" + code` 散装拼 key |
| 审计日志 | `IAuditService.CaptureOperationAsync(new OperationLogEntry(...))` + `PortalConstants.AuditOperations` 常量 | 写路径末尾显式调用 | ❌ 手写 `dbContext.AuditLogs.Add(...)`；❌ 误以为 `[AuditOperation]` 特性自动拦截（本模块未使用） |
| 异常包装 | 全局 ExceptionHandlerMiddleware → 统一 `ApiResponse { success, code, message, data?, errors?, timestamp }` | throw 业务异常 → 中间件自动转 | ❌ Controller 里 try-catch 手动返回 ApiResponse |
| 参数校验 | `[ApiController]` + `System.ComponentModel.DataAnnotations` | DTO 加 `[Required] / [RegularExpression] / [MaxLength]` | ❌ 手写 `if (string.IsNullOrEmpty(...))` |
| ID 混淆 | `QlxIdJsonMetadata`（全局）+ `HashidsLongModelBinder`（请求体） | 自动处理，Controller 直接声明 `long` | ❌ Controller 里 `Hashids.Decode(...)` / `Encode(...)` |
| 防篡改拦截 | `ManifestDefinitionReadOnlyGuardInterceptor` | 四清单表外部写必须 BypassGuard | ❌ 业务 Controller 直接 `DbSet.Add(...)` 清单表实体 |
| 菜单 API | `PortalMenuController.GET api/portal/menus` | 返回扁平 JSON（后端不做树组装，前端 B-2 消费） | ❌ Controller 里组装树形结构再返回 |

### 本次 API 缓存集成清单（2026-10-24 按实际代码校正）

| 缓存项 | Key / 失效前缀（实际代码） | TTL（含抖动） | 读写模式 | 失效时机 |
|---|---|---|---|---|
| 门户角色列表（无 keyword） | 键 `_portal:role:list`（`QlxCacheKey.BuildPortalKey("role","list")`） | 默认 TTL | `GetOrCreateAsync` | 角色 CRUD / 权限变更 / 成员变更后 `RemoveByPrefixAsync("_portal:role:")` |
| 门户角色列表（带 keyword） | **不缓存**，直接查库（避免 keyword 组合爆炸） | — | 直查 | — |
| 角色权限变更失效前缀 | `_portal:perm:role-permissions` | — | — | `PUT /{roleId}/permissions` 后 `RemoveByPrefixAsync` |
| 某角色权限码（供签发聚合） | 键 `_portal:perm:role:{roleCode}` | 默认 TTL | 写入/删除 | 角色权限替换时 `RemoveAsync` + 前缀清理 |
| 菜单扁平数据 | 菜单端点为**实时查库**，当前未接入缓存（权限体系设计中规划的 `menus:flat` 缓存未落地） | — | — | Manifest 同步后无需失效（无缓存） |

**防击穿**：读路径统一 `GetOrCreateAsync`（含进程内 + Redis 跨实例双道互斥）；**防穿透**：不存在/已废弃写入 `NullMarker`（TTL 30s）；**防雪崩**：TTL 自动 ±10% 抖动（`CacheTtlJitter`）；**一致性**：写路径只删不写（C-5）。

### 审计操作码（实际实现：显式 `CaptureOperationAsync`）

> 实际写法不是 `[AuditOperation]` 特性，而是写路径末尾调用
> `audit.CaptureOperationAsync(new OperationLogEntry(...PortalConstants.AuditOperations.Xxx...))`，
> 落门户库 `plog_operation`（模块码 `QlxAuditConstants.PortalModuleCode`）。

| 端点 | 审计操作码常量 | 实际值 |
|---|---|---|
| POST `/api/portal/admin/roles` | `PortalConstants.AuditOperations.CreatePortalRole` | `PORTAL_ROLE_CREATE` |
| PUT `/api/portal/admin/roles/{id}` | `UpdatePortalRole` | `PORTAL_ROLE_UPDATE` |
| DELETE `/api/portal/admin/roles/{id}` | `DeletePortalRole` | `PORTAL_ROLE_DELETE` |
| PUT `/api/portal/admin/roles/{roleId}/permissions` | `ReplaceRolePermissions` | `PORTAL_ROLE_PERMISSIONS_REPLACE` |
| PUT `/api/portal/admin/roles/{roleId}/members` | 成员替换（`PortalUserRoleAdminService` 内落审计） | 以服务内常量为准 |

---

## 〇、统一约定

### 认证
- 所有端点 `[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]`
- 门户角色/权限/成员/菜单管理端点额外 `[Authorize(Policy = QlxAuthConstants.Policies.PortalAdmin)]`（claim `is_portal_admin=1`）；细粒度权限点（`portal.role.*`）已由 Manifest 同步入库，供自定义角色分配，**当前未在端点级强制**
- 菜单端点 `GET /api/portal/menus` 仅要求有效 Bearer（身份级或租户级均可），不做权限裁剪
- **租户侧菜单端点（`/api/tenant/menus`）尚未实现**（见 §9 缺口 G-2），租户菜单当前复用门户菜单数据 + 前端按模块许可/权限码裁剪

### 返回格式

所有端点统一返回 `Qlx.BuildingBlocks.Api.ApiResponse` / `ApiResponse<T>`，字段为
`success / code / message / data / errors / timestamp`；**null 字段不序列化**（`QlxJsonDefaults.Default` 的
`JsonIgnoreCondition.WhenWritingNull`），因此成功响应通常只出现 `success` + `data`/`message` + `timestamp`。

> ⚠️ **唯一例外**：`GET /api/portal/menus` 直接返回匿名对象 `{ navGroups, menus }`，**未包 `ApiResponse`**
> （无 `success/code/timestamp`）。详见 §1.1 与 §9 缺口 G-1。

```jsonc
// 成功（带数据体）：ApiResponse<T>.Ok(data, message?)
{
  "success": true,
  "data": { ... },
  "message": "可选提示",           // 未传则省略
  "timestamp": "2026-10-23T10:00:00+00:00"
}

// 成功（无数据体）：ApiResponse.Ok(message?) —— DELETE 等写操作
{
  "success": true,
  "message": "角色已删除",
  "timestamp": "2026-10-23T10:00:00+00:00"
}

// 失败：ApiResponse<T>.Fail(code, message, status, errors?)
{
  "success": false,
  "code": "ROLE_CODE_EXISTS",     // 字符串业务码，非 HTTP 状态码
  "message": "角色编码 portal.smoke_test 已存在",
  "timestamp": "2026-10-23T10:00:00+00:00"
}

// 校验失败（HTTP 422，errors 为字段级错误列表）
{
  "success": false,
  "code": "VALIDATION_ERROR",
  "message": "请求数据校验失败",
  "errors": [ { "fieldName": "permissionCodes", "errorMessage": "以下权限码不存在或已废弃：portal.fake.code" } ],
  "timestamp": "2026-10-23T10:00:00+00:00"
}
```

| 字段 | 类型 | 说明 |
|---|---|---|
| `success` | bool | 请求是否成功（判断成功只看此字段） |
| `code` | string? | 字符串业务码；`success=true` 时为 `null` |
| `message` | string? | 可读提示 / 错误描述 |
| `data` | T? | 业务数据体；仅 `ApiResponse<T>` 存在，`success=true` 时有值 |
| `errors` | array? | 字段级错误（`fieldName` + `errorMessage`），仅校验失败时存在 |
| `timestamp` | string | 响应生成时间（UTC ISO-8601） |

### 分页结构与查询参数
```
GET /api/...?page=1&pageSize=20&keyword=xxx
```
默认 pageSize=20，上限 100。分页数据体字段固定为：

```jsonc
{
  "items": [ ... ],
  "totalCount": 12,
  "page": 1,
  "pageSize": 20,
  "totalPages": 1,
  "isLastPage": true
}
```

### ID 混淆
- 所有响应中的 ID 字段均为 **Hashids 混淆字符串**（由全局 `QlxIdJsonMetadata` 自动处理）
- 请求体中的 ID 字符串自动经 `HashidsLongModelBinder` 解码为 long，Controller 直接声明 `long`

### 异常码

异常由 `ExceptionHandlerMiddleware` 按异常类型映射 HTTP 状态码，响应体 `code` 为字符串业务码（两者独立，前端判断以 `success` + `code` 为准）。

| HTTP | code | 含义 | 场景 |
|---|---|---|---|
| 401 | `UNAUTHORIZED` | 未认证 | 无效 / 过期 Token |
| 403 | `FORBIDDEN` | 无权限 | 非 PortalAdmin 访问管理端点 / 缺少 permissions claim |
| 404 | `NOT_FOUND` | 资源不存在 | roleId 不存在 / userId 不存在 |
| 409 | `ROLE_CODE_EXISTS` | 唯一约束冲突 | 新建角色编码重复 |
| 409 | `SYSTEM_ROLE_PROTECTED` | 系统角色保护 | 修改 / 删除 / 改权限 IsSystem 角色 |
| 409 | `ROLE_HAS_MEMBERS` | 存在成员 | 删除仍有成员的角色 |
| 422 | `VALIDATION_ERROR` | 参数 / 业务校验失败（含 `errors` 字段列表） | 权限码不存在或已废弃 / 用户或角色 Id 不存在 / 字段格式非法 |
| 500 | `INTERNAL_ERROR` | 未捕获异常 | 服务端缺陷（生产隐藏详情，开发环境附带消息） |

---

## §1 已实现端点（当前可用）

### 1.1 门户菜单 API（扁平 JSON，前端组装树）

| 方法 | 路径 | 鉴权 | Controller |
|---|---|---|---|
| GET | `/api/portal/menus` | Bearer（身份级或租户级均可） | `PortalMenuController` |

**Response（实际：无 ApiResponse 包装，HTTP 200 直接返回）**：
```jsonc
{
  "navGroups": [
    {
      "id": 123456789012345678,     // 注意：此处为真实 long（未混淆），前端以 string 承载
      "groupCode": "workbench",
      "groupName": "工作台",
      "moduleCodes": "portal,bas",  // 逗号分隔
      "sortOrder": 0
    }
  ],
  "menus": [
    {
      "id": 987654321098765432,     // 同上，真实 long
      "moduleCode": "bas",
      "menuCode": "supplier.list",
      "parentCode": null,
      "groupName": "往来单位",
      "displayName": "供应商档案",
      "route": "/bas/supplier/list",
      "icon": "Shop",
      "requiredPermissionCode": null, // null 表示仅模块许可即可见
      "sortOrder": 10,
      "openMode": 1
    }
  ]
}
```

**过滤规则**：
- 导航分组：`IsDeprecated=false`，按 `SortOrder` 排序
- 菜单：`IsVisible=true AND IsDeprecated=false`，按 `ModuleCode → ParentCode → SortOrder` 排序
- **模块许可裁剪 / 权限码裁剪在后端均未实现**，当前设计为前端 menuStore 消费后裁剪

> **实现状态**：前端 `stores/menu.ts` + `utils/menuTree.ts` 已落地树组装与裁剪纯函数，
> 但 `api/menus.ts` 仍按 `{ data, code, message }` 解包（与后端实际返回不一致），且 menuStore
> **尚未接入布局消费**——见 §9 缺口 G-1。

### 1.2 Token 签发（PortalAuthenticationService 已改造）

| Token 类型 | 新增 claim | 写入条件 |
|---|---|---|
| 租户上下文 Token | `member_role` | 永远写入（值 TenantAdmin / OrdinaryMember） |
| 租户上下文 Token | `permissions`（逗号分隔） | member_role != TenantAdmin 时写入，TenantAdmin 零计算旁路不写 |
| 租户上下文 Token | `qlx_data_scope` | 预留，Bas Query 扩展后填充 |
| 身份 Token | `is_portal_admin` | 门户管理员签发值 "1" |
| 身份 Token | `permissions` | 非门户管理员经 PortalRolePermissionQuery 聚合后写入 |

---

## §2 已实现：门户角色管理（`por_roles` / `por_role_permissions` / `por_user_roles`）

> 全部端点需 `PortalAdmin` 策略。路由前缀实际为 **`/api/portal/admin/roles`**。

| # | 方法 | 路径 | 说明 | Controller |
|---|---|---|---|---|
| 2.1 | GET | `/api/portal/admin/roles` | 分页列表（keyword 匹配 RoleCode/RoleName；**key 为空走缓存，非空直查**） | `PortalRoleController` |
| 2.2 | POST | `/api/portal/admin/roles` | 新建角色（**编码后端自动生成**，IsSystem 强制 false） | 同上 |
| 2.3 | PUT | `/api/portal/admin/roles/{id}` | 修改角色（IsSystem=true → 409 `SYSTEM_ROLE_PROTECTED`） | 同上 |
| 2.4 | DELETE | `/api/portal/admin/roles/{id}` | 删除角色（IsSystem / 有成员 → 409） | 同上 |
| 2.5 | GET | `/api/portal/admin/roles/{roleId}/permissions` | 查询角色已有权限码（`string[]`，不存在 → 404） | `PortalRolePermissionController` |
| 2.6 | GET | `/api/portal/admin/roles/{roleId}/permission-options` | 可选权限码清单（`IsDeprecated=false`） | 同上 |
| 2.7 | PUT | `/api/portal/admin/roles/{roleId}/permissions` | 全量替换权限（事务；IsSystem → 409） | 同上 |
| 2.8 | GET | `/api/portal/admin/roles/{roleId}/members` | 角色成员列表（userId/loginAccount/displayName） | `PortalUserRoleController` |
| 2.9 | GET | `/api/portal/admin/roles/{roleId}/member-options` | 可选用户清单（启用中的 PortalUser） | 同上 |
| 2.10 | PUT | `/api/portal/admin/roles/{roleId}/members` | 全量替换成员（事务） | 同上 |

> 说明：2.5 的路径参数名为 `{roleId}`（Controller 路由模板），2.3/2.4 为 `{id}`，均声明为 `long` 并由全局 ModelBinder 自动解码混淆串。

### §2.0 各端点缓存失效时机（严格 C-5 写路径只删不写）

| 端点 | 实际删除的缓存 | 方式 |
|---|---|---|
| POST /roles | `_portal:role:` 前缀 | `RemoveByPrefixAsync` |
| PUT /roles/{id} | `_portal:role:` 前缀 | `RemoveByPrefixAsync` |
| DELETE /roles/{id} | `_portal:role:` 前缀 | `RemoveByPrefixAsync` |
| PUT /roles/{id}/permissions | `_portal:perm:role:{roleCode}` + `_portal:perm:role-permissions` 前缀 + `_portal:role:` 前缀（列表内嵌 permissionCount） | `RemoveAsync` + 两次 `RemoveByPrefixAsync` |
| PUT /roles/{id}/members | `_portal:role:` 前缀（列表内嵌 memberCount） | `RemoveByPrefixAsync` |

**原则**：提交事务成功 → 立即删缓存 → 后续读请求经 `GetOrCreateAsync` 重建；删缓存失败不回滚事务（TTL 兜底）。

### 2.1 GET 列表 Response 结构
```jsonc
{
  "success": true,
  "data": {
    "items": [
      {
        "roleId": "9x",              // Hashids 混淆串
        "roleCode": "portal.gly",    // 后端生成：portal. + 名称拼音首字母（冲突追加 -001）
        "roleName": "管理员",
        "description": "全量权限",
        "isSystem": true,
        "permissionCount": 33,
        "memberCount": 2,
        "updatedAt": "2026-10-23T10:00:00Z"
      }
    ],
    "totalCount": 12,
    "totalPages": 1,
    "isLastPage": true
  },
  "timestamp": "2026-10-23T10:00:00+00:00"
}
```
> 分页体实际字段为 `items / totalCount / totalPages / isLastPage`（`PagedResult<T>` 契约，**无 page/pageSize 回显**）。

**保护规则**：IsSystem 角色前端编辑/删除按钮置灰；后端 PUT/DELETE 强制 409。

### 2.2 POST / PUT Request（角色编码不再由前端传入）

```jsonc
// POST /api/portal/admin/roles
{
  "roleName": "门户审计员",           // 必填，1-64
  "description": "只读查看审计日志"    // 选填，≤256
}

// PUT /api/portal/admin/roles/{id}（RoleCode 不可修改，入参同 POST）
{
  "roleName": "门户审计员",
  "description": "只读查看审计日志"
}
```

**编码生成规则（N-x）**：`IEncodingGenerator.GenerateAsync(RoleName, scope:"portal-role", Prefix:"portal.", PinyinMode:Initials, SequenceDigits:3, MaxLength:64)`
——例「管理员」→ `portal.gly`，冲突自动追加 `-001/-002`。

**校验**：
- RoleName 必填 1-64（DataAnnotations `[Required]/[MinLength]/[MaxLength]`）
- Description ≤256
- Create 强制 `IsSystem = false`；PUT 不改 RoleCode

### 2.7 PUT 设置权限 Request
```jsonc
{
  "permissionCodes": ["portal.audit.login", "portal.audit.operation"]  // 至少 1 项
}
```

**校验**：
- 所有 code 必须存在于 `por_permission_definitions` 且 `IsDeprecated=false`，否则 422 `VALIDATION_ERROR`（`errors[0].fieldName = "PermissionCodes"`）
- IsSystem 角色 → 409 `SYSTEM_ROLE_PROTECTED`

**实现**：事务内 `ExecuteDeleteAsync` → `AddRangeAsync`；去重后写入。

### 2.10 PUT 设置成员 Request
```jsonc
{
  "userIds": ["abc", "def"]   // 顶层 long 数组，由全局 QlxIdJsonMetadata 自动编解码混淆串
}
```

**校验**：
- 所有 user 必须存在于 `portal_users`
- IsSystem 角色允许调整成员（不做拦截）

### 2.8 GET 角色成员 Response
```jsonc
{
  "success": true,
  "data": [
    { "userId": "abc", "loginAccount": "admin", "displayName": "张三" }
  ],
  "timestamp": "2026-10-23T10:00:00+00:00"
}
```

---

## §3 已实现：门户用户-角色双向分配

> 实际路由前缀 **`/api/portal/admin/users/{userId}/...`**，与 §2.8~2.10 反向对称。

| # | 方法 | 路径 | 说明 |
|---|---|---|---|
| 3.1 | GET | `/api/portal/admin/users/{userId}/roles` | 查询某用户已分配的门户角色 |
| 3.2 | GET | `/api/portal/admin/users/{userId}/role-options` | 可选角色清单 |
| 3.3 | PUT | `/api/portal/admin/users/{userId}/roles` | 批量设置某用户的角色 |

**Request（3.3）**：`{ "roleIds": ["abc", "def"] }`（`ReplaceUserRolesRequest`，顶层 long 数组自动混淆）。

**实现**：`PortalUserRoleController` 同时承载角色侧与用户侧两种路由，由 `PortalUserRoleAdminService` 统一处理。

---

## §4 角色权限字典：**服务层契约（无 HTTP 端点）**

> **状态变更**：早期设想的 `GET /api/portal/role-permissions` **未实现**，已由服务层契约取代。

| 契约 | 实现 | 用途 |
|---|---|---|
| `IPortalRolePermissionQuery`（`Qlx.Abstractions.Queries`） | `PortalRolePermissionQuery`（`por_user_roles ⋈ por_role_permissions` 聚合） | `PortalAuthenticationService` 签发**身份 Token** 时聚合非门户管理员的 `permissions` claim |

**降级路径**：签发时若 `IPortalRolePermissionQuery` 未注册，按 `PortalAuthenticationService` 内日志跳过权限码聚合（不调用任何 HTTP API），并记录 `portal_user_id`。因此**不存在**「HTTP 兜底拉全量字典」这一调用场景。

---

## §5 后端代码实现清单（全部已落地，2026-10-24 核验）

| # | 文件 | 状态 | 说明 |
|---|---|---|---|
| 1 | `src/Qlx.Modules.Portal/Controllers/PortalRoleController.cs` | ✅ 已实现 | 角色 CRUD（§2.1-2.4），`/api/portal/admin/roles` |
| 2 | `src/Qlx.Modules.Portal/Controllers/PortalRolePermissionController.cs` | ✅ 已实现 | 角色权限分配（§2.5-2.7），路由 `roles/{roleId}/permissions` |
| 3 | `src/Qlx.Modules.Portal/Controllers/PortalUserRoleController.cs` | ✅ 已实现 | 角色侧成员 + 用户侧角色双向端点（§2.8-2.10 + §3.1-3.3） |
| 4 | `src/Qlx.Modules.Portal/Services/PortalRoleAdminService.cs` | ✅ 已实现 | CRUD + `IEncodingGenerator` 编码生成 + IsSystem 防护 + 列表缓存 |
| 5 | `src/Qlx.Modules.Portal/Services/PortalRolePermissionAdminService.cs` | ✅ 已实现 | 权限全量替换（事务）+ 三处缓存失效 + 审计 |
| 6 | `src/Qlx.Modules.Portal/Services/PortalUserRoleAdminService.cs` | ✅ 已实现 | 双向成员分配 + 成员列表查询 |
| 7 | `src/Qlx.Modules.Portal/PortalServiceCollectionExtensions.cs` | ✅ 已注册 | 三个 Service Scoped 注册（另含 `IPortalRolePermissionQuery`） |
| 8 | `src/Qlx.Modules.Portal/Controllers/PortalMenuController.cs` | ✅ 已实现 | 扁平菜单下发（实时查库，未包 ApiResponse） |
| 9 | `src/Qlx.Modules.Portal/Services/PortalManifestSyncService.cs` | ✅ 已实现 | 启动期 Manifest（权限点/菜单/导航分组）物化入库 |

### Service 层实际方法签名（节选）
```csharp
// IPortalRoleAdminService
Task<PagedResult<PortalRoleListItem>> ListAsync(string? keyword, int page, int pageSize, CancellationToken ct);
Task<PortalRoleDetail> CreateAsync(CreatePortalRoleRequest req, CancellationToken ct);   // 编码自动生成
Task<PortalRoleDetail> UpdateAsync(long roleId, UpdatePortalRoleRequest req, CancellationToken ct);
Task DeleteAsync(long roleId, CancellationToken ct);                                     // IsSystem 或 有成员 → 409

// IPortalRolePermissionAdminService
Task<IReadOnlyList<string>> GetPermissionCodesAsync(long roleId, CancellationToken ct);  // 原 PermissionSet 简化为 string[]
Task<IReadOnlyList<PermissionOption>> GetPermissionOptionsAsync(CancellationToken ct);
Task ReplaceAsync(long roleId, ReplaceRolePermissionsRequest req, CancellationToken ct);

// IPortalUserRoleAdminService（角色侧 + 用户侧）
Task<IReadOnlyList<UserRoleMember>> ListMembersAsync(long roleId, CancellationToken ct);
Task ReplaceMembersAsync(long roleId, ReplaceRoleMembersRequest req, CancellationToken ct);
Task<IReadOnlyList<RoleMemberItem>> ListUserRolesAsync(long userId, CancellationToken ct);
Task ReplaceRolesForUserAsync(long userId, ReplaceUserRolesRequest req, CancellationToken ct);
```

### 参数校验方案（实际 DTO）
```csharp
public sealed record CreatePortalRoleRequest(
    [Required(ErrorMessage = "角色名称必填")]
    [MinLength(1), MaxLength(64)] string RoleName,
    [MaxLength(256)] string? Description);          // 无 RoleCode 字段（N-1）

public sealed record ReplaceRolePermissionsRequest(
    [Required, MinLength(1)] IReadOnlyList<string> PermissionCodes);
```

### 审计日志（实际口径）
写方法内部显式调用 `audit.CaptureOperationAsync(...)`，操作码取 `PortalConstants.AuditOperations`（`PORTAL_ROLE_CREATE` / `PORTAL_ROLE_UPDATE` / `PORTAL_ROLE_DELETE` / `PORTAL_ROLE_PERMISSIONS_REPLACE`），落门户库 `plog_operation`；**不使用** `[AuditOperation]` 特性（该特性存在但本域未采用）。

---

## §6 与前端 API 对接清单（2026-10-24 按实际文件校正）

| 前端调用 | 后端端点 | 前端文件 |
|---|---|---|
| 菜单加载（已实现但**未接线**） | `GET /api/portal/menus` | `api/menus.ts` + `stores/menu.ts`（`fetchMenus` 尚未被布局消费） |
| 门户角色列表 / CRUD | `/api/portal/admin/roles` | `views/portal/PortalRoleAdminView.vue`（左列表 + 右三 Tab） |
| 角色权限分配 | `/api/portal/admin/roles/{id}/permissions` | `components/role-admin/RolePermissionPanel.vue` → `api/adapters/roleAdmin.ts` |
| 可选权限码 | `/api/portal/admin/roles/{id}/permission-options` | 同上 |
| 角色成员分配 | `/api/portal/admin/roles/{id}/members` | `components/role-admin/RoleMemberPanel.vue` → `api/adapters/roleAdmin.ts` |
| 用户侧角色分配 | `/api/portal/admin/users/{id}/roles` | `api/adapters/roleAdmin.ts`（反向路由） |

> **已删除页面**（原文档提及，代码中已不存在）：`PortalRoleListView.vue`、`PortalRolePermissionView.vue`、`PortalUserRoleView.vue`。
> 三者已合并为 `PortalRoleAdminView.vue`；旧路由 `/portal/roles/:id/permissions`、`/portal/roles/:id/members` 保留 **redirect** 到 `/portal/roles`（见 `router/index.ts`）。

---

## §7 验收标准（后端，按实际实现更新）

| # | 标准 | 验证方法 | 状态 |
|---|---|---|---|
| 1 | 全端点 Build 0 错误 0 警告 | 停止 Host 进程后 `dotnet build Qlx.Erpv3.slnx` | ✅ 2026-10-24 |
| 2 | 参数校验全部生效（必填/长度/权限码合法） | 422 用例覆盖 | ✅ |
| 3 | IsSystem 角色不可修改/删除/改权限 | 用内置 `portal.*` 系统角色测试 → 409 | ✅ |
| 4 | 删除有成员角色返回 409 `ROLE_HAS_MEMBERS` | 先分配成员再删 | ✅ |
| 5 | 权限/成员批量替换事务原子性 | 中途断库 → 回滚 | ✅ |
| 6 | 角色编码自动生成且唯一 | 新建同名角色两次 → `portal.xxx` / `portal.xxx-001` | ✅ |
| 7 | 菜单 API 扁平下发无树结构 | 响应体无 children 字段 | ✅ |
| 8 | 审计日志写入 | `plog_operation` 查询操作码 | ✅ |

---

## §8 Bas 租户自管理域 API（组织 / 岗位 / 成员 / 角色，已实现 2026-10-23）

路由前缀 `/api/tenant`，全部端点需租户级 Token。业务 Id 在路由/查询/JSON 属性中由 `HashidsLongModelBinder` + `QlxIdJsonMetadata` 自动混淆/解码；**顶层 long 数组**（`roles/{id}/members` 入参与回包）无属性元数据，由 Service 层统一编解码（GET/PUT 对称混淆串）。响应全部为扁平原始字段（树组装/显示格式化在前端，B-2）。

### §8.1 端点总表（`TenantAdminController`）

| 方法 | 路径 | 说明 | 审计操作码 |
|---|---|---|---|
| GET | `/api/tenant/organizations/tree` | 组织扁平列表（含 `memberCount` 直属成员计数，跨库一次成员查询内存分组，禁 N+1） | — |
| POST | `/api/tenant/organizations` | 新建组织 | — |
| PUT | `/api/tenant/organizations/{id}` | 更新组织（根公司/自身父节点防护） | — |
| POST | `/api/tenant/organizations/{id}/toggle` | 组织启停（根公司不可停用，409 `ORG_ROOT_PROTECTED`） | — |
| GET | `/api/tenant/positions` | 岗位分页（orgId/keyword） | — |
| POST | `/api/tenant/positions` | 岗位新建/保存（**编码后端自动生成** `pos-` + 名称拼音首字母，冲突追加 `-001`；入参 `{ name, orgId, description?, sortOrder }` 无编码字段） | — |
| POST | `/api/tenant/positions/{id}/toggle` | **岗位启停**（Enabled↔Disabled，有在职成员不阻断） | `BAS_POSITION_TOGGLE` |
| GET | `/api/tenant/members` | 成员分页（orgId/memberRole/memberStatus/keyword + roleCodes 填充） | — |
| POST | `/api/tenant/members/invite` | 邀请成员（账号格式校验；已是成员 409 `MEMBER_ALREADY_EXISTS`） | — |
| GET | `/api/tenant/members/{id}` | 成员详情（越租户一律 404） | — |
| POST | `/api/tenant/members/{id}/toggle` | 成员启停（TokenVersion++） | — |
| POST | `/api/tenant/members/{id}/reset` | 重置成员密码 | — |
| DELETE | `/api/tenant/members/{id}` | 移除成员（先清 member_roles 再删 TenantMember） | — |
| POST | `/api/tenant/members/{id}/resend-invite` | **重发邀请**（仅 PendingInvite，否则 409 `MEMBER_NOT_PENDING_INVITE`；经 `IMessageService.PushAsync` 站内直推，收件人为混淆门户用户 Id） | `BAS_MEMBER_RESEND_INVITE` |
| POST | `/api/tenant/members/import` | **批量导入**（入参 `ImportMemberRow[]` 解析后行数组；行级隔离，逐行独立提交；返回 `{ invited, failed: [{ line, reason }] }`） | `BAS_MEMBER_IMPORT` |
| GET | `/api/tenant/members/{id}/login-logs` | **成员登录日志**（租户库 `log_login` 按 LoginAccount 过滤，雪花 EventId 倒序；页大小上限强制收敛至 20；越租户 404） | — |
| GET | `/api/tenant/roles` | 角色列表（含 `memberCount`/`permissionCount`，相关标量子查询单往返） | — |
| POST | `/api/tenant/roles` | 新建角色（**编码后端自动生成** `role-` + 名称拼音首字母，冲突追加 `-001`；入参 `SaveRolePayload { Name, Description?, DataScope }` 无编码字段） | — |
| PUT | `/api/tenant/roles/{id}` | 更新角色（内置角色 403） | — |
| DELETE | `/api/tenant/roles/{id}` | 删除角色（内置角色 403；级联清 role_permissions/member_roles） | — |
| GET | `/api/tenant/roles/{id}/permissions` | **角色权限点回显**（404 守卫） | — |
| PUT | `/api/tenant/roles/{id}/permissions` | 角色权限批量替换（内置角色 403） | — |
| GET | `/api/tenant/roles/{id}/members` | **角色成员 Id 回显**（混淆串数组，与 PUT 入参对称；404 守卫） | — |
| PUT | `/api/tenant/roles/{id}/members` | 角色成员批量替换（Service 解码混淆串） | — |
| GET | `/api/tenant/permissions` | 全部模块权限点分组（前端功能权限树数据源，Portal 库只读投影） | — |
| GET | `/api/tenant/modules` | 本租户已授权模块卡片 | — |

### §8.2 关键响应字段（新增/变更）

```csharp
// 组织扁平节点（B-4）
public sealed record OrganizationFlatRow(
    long Id, long? ParentId, string Name, string Type, long? ManagerMemberId,
    string Status, int SortOrder, bool IsBuiltin, DateTimeOffset CreatedAt,
    int MemberCount);                       // 新增：直属成员聚合计数（原始字段）

// 角色行（B-3）
public sealed record RoleRow(
    long Id, string Code, string Name, string? Description, bool IsBuiltin,
    int DataScope,
    int MemberCount, int PermissionCount,   // 新增：聚合计数（原始字段）
    DateTimeOffset UpdatedAt);

// 成员登录日志行（B-8，来源租户库 log_login）
public sealed record MemberLoginLogRow(
    long EventId, DateTimeOffset LoginAt, string? IpAddress,
    bool IsSuccess, string? FailureReason);

// 批量导入（B-7）
public sealed record ImportMemberRow(int Line, string DisplayName, string Account, string OrgName, string RoleName);
public sealed record ImportMembersResult(int Invited, IReadOnlyList<(int Line, string Reason)> Failed);
```

### §8.3 权限点（Manifest 声明，启动期自动同步进 `por_permission_definitions`）

| 模块 | 权限点 | 种类 |
|---|---|---|
| Portal（角色管理域，B-10） | `portal.role.menu` / `portal.role.action` / `portal.role.create` / `portal.role.update` / `portal.role.delete` / `portal.role.assign` | Menu / Action×5 |
| Bas（租户自管理域，B-10） | `bas.admin.org.menu` / `bas.admin.org.action` / `bas.admin.member.menu` / `bas.admin.member.action` / `bas.admin.role.menu` / `bas.admin.role.action` | Menu / Action 各 3 |

> qlxadmin 走 IsPortalAdmin 零计算旁路、租户 TenantAdmin 零计算旁路，均不依赖角色授权行；权限点供自定义角色分配。
> 登录审计分流：租户登录成功 → 租户库 `log_login`；门户登录/失败 → 门户库 `plog_operation`。
> 权限点入库路径：`PortalManifestSyncService.SyncAllAsync` 在启动期（迁移之后）把各模块 Manifest 的 `Permissions` / `Menus` / `TopNavigationGroups` 物化到 `por_permission_definitions` / `por_module_menus` / `por_navigation_groups`，写入时 `BypassGuard` 放行；**门户角色授权行不在同步范围**（管理员手工配置）。

---

## §9 已知契约缺口（2026-10-24 核查发现，待修复）

| # | 缺口 | 证据 | 影响 | 处置建议 |
|---|---|---|---|---|
| G-1 | **菜单前后端响应契约不一致**：后端 `PortalMenuController` 返回裸 `{ navGroups, menus }`，前端 `api/menus.ts` 按 `{ data, code, message }` 解包并判断 `code !== 0` | `PortalMenuController.cs:78` vs `src/Qlx.Web/src/api/menus.ts:28-35` | 一旦接线必然抛「菜单加载失败」；当前因 menuStore 未被布局消费而未暴露 | 二选一：①后端包 `ApiResponse`；②前端改为裸解包。同时 menuStore 接入布局 |
| G-2 | **租户侧菜单端点缺失**：`/api/tenant/menus` 不存在，Bas Manifest `Menus = []` | `src/Qlx.Modules.Bas/BasModule.cs:42`；Portal 侧仅有 `api/portal/menus` | 租户端动态菜单无法按租户模块许可/权限裁剪下发 | 收口批 R-3 实现 |
| G-3 | 菜单 API 返回真实 `long` Id（未经 Hashids 混淆），与「对外 ID 一律混淆」约束不符 | `PortalMenuController.cs:49,65`（匿名对象，无 `QlxIdJsonMetadata` 属性元数据） | ID 可枚举（低敏，仅清单表 Id） | 计划 R-3：改用具名 DTO 以触发全局混淆 |
| G-4 | 菜单数据未接入缓存（权限体系设计中规划的 `menus:flat` 缓存未落地） | `PortalMenuController` 每次实时查库 | 高频刷新时数据库压力 | 计划 R-3：接 `ICacheService.GetOrCreateAsync` + TTL 抖动 + Manifest 同步后失效 |

> 说明：G-1~G-4 均不改变接口「已实现」事实，故 §1/§2/§3 仍标记已实现；四项已全部纳入收口批 R-3（菜单链路收口）。

