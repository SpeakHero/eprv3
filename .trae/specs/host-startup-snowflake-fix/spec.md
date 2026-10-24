# Spec：Qlx.Host 启动崩溃修复（雪花 ID 租约时序）+ 门户角色 API 冒烟

> 日期：2026-10-23　|　状态：待审批
> 范围：仅修复启动时序缺陷 + 执行既定冒烟；不改动任何业务 API 契约。

## 1. 问题（已证实）

冒烟阶段后台启动 `src/Qlx.Host`（http profile）即崩溃，退出码 0xE0434352：

```
System.InvalidOperationException: workerId 租约尚未完成初始化，禁止在宿主启动前解析雪花 ID 生成器
  at Qlx.BuildingBlocks.Ids.IdsServiceCollectionExtensions.cs:59          ← ISnowflakeIdGenerator 工厂防护
  at Qlx.BuildingBlocks.Audit.AuditServiceCollectionExtensions.cs:59     ← IInterceptor → AuditSaveChangesInterceptor
  at Qlx.Modules.Portal.PortalServiceCollectionExtensions.cs:72          ← PortalDbContext options 解析 IInterceptor 列表
  at Qlx.Host.Program.cs:101                                             ← 启动迁移 GetRequiredService<IDbContextFactory<PortalDbContext>>
```

根因链：Program.Main 启动期 `MigrateAsync` 创建 PortalDbContext → options 回调解析 `IInterceptor` → `AuditSaveChangesInterceptor`（构造函数直依赖 `ISnowflakeIdGenerator`，见 [AuditSaveChangesInterceptor.cs](file:///e:/ERPv2/eprv3/src/Qlx.BuildingBlocks/Audit/AuditSaveChangesInterceptor.cs#L35-L37)）→ 工厂防护检查 `IWorkerIdLeaseHolder.WorkerId` 为 null（租约由 `WorkerIdLeaseHostedService.StartAsync` 赋值，而 HostedService 要到 `app.RunAsync()` 才启动，晚于迁移）→ 抛异常。

结论：**启动迁移链路必然早于租约初始化，属启动时序设计缺陷**，与本次审计/Migration 改动无关（迁移链路首次真实走通才暴露）。

## 2. 方案（选定 B）

**方案 B（选定）：迁移前预初始化租约 + HostedService 幂等化**

1. [Program.cs](file:///e:/ERPv2/eprv3/src/Qlx.Host/Program.cs#L86-L88)：`var app = builder.Build();` 之后、"启动期数据库迁移"之前，新增租约预初始化段——显式 `AcquireAsync` 并写入 `IWorkerIdLeaseHolder.WorkerId`。保证迁移、Manifest 同步、种子全链路可安全解析 `ISnowflakeIdGenerator`（防护原样保留作安全网）。
2. [WorkerIdLeaseHostedService.cs](file:///e:/ERPv2/eprv3/src/Qlx.BuildingBlocks/Ids/WorkerIdLeaseHostedService.cs#L21-L24)：`StartAsync` 幂等化——`holder.WorkerId` 已非 null 时跳过 `AcquireAsync`（Redis 租约避免重复占用 workerId 名额）；`StopAsync` 行为不变。

**否决方案 A**（拦截器/审计服务惰性解析 ISnowflakeIdGenerator）：`EfCoreAuditService`（IAuditService 单例）与 `AuditSaveChangesInterceptor` 两处构造均直接依赖生成器，且启动期 Manifest 同步 SaveChanges 同样可能触发解析，惰性化只把崩溃点后移，覆盖不全且改动面更大。

## 3. 精确变更清单

| 文件 | 变更 |
|---|---|
| `src/Qlx.Host/Program.cs` | L88 后插入「workerId 租约预初始化」块：`CreateAsyncScope` → 解析 `IWorkerIdLeaseHolder`/`IWorkerIdLease` → `WorkerId is null` 时 `AcquireAsync` 赋值；附注释说明早于 HostedService 的原因；更新文件头「修改时间」 |
| `src/Qlx.BuildingBlocks/Ids/WorkerIdLeaseHostedService.cs` | `StartAsync` 首行加幂等短路 `if (holder.WorkerId is not null) return;`；更新类注释与文件头「修改时间」 |
| `tests/Qlx.BuildingBlocks.Tests/.../WorkerIdLeaseHostedServiceTests.cs` | 新增小单测：① 已初始化时 StartAsync 不调用 AcquireAsync；② 未初始化时正常 Acquire 并赋值（复用既有测试风格与 fake lease） |

编码遵循 `.trae/rules/global-csharp-standard.md`（执行时按其文件头/命名/ConfigureAwait 约定落地）。

## 4. 验证步骤

1. `dotnet build`（0 错误 0 新警告）。
2. `dotnet test`（全量回归，期望 740+ 新增用例全绿）。
3. 后台启动 Host，确认：无未处理异常 → audit/portal 迁移日志 → Manifest 同步日志 → `http://localhost:5002` 就绪探测通过。
4. 冒烟矩阵（PowerShell Invoke-RestMethod，身份级 token）：
   - 登录：`/connect/token`（grant_type=urn:qlx:grant:portal_identity、client_id=erp-web、qlxadmin/Qlx@2026）→ 取 access_token 与字段结构。
   - 角色：GET 列表（记录响应包字段名与 **ID 形态**）、POST 创建 `portal.smoke_test`、PUT 更新、DELETE 清理；重复编码创建 → 409。
   - 防护：对 IsSystem 角色 PUT/DELETE → 409；PUT 未知权限码 → 400/409。
   - 权限：GET `{roleId}/permissions`、GET `permission-options`、PUT `{permissionCodes:[...]}` 后回读一致。
   - 成员双向：GET/PUT `roles/{id}/members`、GET `member-options`、GET/PUT `users/{userId}/roles`、GET `role-options`。
   - 每步记录状态码 + 响应摘要；不建新文件，最终文字汇报（含 ID 混淆边界观察项）。

## 5. 风险与回滚

- 风险：Redis 租约场景预初始化占用一个 workerId——幂等短路确保全程仅获取一次；Static 租约（当前 dev）无影响。
- 回滚：两文件改动均独立小段，revert 即可；无数据/契约影响。
