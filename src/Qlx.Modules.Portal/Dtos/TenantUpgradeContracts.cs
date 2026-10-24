/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户升级任务对外契约（门户管理端 API 出参，ID 一律混淆字符串）
 */

namespace Qlx.Modules.Portal.Dtos;

/// <summary>
/// 升级任务条目（对外出参）。
/// </summary>
/// <param name="Id">任务 ID（混淆字符串）。</param>
/// <param name="TenantId">租户 ID（混淆字符串）。</param>
/// <param name="Status">任务状态（Pending / Preparing / Copying / Verifying / Cutover / Done / Failed / Cleaned）。</param>
/// <param name="ModuleCode">当前处理模块编码。</param>
/// <param name="CurrentTable">当前处理表名。</param>
/// <param name="RowsCopied">已累计复制行数。</param>
/// <param name="ErrorMessage">最近失败原因。</param>
/// <param name="RetryCount">累计重试次数。</param>
/// <param name="StartedAt">任务创建时间（UTC）。</param>
/// <param name="CompletedAt">完成 / 失败时间（UTC）。</param>
public sealed record TenantUpgradeJobItem(
    string Id,
    string TenantId,
    string Status,
    string? ModuleCode,
    string? CurrentTable,
    long RowsCopied,
    string? ErrorMessage,
    int RetryCount,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);
