/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：跨模块供应商只读查询契约——采购/入库等下游模块实时读取 Bas 供应商扁平数据时使用。
 */

namespace Qlx.Abstractions.Queries;

/// <summary>
/// 供应商扁平只读视图（bas_suppliers 投影）。
/// <para>仅下发原始字段：<c>Grade</c> / <c>Status</c> 为数值枚举，中文映射在前端公共层维护（B-2-5）。</para>
/// <para><c>Id</c> 为进程内雪花 long，进出 API 时由全局 Hashids JSON 元数据自动混淆。</para>
/// </summary>
/// <param name="Id">供应商 Id（bas_suppliers.id）。</param>
/// <param name="Code">供应商编码。</param>
/// <param name="Name">供应商名称。</param>
/// <param name="Uscc">统一社会信用代码。</param>
/// <param name="Grade">供应商评级数值枚举（A/B/C/Blacklist）。</param>
/// <param name="Status">供应商状态数值枚举（Pending/Active/Frozen/Closed）。</param>
public sealed record SupplierQueryRow(
    long Id,
    string Code,
    string Name,
    string Uscc,
    int Grade,
    int Status);

/// <summary>
/// 跨模块供应商只读查询契约。Bas 模块负责实现并强制当前租户过滤；
/// 消费方禁止自行跨库 Join，一律经本接口。
/// </summary>
public interface ISupplierQueryService : IModuleQuery
{
    /// <summary>
    /// 查询当前租户处于合作中（ACTIVE）的供应商扁平列表（采购下单可引用集合）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>合作中供应商行列表；无数据返回空数组。</returns>
    Task<IReadOnlyList<SupplierQueryRow>> ListActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 按供应商 Id 查询单条供应商。
    /// </summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>供应商行；不存在返回 <see langword="null"/>。</returns>
    Task<SupplierQueryRow?> FindAsync(long id, CancellationToken cancellationToken);
}
