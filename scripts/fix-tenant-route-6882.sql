/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：e2e-6882 一次性修复脚本——纠正「租户已升级独立库、路由行仍为共享模式」的错切数据（L-02）
 *
 * 背景：
 *   升级 Cutover 早期版本的 TenantDatabase.RelocateTo 只改 DbHost/DbPort/DbName，未同步
 *   por_tenant_databases.DatabaseMode；路由解析器按路由行模式判定共享/独立分支，
 *   导致已升级租户仍被组装为共享库连接（e2e-6882：租户详情 database_mode=Isolated，
 *   但运行时仍连租户共享库）。
 *   代码侧已在 RelocateTo 内同步置 Isolated；本脚本仅修复历史遗留数据。
 *
 * 执行前提：
 *   1. 在【门户库】执行（默认库名 qlx_portal，按环境 config/portal/database.json 确认）。
 *   2. 先执行「一、诊断」确认命中行，再执行「二、修复」。
 *   3. 脚本幂等：重复执行不会重复变更（仅处理 por_tenants.DatabaseMode=2 且
 *      por_tenant_databases.DatabaseMode=1 的行）。
 *
 * 回滚说明：
 *   本脚本只把路由行模式从 Shared(1) 纠正为 Isolated(2)，与租户主表一致；
 *   如需回滚（仅在确认该租户实际仍使用共享库时），按下方「三、回滚」逐租户执行，
 *   且必须同时回退 por_tenants.DatabaseMode=1，避免再次错切。
 */

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- ========== 一、诊断（只读） ==========
-- 列出模式漂移：租户主表已 Isolated，但路由行仍为 Shared
SELECT
    t.Id                AS TenantId,
    t.TenantCode        AS TenantCode,
    t.DisplayName       AS TenantName,
    t.Status            AS TenantStatus,
    t.DatabaseMode      AS TenantDatabaseMode,
    d.Id                AS DatabaseRowId,
    d.DatabaseMode      AS RouteDatabaseMode,
    d.DbHost            AS RouteDbHost,
    d.DbPort            AS RouteDbPort,
    d.DbName            AS RouteDbName
FROM por_tenants AS t
INNER JOIN por_tenant_databases AS d ON d.TenantId = t.Id
WHERE t.DatabaseMode = 2          -- TenantDatabaseMode.Isolated
  AND d.DatabaseMode = 1          -- TenantDatabaseMode.Shared（漂移行）
ORDER BY t.Id;

-- ========== 二、修复（幂等） ==========
DECLARE @fixedCount int = 0;

BEGIN TRANSACTION;

UPDATE d
SET d.DatabaseMode = 2            -- TenantDatabaseMode.Isolated
FROM por_tenant_databases AS d
INNER JOIN por_tenants AS t ON t.Id = d.TenantId
WHERE t.DatabaseMode = 2
  AND d.DatabaseMode = 1;

SET @fixedCount = @@ROWCOUNT;

COMMIT TRANSACTION;

PRINT CONCAT(N'已纠正模式漂移的路由行数量：', @fixedCount);
PRINT N'提示：执行后需重启 Qlx.Host（或触发路由刷新）以重建租户连接缓存。';

-- 复核：应返回 0 行
SELECT COUNT(1) AS RemainingDrift
FROM por_tenants AS t
INNER JOIN por_tenant_databases AS d ON d.TenantId = t.Id
WHERE t.DatabaseMode = 2
  AND d.DatabaseMode = 1;

-- ========== 三、回滚模板（按需逐租户执行，勿整表执行） ==========
-- DECLARE @TenantId bigint = 6882;  -- 替换为实际租户 Id
-- BEGIN TRANSACTION;
-- UPDATE d SET d.DatabaseMode = 1
-- FROM por_tenant_databases AS d WHERE d.TenantId = @TenantId;
-- UPDATE t SET t.DatabaseMode = 1
-- FROM por_tenants AS t WHERE t.Id = @TenantId;
-- COMMIT TRANSACTION;
