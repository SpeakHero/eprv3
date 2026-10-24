/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.2.0 beta
 * 创建时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-22 22:35:00 +08:00 中国标准时间 星期四
 * 描述：租户级 DbContext 基类，动态路由租户数据库连接（共享/独立）+ 全局注入 TenantId 过滤 + snake_case 命名 + 软删过滤 + 实体级表前缀特性
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 租户级 DbContext 基类。所有业务模块 DbContext 必须继承此类，禁止直连固定连接串。
/// 构造函数注入 <see cref="ITenantDatabaseResolver"/> 与 tenant_id；
/// OnConfiguring 动态获取连接串、OnModelCreating 自动注入 TenantId 全局查询过滤。
/// 同时接入 <see cref="ITenantConnectionPool"/> 闸门（50+ 租户场景限制并发连接数）。
/// </summary>
/// <remarks>
/// 构造函数：注入租户数据库路由解析器、tenant_id 与基础 DbContextOptions。
/// </remarks>
/// <param name="resolver">租户数据库路由解析器。</param>
/// <param name="tenantId">当前请求的租户 ID（0 表示请求管线尚未初始化，开发期降级用）。</param>
/// <param name="baseOptions">基础 DbContextOptions（不含连接串）。</param>
public abstract partial class TenantDbContext(
    ITenantDatabaseResolver resolver,
    long tenantId,
    DbContextOptions baseOptions) : DbContext(baseOptions)
{
    /// <summary>当前请求的租户 ID。</summary>
    protected long TenantId { get; } = tenantId;

    /// <summary>
    /// 当前上下文绑定的租户 ID（0 表示无租户上下文）。
    /// 供写入守卫与隔离巡检读取，业务代码禁止直接用于拼接 SQL。
    /// </summary>
    public long CurrentTenantId => TenantId;

    /// <summary>租户连接池闸门令牌（Dispose 时自动释放）。</summary>
    private IDisposable? _poolLease;

    /// <summary>
    /// 模块表前缀，由子类覆写。返回如 <c>"pur_"</c> / <c>"sal_"</c> / <c>"bas_"</c>。
    /// 默认 <c>null</c> 表示不加前缀（仅 snake_case）。
    /// </summary>
    /// <remarks>
    /// 架构规格 §2.3.4：每模块固定前缀，表名格式 <c>{module_prefix}_{entity_snake_case}</c>。
    /// </remarks>
    protected virtual string? GetModuleTablePrefix()
    {
        return null;
    }

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // 应用级 IServiceProvider：AddDbContext / 手工 DbContextOptionsBuilder 注册时随选项携带。
        // 注意：此处禁止经 IInfrastructure<IServiceProvider> 访问上下文自身——
        // OnConfiguring 期间上下文仍在配置中，会抛“cannot be used inside OnConfiguring”异常。
        var applicationServices = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()
            ?.ApplicationServiceProvider;

        if (!optionsBuilder.IsConfigured)
        {
            // 模块侧 AddDbContext 仅注册类型、不带提供程序（连接串按租户动态路由），
            // 由基类在此统一挂 SqlServer 提供程序；测试场景可预配 Sqlite 走 IsConfigured 分支跳过
            _ = optionsBuilder.UseSqlServer(GetConnectionString(TenantId, applicationServices));
        }

        // 接入租户连接池闸门（TenantId>0 且应用容器内注册了池时）
        if (TenantId > 0 && applicationServices?.GetService<ITenantConnectionPool>() is { } pool)
        {
            try
            {
                // Fire-and-forget 用同步 Wait（OnConfiguring 是同步方法）；
                // OnConfiguring 为 EF 同步管线回调、无环境 CancellationToken 可用，
                // 按规范【豁免点 1】显式传 None 并注明原因
                _poolLease = pool.AcquireAsync(TenantId, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                // 闸门获取失败不阻断主流程（极端场景降级为直连）
            }
        }

        // 从应用容器获取注册的拦截器（AuditSaveChangesInterceptor 等）；
        // 测试场景经 options.AddInterceptors 注入，ApplicationServiceProvider 为空时跳过容器解析
        if (applicationServices is not null)
        {
            IInterceptor[] interceptors = [.. applicationServices.GetServices<IInterceptor>()];
            if (interceptors.Length > 0)
            {
                _ = optionsBuilder.AddInterceptors(interceptors);
            }
        }
    }

    /// <summary>
    /// 显式释放连接池闸门（调用方显式 Dispose DbContext 时自动调用）。
    /// </summary>
    private void ReleasePoolLease()
    {
        _poolLease?.Dispose();
        _poolLease = null;
    }

    /// <summary>
    /// DbContext.Dispose 是 sealed，无法 override。
    /// 用 using 包裹 DbContext 时由 Dispose 链自动传播到基类；
    /// 连接池闸门通过显式 Dispose 调用在租户 DbContext Factory 中管理。
    /// </summary>
    public void DisposePoolLease() => ReleasePoolLease();

    /// <summary>
    /// 从 Resolver 获取指定租户的连接串（共享/独立均由 Resolver 按路由行判定）。
    /// <c>TenantId&lt;=0</c> 时仅当宿主为 Development 且显式启用 <see cref="TenantConnectionFallbackOptions"/>
    /// 才使用开发兜底，否则直接失败——禁止静默连到任意库，生产环境永不启用兜底。
    /// </summary>
    private string GetConnectionString(long tenantId, IServiceProvider? applicationServices)
    {
        if (tenantId > 0)
        {
            return resolver.Resolve(tenantId).ToConnectionString();
        }

        var fallback = applicationServices?
            .GetService<IOptions<TenantConnectionFallbackOptions>>()?.Value;

        // 宿主环境缺失（纯单元测试场景）视为允许按配置判定；真实宿主必须为 Development
        var environment = applicationServices?.GetService<IHostEnvironment>();
        var isDevelopment = environment is null || environment.IsDevelopment();

        if (isDevelopment && fallback is { Enabled: true } && !string.IsNullOrWhiteSpace(fallback.ConnectionString))
        {
            return fallback.ConnectionString;
        }

        throw new InvalidOperationException(
            $"租户 Id 未初始化（{tenantId}），无法解析租户数据库连接。" +
            $"开发环境如需兜底，请配置 {TenantConnectionFallbackOptions.SectionName}:Enabled=true 并提供 ConnectionString；" +
            "生产环境禁止启用该兜底。");
    }

    /// <summary>
    /// 覆盖全局查询过滤与命名约定：
    /// ① snake_case 表名 / 列名（PascalCase→snake_case）；
    /// ② 实体含 TenantId 则自动注入 TenantId 全局过滤；
    /// ③ 实体含 IsDeleted 则自动注入 IsDeleted=false 软删过滤。
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var prefix = GetModuleTablePrefix();

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            // 模块前缀 + snake_case 表名；实体标注 [TenantTablePrefix] 时特性前缀优先（单模块多前缀场景）
            var snakeName = ToSnakeCase(entityType.GetTableName() ?? entityType.ClrType.Name);
            var entityPrefix = entityType.ClrType.GetCustomAttribute<TenantTablePrefixAttribute>()?.Prefix;
            var effectivePrefix = entityPrefix ?? prefix;
            var tableName = string.IsNullOrEmpty(effectivePrefix) ? snakeName : $"{effectivePrefix}{snakeName}";
            entityType.SetTableName(tableName);

            // snake_case 列名
            foreach (IMutableProperty property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            // TenantId 全局过滤
            IMutableProperty? tenantIdProperty = entityType.FindProperty(nameof(TenantId))
                ?? entityType.FindProperty("Tenant_Id");

            if (tenantIdProperty is { })
            {
                ParameterExpression parameter = Expression.Parameter(entityType.ClrType, "e");
                MemberExpression propertyAccess = Expression.MakeMemberAccess(parameter, tenantIdProperty.PropertyInfo!);
                // 必须引用「上下文实例属性」而非固定常量：EF 模型按上下文类型缓存，
                // 常量会在模型构建后永久固化到首个租户；实例引用由 EF 在每次查询时重写为当前上下文。
                MemberExpression tenantAccess = Expression.Property(
                    Expression.Constant(this), nameof(CurrentTenantId));
                BinaryExpression comparison = Expression.Equal(propertyAccess, tenantAccess);
                LambdaExpression lambda = Expression.Lambda(comparison, parameter);

                entityType.SetQueryFilter(CombineQueryFilter(
                    entityType.GetDeclaredQueryFilters().FirstOrDefault()?.Expression, lambda));
            }

            // 软删过滤
            IMutableProperty? isDeletedProperty = entityType.FindProperty("IsDeleted");
            if (isDeletedProperty != null && isDeletedProperty.ClrType == typeof(bool))
            {
                ParameterExpression parameter = Expression.Parameter(entityType.ClrType, "e");
                MemberExpression propertyAccess = Expression.MakeMemberAccess(parameter, isDeletedProperty.PropertyInfo!);
                ConstantExpression falseConstant = Expression.Constant(false);
                BinaryExpression comparison = Expression.Equal(propertyAccess, falseConstant);
                LambdaExpression lambda = Expression.Lambda(comparison, parameter);

                entityType.SetQueryFilter(CombineQueryFilter(
                    entityType.GetDeclaredQueryFilters().FirstOrDefault()?.Expression, lambda));
            }
        }
    }

    /// <summary>
    /// 合并现有 QueryFilter 与新过滤器（AND 逻辑）。
    /// </summary>
    private static LambdaExpression? CombineQueryFilter(LambdaExpression? existing, LambdaExpression? additional)
    {
        if (existing is null)
        {
            return additional;
        }

        if (additional is null)
        {
            return existing;
        }

        ParameterExpression parameter = existing.Parameters[0];
        BinaryExpression body = Expression.AndAlso(
            existing.Body,
            new ParameterReplacer(additional.Parameters[0], parameter).Visit(additional.Body));

        return Expression.Lambda(body, parameter);
    }

    /// <summary>PascalCase 转 snake_case。</summary>
    private static string ToSnakeCase(string input)
    {
        return string.IsNullOrWhiteSpace(input) ? input : PascalToSnakeBoundaryRegex().Replace(input, "$1$3_$2$4").ToLowerInvariant();
    }

    /// <summary>简单的参数替换表达式访问器。</summary>
    private sealed class ParameterReplacer(ParameterExpression target, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
        {
            return ReferenceEquals(node, target) ? replacement : base.VisitParameter(node);
        }
    }

    /// <summary>PascalCase 边界切分模式（源生成预编译，200ms 匹配超时）。</summary>
    [GeneratedRegex(@"([a-z0-9])([A-Z])|([A-Z]+)([A-Z][a-z])", RegexOptions.Compiled | RegexOptions.CultureInvariant, 200)]
    private static partial Regex PascalToSnakeBoundaryRegex();
}

