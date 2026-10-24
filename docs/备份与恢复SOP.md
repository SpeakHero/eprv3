# Qlx ERPv3 备份与恢复 SOP

> **版本**：v1.0
> **更新日期**：2026-10-22
> **范围**：四库备份策略、恢复流程、租户共享库影响面、升级窗口 SOP
> **依据**：`docs/部署配置清单.md` §5/§6、`docs/saas-architecture-spec.md` §3.8 四库边界

---

## 一、四库备份策略

### 1.1 备份频率与保留

| 库 | 全量备份 | 差异备份 | 日志备份 | 保留期限 | 备份账号 | 影响范围 |
| --- | --- | --- | --- | --- | --- | --- |
| `qlx_portal` | 每日 02:00 | 每 6 小时 | 每 15 分钟 | 30 天 | `qlx_backup` | 门户路由、租户定义、模块许可、审计日志 |
| `qlx_shared` | 每日 03:00 | 每 4 小时 | 每 15 分钟 | 30 天 | `qlx_backup` | 全局数据（公告/任务/字典）+ Hangfire 队列表 — **影响所有租户** |
| `qlx_tenant_shared` | 每周六 01:00 | 每日 01:00 | 每 30 分钟 | 30 天 | `qlx_backup` | N 租户共用业务库 — **影响所有 Shared 模式租户** |
| `qlx_tenant_*`（独立租户） | 每日 04:00 | 每 6 小时 | 每 15 分钟 | 14 天 | `qlx_backup` | 单租户独占 — **仅影响该租户** |

### 1.2 备份脚本模板

```sql
-- 全量备份
BACKUP DATABASE [qlx_portal] TO DISK = N'\\backup-share\qlx_portal\full_$(date).bak'
WITH COMPRESSION, INIT, STATS = 10, CHECKSUM;

-- 差异备份
BACKUP DATABASE [qlx_portal] TO DISK = N'\\backup-share\qlx_portal\diff_$(date)_$(hour).bak'
WITH DIFFERENTIAL, COMPRESSION, STATS = 10, CHECKSUM;

-- 日志备份
BACKUP LOG [qlx_portal] TO DISK = N'\\backup-share\qlx_portal\log_$(date)_$(hour)_$(minute).trn'
WITH COMPRESSION, STATS = 10, CHECKSUM;
```

> `qlx_tenant_shared` 和独立租户库使用相同模板，但恢复前必须先停掉受影响租户的宿主进程（见 §2）。

---

## 二、恢复流程

### 2.1 恢复影响面评估（必做）

| 恢复对象 | 影响租户 | 停机范围 | 前置条件 |
| --- | --- | --- | --- |
| `qlx_portal` | **全部** | 门户宿主 + 所有租户宿主暂停 | 所有宿主切换到只读模式（`Qlx:Tenancy:DevelopmentFallback.Enabled=false`，但临时熔断写操作） |
| `qlx_shared` | **全部** | 门户宿主暂停全局项写入；租户侧切换本地缓存降级 | 全局公告/任务队列暂停消费 |
| `qlx_tenant_shared` | **全部 Shared 模式租户** | 所有 Shared 租户宿主暂停写 | 独立租户不受影响 |
| `qlx_tenant_*`（独立租户） | **仅该租户** | 单租户宿主暂停写 | 其他租户完全不受影响 |

### 2.2 恢复步骤模板（以 `qlx_tenant_shared` 为例）

