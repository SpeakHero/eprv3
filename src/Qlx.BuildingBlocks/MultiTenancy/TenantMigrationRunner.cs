/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.1 beta
 * 创建时间：2026-10-21 23:59:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期四
 * 描述：租户数据库迁移原语：按迁移计划通用遍历、应用锁去重、共享库按模块独立历史表、禁止删库
 */

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 租户数据库迁移执行器。被启动装配（共享库去重一次）与后台预配服务（独立库按租户）共用，
/// 统一负责：按 <see cref="ITenantMigrationPlan"/> 依赖序遍历模块、数据库级应用锁去重、
/// 租户共享库按模块独立历史表（<c>__EFMigrationsHistory_{module}</c>）、以及禁止删库。
/// </summary>
/// <remarks>
/// 本执行器**只负责迁移**，不创建共享库、不激活租户、不做失败清理；
/// 状态激活与重试归属 <see cref="TenantMigrationService"/>，共享库建立归属门户/平台运维（B4-01）。
/// </remarks>
/// <remarks>构造函数。</remarks>
public sealed partial class TenantMigrationRunner(
    IServiceProvider serviceProvider,
    IEnumerable<ITenantMigrationPlan> plans,
    IOptions<TenantMigrationOptions> options,
    IOptions<SharedDatabaseOptions> sharedDatabase,
    Rls.TenantRlsInstaller rlsInstaller,
    ILogger<TenantMigrationRunner> logger)
{
    /// <summary>共享库迁移应用锁资源名（跨进程去重）。</summary>
    internal const string SharedMigrationLockResource = "qlx:migrate:tenant_shared";

    /// <summary>独立租户库迁移应用锁资源名前缀。</summary>
    private const string TenantMigrationLockResourcePrefix = "qlx:migrate:tenant:";

    /// <summary>共享库按模块拆分的历史表名前缀。</summary>
    private const string HistoryTablePrefix = "__EFMigrationsHistory_";

    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IEnumerable<ITenantMigrationPlan> _plans = plans;
    private readonly IOptions<TenantMigrationOptions> _options = options;
    private readonly IOptions<SharedDatabaseOptions> _sharedDatabase = sharedDatabase;
    private readonly Rls.TenantRlsInstaller _rlsInstaller = rlsInstaller;
    private readonly ILogger<TenantMigrationRunner> _logger = logger;


    /// <summary>
    /// 确保共享库（qlx_tenant_shared）存在：不存在时自动创建（仅限开发期便捷；生产应由运维预配）。
    /// 与 <see cref="MigrateTenantAsync"/> 的建库逻辑一致，但复用共享库连接配置。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task EnsureSharedDatabaseExistsAsync(CancellationToken cancellationToken)
    {
        var connectionInfo = _sharedDatabase.Value.ToConnectionInfo(0);
        await EnsureDatabaseExistsAsync(connectionInfo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 迁移租户共享库：**不建库**、**不删库**，全部模块在应用锁内执行一次（跨 Host 并发启动只迁一次），
    /// 每个模块使用独立历史表 <c>__EFMigrationsHistory_{module}</c>。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task MigrateSharedDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionInfo = _sharedDatabase.Value.ToConnectionInfo(0);
        var plans = GetSortedPlans();

        LogMigrationPlan(string.Join(", ", plans.Select(p => p.ModuleCode)));

        await ExecuteWithAppLockAsync(
            connectionInfo,
            SharedMigrationLockResource,
            token => MigrateModulesAsync(plans, tenantId: 0, connectionInfo, perModuleHistory: true, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 迁移单个独立租户库：确保数据库存在 → 应用锁内按模块依赖序迁移（历史表保持单表）。
    /// 迁移失败时抛出 <see cref="TenantModuleMigrationException"/>，由调用方决定重试与状态。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="connectionInfo">租户连接信息（由调用方从 Resolver 解析）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task MigrateTenantAsync(
        long tenantId, TenantConnectionInfo connectionInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connectionInfo);

        await EnsureDatabaseExistsAsync(connectionInfo, cancellationToken).ConfigureAwait(false);

        var plans = GetSortedPlans();
        LogMigrationPlan(string.Join(", ", plans.Select(p => p.ModuleCode)));

        await ExecuteWithAppLockAsync(
            connectionInfo,
            TenantMigrationLockResourcePrefix + tenantId,
            token => MigrateModulesAsync(plans, tenantId, connectionInfo, perModuleHistory: false, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>按依赖拓扑排序模块迁移计划。</summary>
    internal IReadOnlyList<ITenantMigrationPlan> GetSortedPlans() => TopologicalSort([.. _plans]);

    /// <summary>租户共享库的模块历史表名（独立库不使用）。</summary>
    internal static string BuildHistoryTableName(string moduleCode)
        => HistoryTablePrefix + moduleCode.Replace('-', '_');

    private async Task MigrateModulesAsync(
        IReadOnlyList<ITenantMigrationPlan> plans,
        long tenantId,
        TenantConnectionInfo connectionInfo,
        bool perModuleHistory,
        CancellationToken ct)
    {
        foreach (var plan in plans)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.Value.ModuleTimeoutSeconds));

            LogModuleStart(tenantId, plan.ModuleCode);
            var dbContext = CreateTenantDbContext(
                plan.DbContextType, tenantId, connectionInfo, perModuleHistory, plan.ModuleCode);

            try
            {
                await using (dbContext as IAsyncDisposable)
                {
                    // pending-changes 异常（模型与迁移快照不一致，如设计时/运行时提供程序不匹配）必须显式失败：
                    // 早期版本曾静默回落 EnsureCreated——它不写 __EFMigrationsHistory（毒害后续迁移），
                    // 且在已有任意表的库上直接空操作，造成「迁移成功」假象。此处不做回落，
                    // 异常冒泡为 TenantModuleMigrationException 由调用方显式处理。
                    await dbContext.Database.MigrateAsync(timeoutCts.Token).ConfigureAwait(false);

                    // 迁移完成后按模块模型安装 RLS 策略（未启用时空操作）；失败阻断本次迁移并进入重试
                    await _rlsInstaller.InstallAsync(
                        dbContext.Database.GetDbConnection(), dbContext.Model, timeoutCts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogModuleError(tenantId, plan.ModuleCode, ex);
                throw new TenantModuleMigrationException(plan, ex);
            }

            LogModuleDone(tenantId, plan.ModuleCode);
        }
    }

    private DbContext CreateTenantDbContext(
        Type dbContextType,
        long tenantId,
        TenantConnectionInfo connectionInfo,
        bool perModuleHistory,
        string moduleCode)
    {
        // 显式配置连接串与历史表：TenantDbContext.OnConfiguring 检测到 IsConfigured 后不再覆盖，
        // 避免 TenantId=0（共享库）落入开发兜底连接。
        var optionsBuilder = new DbContextOptionsBuilder();
        optionsBuilder.UseSqlServer(connectionInfo.ToConnectionString(), sql =>
        {
            if (perModuleHistory)
            {
                sql.MigrationsHistoryTable(BuildHistoryTableName(moduleCode));
            }
        });

        var snapshotResolver = new SnapshotTenantDatabaseResolver(tenantId, connectionInfo);
        var ctorArgs = new object[] { snapshotResolver, tenantId, optionsBuilder.Options };
        return (DbContext)ActivatorUtilities.CreateInstance(_serviceProvider, dbContextType, ctorArgs);
    }

    /// <summary>
    /// 在目标数据库的连接上获取会话级应用锁后执行迁移；连接释放即释放锁，
    /// 多 Host 并发启动时后到者等待前者的迁移完成（超时由 <see cref="TenantMigrationOptions.MigrationLockTimeoutSeconds"/> 控制）。
    /// </summary>
    private async Task ExecuteWithAppLockAsync(
        TenantConnectionInfo connectionInfo,
        string resource,
        Func<CancellationToken, Task> action,
        CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionInfo.ToConnectionString());
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var acquire = connection.CreateCommand();
        acquire.CommandText = """
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Session',
                @LockTimeout = @timeout;
            SELECT @result;
            """;
        acquire.Parameters.AddWithValue("@resource", resource);
        acquire.Parameters.AddWithValue("@timeout", _options.Value.MigrationLockTimeoutSeconds * 1000);

        var result = Convert.ToInt32(
            await acquire.ExecuteScalarAsync(ct).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
        if (result < 0)
        {
            throw new InvalidOperationException(
                $"获取迁移应用锁 [{resource}] 失败（返回码 {result}），可能有其它实例正在迁移或等待超时");
        }

        // 锁随连接关闭自动释放（LockOwner=Session），无需显式 sp_releaseapplock
        await action(ct).ConfigureAwait(false);
    }

    private async Task EnsureDatabaseExistsAsync(TenantConnectionInfo connectionInfo, CancellationToken ct)
    {
        // 用 Master 连接串检查数据库是否存在，不存在则创建（仅独立库；共享库禁止在此创建）
        var masterConnString = connectionInfo.ToBuilder("master").ConnectionString;

        await using var connection = new SqlConnection(masterConnString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = $"SELECT DB_ID('{SqlEscape(connectionInfo.DbName)}')";
        var existsObj = await checkCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);

        // ExecuteScalar 将 SQL NULL 装箱为 DBNull.Value（不是 C# null），必须显式判别
        if (existsObj is null || existsObj == DBNull.Value)
        {
            var createCmd = connection.CreateCommand();
            createCmd.CommandText = $"CREATE DATABASE [{SqlEscape(connectionInfo.DbName)}]";
            await createCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            LogDatabaseCreated(connectionInfo.DbName);
        }
    }

    private static string SqlEscape(string name) => name.Replace("]", "]]");

    private static List<ITenantMigrationPlan> TopologicalSort(List<ITenantMigrationPlan> plans)
    {
        // Kahn 拓扑排序
        var planMap = plans.ToDictionary(p => p.ModuleCode);
        var inDegree = new Dictionary<string, int>();
        var graph = new Dictionary<string, List<string>>();

        foreach (var p in plans)
        {
            inDegree[p.ModuleCode] = 0;
            graph[p.ModuleCode] = [];
        }

        foreach (var p in plans)
        {
            foreach (var dep in p.DependsOn)
            {
                if (planMap.ContainsKey(dep))
                {
                    graph[dep].Add(p.ModuleCode);  // dep → p（dep 必须先）
                    inDegree[p.ModuleCode]++;
                }
            }
        }

        var queue = new Queue<string>(inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key));
        var result = new List<ITenantMigrationPlan>();

        while (queue.Count > 0)
        {
            var code = queue.Dequeue();
            result.Add(planMap[code]);

            foreach (var next in graph[code])
            {
                if (--inDegree[next] == 0)
                {
                    queue.Enqueue(next);
                }
            }
        }

        // 依赖环路：拓扑排序后数量不够，把剩余的按原顺序追加
        if (result.Count < plans.Count)
        {
            var sortedCodes = new HashSet<string>(result.Select(p => p.ModuleCode));
            foreach (var p in plans.Where(p => !sortedCodes.Contains(p.ModuleCode)))
            {
                result.Add(p);
            }
        }

        return result;
    }

    /// <summary>一次性 Resolver：Resolve 固定返回快照，Preload 直接完成。</summary>
    private sealed class SnapshotTenantDatabaseResolver(long tenantId, TenantConnectionInfo info) : ITenantDatabaseResolver
    {
        private readonly long _tenantId = tenantId;
        private readonly TenantConnectionInfo _info = info;

        public TenantConnectionInfo Resolve(long tenantId)
            => tenantId == _tenantId ? _info : throw new InvalidOperationException();

        public Task PreloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Invalidate(long tenantId) { }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "迁移计划模块依赖序：{ModuleOrder}")]
    private partial void LogMigrationPlan(string moduleOrder);

    [LoggerMessage(Level = LogLevel.Information, Message = "  模块 {ModuleCode} 开始迁移（租户 {TenantId}）")]
    private partial void LogModuleStart(long tenantId, string moduleCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "  模块 {ModuleCode} 迁移完成（租户 {TenantId}）")]
    private partial void LogModuleDone(long tenantId, string moduleCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "  模块 {ModuleCode} 迁移失败（租户 {TenantId}）（详细异常见日志）")]
    private partial void LogModuleError(long tenantId, string moduleCode, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "已创建租户数据库 [{DbName}]")]
    private partial void LogDatabaseCreated(string dbName);
}

/// <summary>
/// 模块迁移失败异常。携带失败的迁移计划，供调用方判断是否为「首个模块」以决定失败清理策略。
/// </summary>
/// <remarks>构造函数。</remarks>
/// <param name="plan">失败的模块迁移计划。</param>
/// <param name="innerException">原始异常。</param>
public sealed class TenantModuleMigrationException(ITenantMigrationPlan plan, Exception innerException) : Exception(BuildMessage(plan, innerException), innerException)
{


    /// <summary>失败的模块迁移计划。</summary>
    public ITenantMigrationPlan Plan { get; } = plan;

    private static string BuildMessage(ITenantMigrationPlan plan, Exception innerException)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(innerException);
        return $"模块 {plan.ModuleCode} 迁移失败：{innerException.Message}";
    }
}