/// <summary>
/// 标记实体类包含 TenantId 属性，供 <see cref="TenantDbContext.OnModelCreating"/> 自动注入全局过滤。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class TenantEntityAttribute : Attribute;

/// <summary>
/// 雪花主键约定（公共层唯一实现）：将模型中全部 <c>long</c> 类型 <c>Id</c> 属性统一挂显式
/// <see cref="Qlx.BuildingBlocks.Ids.SnowflakeIdValueGenerator"/>（<see cref="ValueGenerated.OnAdd"/>）——
/// 实体 Add 到 ChangeTracker 时即生成真实雪花值并作为跟踪键，同类型多实体单次 SaveChanges
/// 不会因默认键 0 互撞 IdentityMap；显式生成器使 SQL Server 值生成策略降级为 None（禁止 IDENTITY，
/// 列保持纯 bigint 主键）。实体 <c>Id</c> 继承自 <see cref="Qlx.Abstractions.Domain.Entity"/> 基类
/// 无法加数据注解，各 DbContext 在 <c>OnModelCreating</c> 末尾显式调用本约定
/// （Bas 自增模型除外，不得调用）。
/// </summary>
public static class SnowflakeKeyConvention
{
    /// <summary>测试 / 设计期回退生成器（无应用容器时进程内唯一即可，workerId=0，线程安全）。</summary>
    private static readonly Ids.ISnowflakeIdGenerator s_fallbackGenerator = new Ids.SnowflakeIdGenerator(workerId: 0);