```
┌─────────────────────────────────────────────────────────────────┐
│  恢复前准备（15 分钟）                                            │
│  ① 确认最新可用备份（全量 + 差异 + 日志链连续）                    │
│  ② 在门户控制台标记所有 Shared 租户为 Maintenance                 │
│  ③ 停掉所有 Shared 租户宿主进程（或熔断网关流量）                  │
│  ④ 检查 Hangfire 队列中是否有正在执行的 Shared 任务 → 标记失败       │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  数据库恢复（30~60 分钟，视库大小）                                │
│  ① RESTORE DATABASE qlx_tenant_shared WITH REPLACE                │
│  ② 还原差异 + 日志链（指定 STOPAT 时间点如有需要）                  │
│  ③ 恢复后验证 RLS 策略存在：SELECT * FROM sys.security_policies    │
│  ④ 验证 TenantId 非空约束 + 唯一索引                                │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  应用层恢复（10 分钟）                                            │
│  ① 启动租户宿主（Shared 模式）                                     │
│  ② 执行 `/health` 确认所有探针 Green                               │
│  ③ 租户侧登录冒烟 + 业务读写验证                                   │
│  ④ 在门户控制台解除 Maintenance 标记                                │
│  ⑤ 触发巡检服务（TenantIsolationAuditor）重新校验 RLS               │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  恢复后验证（持续 24 小时）                                        │
│  ① 监控 §监控告警配置.md 中所有指标无异常                          │
│  ② 审计日志落库正常（两侧不复制）                                   │
│  ③ Hangfire 积压任务处理完毕                                       │
│  ④ 记录恢复过程到 plog_operation（门户库）                         │
└─────────────────────────────────────────────────────────────────┘
```

### 2.3 关键注意事项

1. **`qlx_tenant_shared` 恢复前必须停掉所有 Shared 模式租户的宿主** — 回滚期间不可接受新写入，否则备份/恢复不一致；
2. **独立租户库可按需单独恢复** — 不影响其他租户，恢复后验证 TenantId 行全部存在 + RLS 策略未丢失 + 升级断点（如在迁移中）；
3. **门户库恢复后 `por_tenant_databases` 路由行可能指向已被重命名的独立库** — 恢复完成后需逐一校验路由行指向的物理库是否存在，不存在则标记该租户 `ProvisioningFailed`；
4. **Hangfire `hangfire_*` 表不可与 `qlx_shared` 独立备份** — 恢复时必须一起恢复，否则 Job 元数据与队列数据不一致；
5. **恢复演练必须至少每季度执行一次** — 覆盖四库全部场景，验证恢复时间 RTO ≤ 约定值（门户 30min / 租户共享 60min / 独立租户 15min）。

---

## 三、升级窗口 SOP（共享→独立）

升级是**异步流水线**（B5 已落地），大部分步骤应用层自动执行。运维只需确认关键节点。

### 3.1 阶段概览

```
Preparing ──► Copying ──► Verifying ──► Cutover ──► Done ──► [30天保留] ──► Cleaned
 │              │           │          │           │                          │
 │              │           │          │           │                          └── 自动清理共享库残留数据
 │              │           │          │           └── 路由切换完成，租户无感
 │              │           │          └── 约 5 秒短暂停机
 │              │           └── 行数 + 抽样哈希比对（幂等）
 │              └── 键集分页批量复制（业务正常读写）
 └── 独立库创建 + 模块迁移 + RLS 策略部署
```

### 3.2 运维操作清单

| 阶段 | 动作 | 负责方 | 工具/API | 超时 |
| --- | --- | --- | --- | --- |
| **发起** | 在门户详情页点击「升级为独立库」 | 门户管理员 | `POST /api/portal/admin/tenants/{id}/promote-to-isolated` | — |
| **预检查** | 确认独立库已创建 + 模块迁移成功 + RLS 部署 | 应用层自动 | sqlcmd 查询 `__EFMigrationsHistory_*` + `sys.security_policies` | 5 min |
| **复制** | `TenantDataCopier` 键集分页批量复制 | 应用层自动 | 查询 `por_tenant_upgrade_checkpoints` 看 LastKey 递增 | 业务量相关，设 10 min 告警 |
| **校验** | `TenantDataVerifier` 行数 + 抽样哈希 | 应用层自动 | 必须全部一致才进入 Cutover | 3 min |
| **切换** | 路由改写 + resolver 刷新 | 应用层自动 | `Tenant.ChangeDatabaseMode(Isolated)` + `RefreshAsync` | 5 秒停机窗口 |
| **完成** | 标记 Done + 审计落库 | 应用层自动 | 前端 3 秒轮询自动感知 | — |
| **清理** | 30 天后自动 DELETE Top（非 DROP） | `TenantSharedDataCleanService` | `por_tenant_upgrade_jobs.Status=Cleaned` | — |

