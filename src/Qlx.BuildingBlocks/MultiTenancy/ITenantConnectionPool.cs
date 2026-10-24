/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 05:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 05:30:00 +08:00 中国标准时间 星期三
 * 描述：租户连接池闸门：按租户 ID 限制最大并发打开的 DbContext/连接数
 */

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 租户连接池闸门。在高租户数（50+）场景下，
/// 用 SemaphoreSlim 限制每个租户同时打开的 DbContext/连接数，
/// 避免 ADO.NET 内置连接池 × 租户数 = 连接数爆炸。
/// </summary>
public interface ITenantConnectionPool
{
    /// <summary>
    /// 获取一个闸门令牌（Acquire 即进入 WaitAsync）。
    /// 调用方 using 包裹，结束后自动释放。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>IDisposable 令牌，Dispose 即释放闸门。</returns>
    Task<IDisposable> AcquireAsync(long tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// 重置指定租户的闸门（租户数据库重建后调用）。
    /// </summary>
    void Reset(long tenantId);
}
