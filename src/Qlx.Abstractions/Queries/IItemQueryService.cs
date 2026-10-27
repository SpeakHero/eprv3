/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 16:00:00 +08:00 中国标准时间 星期一
 * 描述：跨模块商品只读查询契约——Wms 作业校验批次属性与入库成本回退时使用（A-14）
 */

namespace Qlx.Abstractions.Queries;

/// <summary>
/// 商品扁平只读视图（stk_items 投影）。
/// <para>仅下发业务校验所需的原始字段；不做展示映射（B-2-5）。</para>
/// </summary>
/// <param name="Id">商品 Id。</param>
/// <param name="BatchEnabled">是否启用批次管理。</param>
/// <param name="ExpiryEnabled">是否启用有效期管理。</param>
/// <param name="RefCostPrice">参考成本价（业务口径原始值，不做权限裁剪）。</param>
/// <param name="Status">启停状态数值枚举（1 启用 / 2 停用）。</param>
/// <param name="CategoryId">商品分类 Id（原始引用；盘点按品类范围快照时使用）。</param>
public sealed record StockItemQueryRow(
    long Id,
    bool BatchEnabled,
    bool ExpiryEnabled,
    decimal? RefCostPrice,
    int Status,
    long CategoryId = 0);

/// <summary>
/// 跨模块商品只读查询契约。Stk 模块负责实现并强制当前租户过滤；
/// 消费方禁止自行跨库 Join，一律经本接口。
/// </summary>
public interface IItemQueryService : IModuleQuery
{
    /// <summary>
    /// 按商品 Id 查询单个商品。
    /// </summary>
    /// <param name="id">商品 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>商品行；不存在返回 <see langword="null"/>。</returns>
    Task<StockItemQueryRow?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>
    /// 按商品 Id 批量查询（禁止消费方逐条调用造成 N+1）。
    /// </summary>
    /// <param name="ids">商品 Id 列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命中的商品行列表（缺失的 Id 不返回）。</returns>
    Task<IReadOnlyList<StockItemQueryRow>> FindByIdsAsync(
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken);
}