### 3.3 失败回滚场景

| 失败阶段 | 自动处理 | 运维介入 |
| --- | --- | --- |
| Preparing / Copying / Verifying | 任务标记 Failed，独立库保留，路由仍指共享库 | 手动重试（断点续跑）：`POST /api/portal/admin/tenants/{id}/upgrade-job/retry` |
| Cutover（极罕见） | 任务标记 Failed，路由保持 Shared（事务内操作已回滚） | 手动重试同上；若 3 次仍失败 → 手动比对数据一致性 |
| 3 次重试仍失败 | — | 运维手动比对共享库/独立库 + 手动改回路由（不自动降级，无降级接口） |

### 3.4 紧急回滚（已 Cutover 后发现问题）

> **注意**：设计铁律是「无降级入口」——升级完成后**不可**自动从独立库回退共享库。如发现严重问题：

1. 立即标记该租户 Maintenance；
2. 从升级前备份恢复原独立库（或从共享库恢复到新独立库）；
3. 手动改 `por_tenant_databases` 路由行：`DatabaseMode=Shared`，凭据置空串；
4. 调用 `ITenantDatabaseResolver.RefreshAsync` 强制刷新路由缓存；
5. 解除 Maintenance 标记。

---

## 四、数据库迁移 SOP（模块升级）

当有新模块发布或 Schema 变更时执行。

### 4.1 门禁

- `dotnet build` 0 警告 0 错误；
- 架构测试全绿；
- 新迁移脚本在测试库验证通过（门户 + 共享 + 租户共享 + 独立租户）；
- 迁移脚本是幂等的（EF Core 自动保证）。

### 4.2 执行

```
① 在低峰期（推荐 02:00-04:00）发布宿主新版本
② 宿主启动时自动执行迁移：
   - 门户库：`OpenIddict + por_* + plog_*`（门户宿主）
   - 共享库：`glb_* + hangfire_*`（门户宿主 sp_getapplock 互斥）
   - 租户共享库：业务模块表（TenantMigrationRunner 自动）
   - 独立租户库：业务模块表（TenantMigrationRunner 自动）
③ 验证每个库的 `__EFMigrationsHistory_{Module}` 表有新行
④ 执行 `/health` 确认 Green
⑤ 门户库 + 共享库 + 租户共享库各做一次备份（迁移后基线）
```

### 4.3 回滚

EF Core 迁移**无自动 Down 脚本**（未手动编写 `Down()`）。如迁移失败导致库结构异常：

1. 停止所有宿主；
2. 从迁移前备份恢复受影响的库；
3. 修复代码/迁移脚本；
4. 重新发布宿主。

---

## 五、定期检查清单

| 频率 | 检查项 | 通过标准 | 告警阈值 |
| --- | --- | --- | --- |
| **每日** | 备份任务完成状态 | 四库全量+日志备份均成功 | 任一库备份失败 → P1 告警 |
| **每日** | 备份文件完整性校验 | `RESTORE VERIFYONLY` 全部通过 | 校验失败 → P1 告警 |
| **每周** | 恢复演练 | 至少覆盖 1 个独立租户库恢复 | 恢复时间 > RTO → P2 告警 |
| **每周** | 备份保留清理 | 过期备份文件已删除 | 保留超期 → P2 告警 |
| **每月** | 全量恢复演练 | 覆盖 1 次门户库 + 1 次租户共享库 | — |

---

## 参考

- [部署配置清单.md](部署配置清单.md) — §5 备份与恢复策略、§6 升级窗口 SOP（简要版）
- [监控告警配置.md](监控告警配置.md) — 与备份/恢复/升级相关的监控指标
- [saas-architecture-spec.md](saas-architecture-spec.md) — §3.8 四库边界、§14.2 资源预估
- [多租户数据库模式补充设计.md](多租户数据库模式补充设计.md) — §5 升级全流程（J-01~J-06）
