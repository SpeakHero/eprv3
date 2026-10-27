/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：跨模块商品分类只读查询契约——采购/销售/库存等下游模块实时读取 Bas 分类扁平数据时使用。
 */

namespace Qlx.Abstractions.Queries;

/// <summary>
/// 商品分类扁平只读视图（bas_item_categories 投影）。
/// <para>仅下发原始字段：<c>ParentId</c> 为原始父级引用，分类树组装由消费方前端完成（B-2-1）；
/// <c>LevelNo</c> / <c>Status</c> 为数值枚举，中文映射在前端公共层维护（B-2-5）。</para>
/// <para><c>Id</c> 为进程内雪花 long，进出 API 时由全局 Hashids JSON 元数据自动混淆。</para>
/// </summary>
/// <param name="Id">分类 Id（bas_item_categories.id）。</param>
/// <param name="ParentId">父级分类 Id；顶级分类为 <see langword="null"/>。</param>
/// <param name="Code">分类编码（层级连续性编码）。</param>
/// <param name="Name">分类名称。</param>
/// <param name="LevelNo">分类层级（从 1 开始的原始层级号）。</param>
/// <param name="Status">分类状态数值枚举。</param>
public sealed record ProductCategoryQueryRow(
    long Id,
    long? ParentId,
    string Code,
    string Name,
    int LevelNo,
    int Status);

/// <summary>
/// 跨模块商品分类只读查询契约。Bas 模块负责实现并强制当前租户过滤；
/// 消费方禁止自行跨库 Join，一律经本接口。
/// </summary>
public interface IProductCategoryQueryService : IModuleQuery
{
    /// <summary>
    /// 查询当前租户全部商品分类（扁平列表，不组装层级）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>扁平分类行列表；无分类返回空数组。</returns>
    Task<IReadOnlyList<ProductCategoryQueryRow>> ListAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 按分类 Id 查询单条分类。
    /// </summary>
    /// <param name="id">分类 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分类行；不存在返回 <see langword="null"/>。</returns>
    Task<ProductCategoryQueryRow?> FindAsync(long id, CancellationToken cancellationToken);
}
