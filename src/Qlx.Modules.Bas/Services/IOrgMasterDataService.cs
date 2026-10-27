/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 13:10:00 +08:00 中国标准时间 星期日
 * 描述：组织架构主数据服务接口——扁平查询/历史回看/创建/更新/停用启用/合并/审批申请
 */

using Qlx.Modules.Bas.Dtos;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 组织架构主数据服务。读路径经 <c>ICacheService.GetOrCreateAsync</c>（C-2/C-3/C-4），
/// 写路径先提交事务再按租户 Bas 前缀失效缓存（C-5 失效优先，只删不写）。
/// </summary>
public interface IOrgMasterDataService
{
    /// <summary>组织扁平列表；<paramref name="asOfDate"/> 非空时返回该日期生效的历史版本快照。</summary>
    Task<IReadOnlyList<OrgRow>> ListAsync(DateOnly? asOfDate, CancellationToken ct);

    /// <summary>单组织详情；不存在 404。</summary>
    Task<OrgRow> GetAsync(long id, CancellationToken ct);

    /// <summary>组织版本历史（按版本号升序）。</summary>
    Task<IReadOnlyList<OrgHistoryRow>> GetHistoryAsync(long id, CancellationToken ct);

    /// <summary>创建组织（编码经统一发号器生成）。</summary>
    Task<OrgRow> CreateAsync(SaveOrgPayload payload, CancellationToken ct);

    /// <summary>更新组织（名称/类型/上级/负责人/有效期/排序）。</summary>
    Task<OrgRow> UpdateAsync(long id, SaveOrgPayload payload, CancellationToken ct);

    /// <summary>停用/启用翻转；停用前置为无启用岗位且无在职人员。</summary>
    Task<OrgRow> ToggleAsync(long id, CancellationToken ct);

    /// <summary>合并组织：源组织归档（软删），前置为无下级、无在职人员、无启用岗位。</summary>
    Task<OrgRow> MergeAsync(long sourceId, long targetId, CancellationToken ct);

    /// <summary>提交组织调整审批申请（挂起，审批通过后由事件驱动生效）。</summary>
    Task<ApprovalRequestRow> SubmitAdjustRequestAsync(SubmitOrgAdjustPayload payload, CancellationToken ct);

    /// <summary>应用审批通过的组织调整载荷（幂等由调用方按请求状态保证）。</summary>
    Task ApplyAsync(SubmitOrgAdjustPayload payload, CancellationToken ct);
}