    /// <summary>
    /// 对当前模型全部实体应用雪花主键约定（宿主 / 测试统一入口）。
    /// 模型构建期仅捕获应用根容器引用，真实生成器延迟到首次 Add（Next 调用）解析——
    /// 宿主启动期迁移阶段模型构建早于 workerId 租约初始化，禁止此时解析容器单例。
    /// </summary>
    /// <param name="modelBuilder">模型构造器。</param>
    /// <param name="context">当前 DbContext 实例（经内部 <see cref="IDbContextOptions"/> 读取应用容器，避免捕获构造参数）。</param>
    public static void Apply(ModelBuilder modelBuilder, DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var applicationServices = context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;

        // 无应用容器（纯单元测试手工构造选项）→ 固定回退静态实例；
        // 有容器 → 惰性解析（首次 Next 时宿主已完成启动，租约就绪）
        Func<Ids.ISnowflakeIdGenerator> resolver = applicationServices is null
            ? static () => s_fallbackGenerator
            : () => applicationServices.GetRequiredService<Ids.ISnowflakeIdGenerator>();

        Apply(modelBuilder, resolver);
    }

    /// <summary>对当前模型全部实体应用雪花主键约定（直接实例，测试 / 工具场景）。</summary>
    /// <param name="modelBuilder">模型构造器。</param>
    /// <param name="snowflake">雪花 ID 生成器。</param>
    public static void Apply(ModelBuilder modelBuilder, Ids.ISnowflakeIdGenerator snowflake)
    {
        ArgumentNullException.ThrowIfNull(snowflake);
        Apply(modelBuilder, () => snowflake);
    }

    /// <summary>对当前模型全部实体应用雪花主键约定（惰性解析核心实现）。</summary>
    /// <param name="modelBuilder">模型构造器。</param>
    /// <param name="resolver">雪花 ID 生成器解析委托（首次 Add 时调用并缓存）。</param>
    public static void Apply(ModelBuilder modelBuilder, Func<Ids.ISnowflakeIdGenerator> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            IMutableProperty? idProperty = entityType.FindProperty(nameof(Qlx.Abstractions.Domain.Entity.Id));
            if (idProperty is not null && idProperty.ClrType == typeof(long))
            {
                idProperty.ValueGenerated = ValueGenerated.OnAdd;
                idProperty.SetValueGeneratorFactory((_, _) => new Ids.SnowflakeIdValueGenerator(resolver));
                // 显式降级 SQL Server 策略为 None：OnAdd 整型键默认被挂 IDENTITY，
                // 雪花主键必须保持纯 bigint 列（应用层生成，禁止数据库自增）
                idProperty.SetValueGenerationStrategy(SqlServerValueGenerationStrategy.None);
            }
        }
    }
}
