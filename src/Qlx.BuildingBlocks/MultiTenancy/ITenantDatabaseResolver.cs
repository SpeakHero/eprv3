/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-21 23:59:00 +08:00 中国标准时间 星期三
 * 描述：租户数据库路由解析契约，返回已解密的连接信息（含统一刷新入口）
 */

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 租户数据库路由解析契约。实现从门户库读取租户数据库连接信息、
/// 解密凭据（ASP.NET Core Data Protection）、组装为 <see cref="TenantConnectionInfo"/> 返回。
/// <para>
/// Resolve 为同步版本，供 DbContext 基类构造函数调用（运行时缓存命中，无 DB 查询）；
/// Preload 为异步预热版本，供 Host 启动期将全部活跃租户连接信息拉入缓存。
/// </para>
/// </summary>
public interface ITenantDatabaseResolver
{
    /// <summary>
    /// 同步解析指定租户的连接信息。实现必须内部缓存（ConcurrentDictionary 或 MemoryCache），
    /// 运行时调用不得触发 DB 查询。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <returns>已组装的连接信息。</returns>
    /// <exception cref="InvalidOperationException">租户不存在 / 数据库未初始化 / 连接信息未缓存时抛出。</exception>
    TenantConnectionInfo Resolve(long tenantId);

    /// <summary>
    /// 启动期预热：从门户库拉取全部活跃租户的连接信息并解密缓存。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PreloadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 使指定租户的缓存失效（租户数据库配置变更后调用）。
    /// </summary>
    void Invalidate(long tenantId);

    /// <summary>
    /// 统一刷新：先失效指定租户的缓存，再整体重新预热。
    /// 共享开通、升级 Cutover、连接信息变更、管理端手动刷新必须走本方法，
    /// 避免只删缓存未回填导致运行时 Resolve 未命中。
    /// </summary>
    /// <param name="tenantId">发生变更的租户 ID（仅用于失效，预热会覆盖全部租户）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RefreshAsync(long tenantId, CancellationToken cancellationToken)
    {
        Invalidate(tenantId);
        return PreloadAsync(cancellationToken);
    }
}
