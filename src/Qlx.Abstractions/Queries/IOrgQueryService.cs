/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：跨模块组织只读查询契约——下游模块（采购/销售/人员等）实时读取 Bas 组织扁平数据时使用。
 */

namespace Qlx.Abstractions.Queries;

/// <summary>
/// 组织扁平只读视图（bas_organizations 投影）。
/// <para>仅下发原始字段：<c>ParentId</c> 为原始父级引用，层级组装由消费方前端完成（B-2-1）；
/// <c>Type</c> / <c>Status</c> 为数值枚举，中文映射在前端公共层维护（B-2-5）。</para>
/// <para><c>Id</c> 为进程内雪花 long，进出 API 时由全局 Hashids JSON 元数据自动混淆。</para>
/// </summary>
/// <param name="Id">组织 Id（bas_organizations.id）。</param>
/// <param name="ParentId">父级组织 Id；顶级组织为 <see langword="null"/>。</param>
/// <param name="Code">组织编码。</param>
/// <param name="Name">组织名称。</param>
/// <param name="Type">组织类型数值枚举。</param>
/// <param name="Status">组织状态数值枚举。</param>
/// <param name="SortOrder">展示排序号（原始值，排序由消费方决定）。</param>
public sealed record OrgQueryRow(
    long Id,
    long? ParentId,
    string Code,
    string Name,
    int Type,
    int Status,
    int SortOrder);

/// <summary>
/// 跨模块组织只读查询契约。Bas 模块负责实现并强制当前租户过滤；
/// 消费方禁止自行跨库 Join，一律经本接口。
/// </summary>
public interface IOrgQueryService : IModuleQuery
{
    /// <summary>
    /// 查询当前租户全部组织（扁平列表，不组装层级）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>扁平组织行列表；无组织返回空数组。</returns>
    Task<IReadOnlyList<OrgQueryRow>> ListAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 按组织 Id 查询单条组织。
    /// </summary>
    /// <param name="id">组织 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>组织行；不存在返回 <see langword="null"/>。</returns>
    Task<OrgQueryRow?> FindAsync(long id, CancellationToken cancellationToken);
}
