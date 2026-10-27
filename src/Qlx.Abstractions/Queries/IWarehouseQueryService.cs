/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：跨模块仓库只读查询契约——Stk 及下游模块读取 Wms 仓库扁平数据时使用。
 */

namespace Qlx.Abstractions.Queries;

/// <summary>
/// 仓库扁平只读视图（wms_warehouses 投影）。
/// <para>仅下发原始字段：<c>OrgId</c> 为原始所属组织引用，<c>WhType</c>/<c>Status</c> 为数值枚举，
/// 中文映射与树形组装由消费方前端完成（B-2-1 / B-2-5）。</para>
/// <para><c>Id</c> 为进程内雪花 long，进出 API 时由全局 Hashids JSON 元数据自动混淆。</para>
/// </summary>
/// <param name="Id">仓库 Id（wms_warehouses.id）。</param>
/// <param name="OrgId">所属组织 Id；未绑定时为 <see langword="null"/>。</param>
/// <param name="Code">仓库编码。</param>
/// <param name="Name">仓库名称。</param>
/// <param name="WhType">仓库类型数值枚举（1 自有 / 2 租赁 / 3 项目临时）。</param>
/// <param name="Status">启用状态数值枚举。</param>
public sealed record WarehouseQueryRow(
    long Id,
    long? OrgId,
    string Code,
    string Name,
    int WhType,
    int Status);

/// <summary>
/// 跨模块仓库只读查询契约。Wms 模块负责实现并强制当前租户过滤；
/// 消费方禁止自行跨库 Join，一律经本接口。
/// </summary>
public interface IWarehouseQueryService : IModuleQuery
{
    /// <summary>
    /// 查询当前租户全部仓库（扁平列表）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>扁平仓库行列表；无数据返回空数组。</returns>
    Task<IReadOnlyList<WarehouseQueryRow>> ListAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 查询指定组织下的仓库（扁平列表）。
    /// </summary>
    /// <param name="orgId">组织 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>扁平仓库行列表；无数据返回空数组。</returns>
    Task<IReadOnlyList<WarehouseQueryRow>> ListByOrgAsync(long orgId, CancellationToken cancellationToken);

    /// <summary>
    /// 按仓库 Id 查询单条仓库。
    /// </summary>
    /// <param name="id">仓库 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>仓库行；不存在返回 <see langword="null"/>。</returns>
    Task<WarehouseQueryRow?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>
    /// 按仓库 Id 批量查询（禁止消费方逐条调用造成 N+1）。
    /// </summary>
    /// <param name="ids">仓库 Id 列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命中的仓库行列表（缺失的 Id 不返回）。</returns>
    Task<IReadOnlyList<WarehouseQueryRow>> FindByIdsAsync(
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken);
}
