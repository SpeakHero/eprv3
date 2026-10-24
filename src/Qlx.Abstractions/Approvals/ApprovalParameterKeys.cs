/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：审批中心业务运行参数键（por_parameters），config 值仅作 fallback
 */

namespace Qlx.Abstractions.Approvals;

/// <summary>
/// 审批中心系统参数键约定（点分小写，统一 <c>approval.*</c> 前缀）。
/// <para>
/// 运行阈值经 <see cref="Qlx.Abstractions.Modules.ISystemParameterProvider.GetIntAsync"/> 读取，
/// 参数表缺失 / 不可解析时回落到 <c>SupportApprovalsOptions</c> 的 config 值。
/// </para>
/// </summary>
public static class ApprovalParameterKeys
{
    /// <summary>超时催办次数上限（次）。</summary>
    public const string MaxUrgeCount = "approval.max_urge_count";

    /// <summary>催办节流间隔（分钟）。</summary>
    public const string UrgeIntervalMinutes = "approval.urge_interval_minutes";

    /// <summary>超时扫描单轮批量上限（条）。</summary>
    public const string TimeoutScanBatchSize = "approval.timeout_scan_batch_size";
}
