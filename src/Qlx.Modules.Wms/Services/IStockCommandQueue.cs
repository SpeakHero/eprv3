/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 21:00:00 +08:00 中国标准时间 星期一
 * 描述：Wms → Stk 库存命令入队契约——作业完成与命令入队必须同事务（A-17），禁止直连 Stk 写台账
 */

using Qlx.Abstractions.Stock;

namespace Qlx.Modules.Wms.Services;

/// <summary>
/// 库存命令入队契约。实现只向当前 <c>WmsTenantDbContext</c> 追加 Outbox 行，
/// <b>不调用</b> <c>SaveChanges</c>——调用方必须在同一事务（<c>ExecuteInTransactionAsync</c> 回调内）
/// 与作业状态一并提交，保证「状态变更与命令入队」原子。
/// </summary>
internal interface IStockCommandQueue
{
    /// <summary>
    /// 入队一批库存变动命令。同一幂等键（含本批内重复与库内已存在）零新增。
    /// </summary>
    /// <param name="requests">库存变动请求（幂等键必填）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次新增的 Outbox 行数（去重后）。</returns>
    Task<int> EnqueueAsync(
        IReadOnlyList<PostStockTxnRequest> requests,
        CancellationToken cancellationToken);
}
