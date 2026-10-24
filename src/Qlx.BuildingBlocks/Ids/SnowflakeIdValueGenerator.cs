/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-23 14:30:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 16:20:00 +08:00 中国标准时间 星期五
 * 描述：雪花 ID 的 EF 值生成器——实体 Add 时即生成真实雪花值，规避默认键 0 的 IdentityMap 冲突
 */

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// 雪花 ID 的 EF Core 值生成器（非临时值）。挂接于 <c>ValueGenerated.OnAdd</c> 的长整型主键：
/// 实体 Add 到 ChangeTracker 时同步生成真实雪花值并作为跟踪键，保证同类型多实体单次
/// <c>SaveChanges</c> 不会以默认键 0 互撞 IdentityMap；显式赋值的 Id（HasData / 固定种子）不被覆盖。
/// 显式生成器使 SQL Server 值生成策略降级为 None，列保持纯 bigint（禁止 IDENTITY）。
/// <para>提供惰性解析构造函数：宿主启动期（迁移阶段）模型构建时 workerId 租约尚未就绪，
/// 生成器解析延迟到首次 <see cref="Next"/> 调用（真实 Add 发生在宿主启动完成之后）。</para>
/// </summary>
public sealed class SnowflakeIdValueGenerator : ValueGenerator<long>
{
    private readonly Func<ISnowflakeIdGenerator> _resolver;
    private ISnowflakeIdGenerator? _cached;

    /// <summary>
    /// 直接实例构造函数：测试与进程内固定场景使用。
    /// </summary>
    /// <param name="snowflake">雪花 ID 生成器（单例，线程安全）。</param>
    public SnowflakeIdValueGenerator(ISnowflakeIdGenerator snowflake)
    {
        ArgumentNullException.ThrowIfNull(snowflake);
        _resolver = () => snowflake;
    }

    /// <summary>
    /// 惰性解析构造函数：首次 <see cref="Next"/> 时才经 <paramref name="resolver"/> 解析真实生成器并缓存。
    /// 用于宿主模型构建早于 workerId 租约初始化的场景（启动期迁移）。
    /// </summary>
    /// <param name="resolver">生成器解析委托（须返回进程内单例，重复解析结果一致）。</param>
    public SnowflakeIdValueGenerator(Func<ISnowflakeIdGenerator> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    /// <inheritdoc />
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc />
    public override long Next(EntityEntry entry)
    {
        // 引用赋值原子、容器解析结果恒定（单例），无锁双检即可
        return (_cached ??= _resolver()).NextId();
    }
}
