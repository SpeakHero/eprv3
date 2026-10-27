/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 单次调用尝试结果枚举
 */

using System.ComponentModel;

namespace Qlx.Abstractions.Ai;

/// <summary>
/// AI 单次厂商调用尝试的结果。
/// </summary>
public enum AiAttemptOutcome
{
    /// <summary>成功（HTTP 2xx 且内容有效）。</summary>
    [Description("成功")]
    Success = 1,

    /// <summary>失败（后续按策略继续下一个候选厂商）。</summary>
    [Description("失败")]
    Failed = 2
}
