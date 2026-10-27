/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存命令 Outbox 服务契约（人工重推 R-8）
 */

using Qlx.Modules.Wms.Dtos;

namespace Qlx.Modules.Wms.Services;

/// <summary>库存命令 Outbox 服务。</summary>
public interface IStockCommandService
{
    /// <summary>人工重推失败终态命令（仅 state=3 可重推，重复 409 WMS_INVALID_STATE）。</summary>
    /// <param name="id">Outbox 行 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>重推后的命令行（state=1、retryCount=0、nextRetryAt=now）。</returns>
    Task<StockCommandRow> RetryAsync(long id, CancellationToken ct);
}
