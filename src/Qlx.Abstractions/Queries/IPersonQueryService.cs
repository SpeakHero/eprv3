/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：跨模块人员只读查询契约——组织/岗位在岗统计及下游模块引用 Bas 人员扁平数据时使用。
 */

namespace Qlx.Abstractions.Queries;

/// <summary>
/// 人员扁平只读视图（bas_persons 投影）。
/// <para>仅下发非敏感原始字段：证件号、银行卡号、手机号等敏感字段一律不出现在本视图（敏感字段走独立授权接口并写审计）。</para>
/// <para>仅下发原始字段：<c>PersonType</c> / <c>EmployStatus</c> 为数值枚举，中文映射在前端公共层维护（B-2-5）。</para>
/// <para><c>Id</c> 为进程内雪花 long，进出 API 时由全局 Hashids JSON 元数据自动混淆。</para>
/// </summary>
/// <param name="Id">人员 Id（bas_persons.id）。</param>
/// <param name="Code">人员编码。</param>
/// <param name="Name">人员姓名。</param>
/// <param name="PersonType">人员类型数值枚举（正式/劳务等）。</param>
/// <param name="OrgId">所属组织 Id；未分配为 <see langword="null"/>。</param>
/// <param name="PositionId">所属岗位 Id；未分配为 <see langword="null"/>。</param>
/// <param name="EmployStatus">在职状态数值枚举。</param>
/// <param name="IsDeleted">是否已软删（供消费方判定数据可用性）。</param>
public sealed record PersonQueryRow(
    long Id,
    string Code,
    string Name,
    int PersonType,
    long? OrgId,
    long? PositionId,
    int EmployStatus,
    bool IsDeleted);

/// <summary>
/// 跨模块人员只读查询契约。Bas 模块负责实现并强制当前租户过滤；
/// 消费方禁止自行跨库 Join，一律经本接口。
/// </summary>
public interface IPersonQueryService : IModuleQuery
{
    /// <summary>
    /// 按人员 Id 查询单条人员。
    /// </summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>人员行；不存在返回 <see langword="null"/>。</returns>
    Task<PersonQueryRow?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>
    /// 查询指定岗位下的在职人员（岗位在岗统计用）。
    /// </summary>
    /// <param name="positionId">岗位 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该岗位在职人员扁平列表；无在岗人员返回空数组。</returns>
    Task<IReadOnlyList<PersonQueryRow>> ListByPositionAsync(long positionId, CancellationToken cancellationToken);
}
