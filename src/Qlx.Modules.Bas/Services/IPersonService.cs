/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 22:00:00 +08:00 中国标准时间 星期日
 * 描述：人员主数据服务契约——编码发号 / 证件防重与加密 / 任职记录 / 在职状态机 / 岗位在岗数回写 /
 *       敏感字段明文端点（独立权限 + 审计）与证件子表维护
 */

using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Bas.Dtos;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 人员主数据服务。列表只读不缓存（分页筛选），详情主档经统一缓存读穿；
/// 写路径事务提交后落审计并失效 Bas 租户缓存前缀（C-5 失效优先）。
/// </summary>
public interface IPersonService
{
    /// <summary>人员分页列表（类型 / 在职状态 / 组织 / 岗位 / 关键字筛选，不缓存）。</summary>
    /// <param name="personType">人员类型数值（可选）。</param>
    /// <param name="employStatus">在职状态数值（可选）。</param>
    /// <param name="orgId">所属组织 Id（可选）。</param>
    /// <param name="positionId">所属岗位 Id（可选）。</param>
    /// <param name="keyword">关键字（姓名 / 编码）。</param>
    /// <param name="page">页码（从 1 开始）。</param>
    /// <param name="pageSize">每页大小（1~200）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>分页扁平行结果。</returns>
    Task<PagedResult<PersonRow>> ListAsync(
        int? personType,
        int? employStatus,
        long? orgId,
        long? positionId,
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct);

    /// <summary>人员详情（主档 + 当前任职 + 任职历史 + 证件列表）。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>人员详情。</returns>
    Task<PersonDetailRow> GetAsync(long id, CancellationToken ct);

    /// <summary>新建人员（编码经统一发号器按人员类型生成，证件/手机加密落库）。</summary>
    /// <param name="payload">新建入参（无编码字段）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的人员扁平行。</returns>
    Task<PersonRow> CreateAsync(SavePersonPayload payload, CancellationToken ct);

    /// <summary>更新人员基础信息与任职（调岗/换组织关闭旧任职并追加新记录）。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的人员扁平行。</returns>
    Task<PersonRow> UpdateAsync(long id, UpdatePersonPayload payload, CancellationToken ct);

    /// <summary>办理离职（终态，关闭当前任职并回写在岗数，提交后持久发布离职事件）。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="payload">离职入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的人员扁平行。</returns>
    Task<PersonRow> ResignAsync(long id, ResignPersonPayload payload, CancellationToken ct);

    /// <summary>再入职（仅离职状态允许，复用同一人员记录并新增当前任职）。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="payload">再入职入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的人员扁平行。</returns>
    Task<PersonRow> RehireAsync(long id, RehirePersonPayload payload, CancellationToken ct);

    /// <summary>读取敏感字段明文（调用方必须已通过 <c>bas.person.field.plaintext</c> 权限校验；本方法写审计）。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>明文行（证件号 / 手机号 / 银行卡号）。</returns>
    Task<PersonPlaintextRow> GetPlaintextAsync(long id, CancellationToken ct);

    /// <summary>人员证件档案扁平集合。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>证件扁平行列表。</returns>
    Task<IReadOnlyList<PersonCertRow>> ListCertsAsync(long id, CancellationToken ct);

    /// <summary>登记人员证件档案。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="payload">证件入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的证件扁平行。</returns>
    Task<PersonCertRow> SaveCertAsync(long id, SavePersonCertPayload payload, CancellationToken ct);

    /// <summary>更新人员证件档案。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="certId">证件记录 Id。</param>
    /// <param name="payload">证件入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的证件扁平行。</returns>
    Task<PersonCertRow> UpdateCertAsync(
        long id, long certId, SavePersonCertPayload payload, CancellationToken ct);

    /// <summary>删除人员证件档案。</summary>
    /// <param name="id">人员 Id。</param>
    /// <param name="certId">证件记录 Id。</param>
    /// <param name="ct">取消令牌。</param>
    Task DeleteCertAsync(long id, long certId, CancellationToken ct);
}
