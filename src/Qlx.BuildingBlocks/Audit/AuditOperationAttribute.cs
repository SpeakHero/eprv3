/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 12:30:00 +08:00 中国标准时间 星期五
 * 描述：方法级审计特性——标注 MVC Action 方法，由 OperationAuditFilter 执行后写操作日志
 */

namespace Qlx.BuildingBlocks.Audit;

/// <summary>
/// 方法级审计操作特性。标注于 MVC Controller Action 方法（仅 Method 级），
/// 执行成功后由 <see cref="OperationAuditFilter"/> 写 <c>IAuditService.CaptureOperationAsync</c>；
/// 执行异常时写失败审计（操作码追加 <see cref="OperationAuditFilter.FailedOperationSuffix"/>，异常原样上抛）。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AuditOperationAttribute(string module, string operation, string description) : Attribute
{
    /// <summary>业务模块编码（如 <c>support</c>，与 ModuleManifest.ModuleCode 对齐）。</summary>
    public string Module { get; } = module;

    /// <summary>操作编码（大写下划线风格，如 UPLOAD / DELETE / GRANT_ACL）。</summary>
    public string Operation { get; } = operation;

    /// <summary>操作描述（面向审计报表可读，如「上传了文件」）。</summary>
    public string Description { get; } = description;

    /// <summary>
    /// 审计目标类型（如 <c>StorageObject</c>）；空表示无固定目标类型。
    /// </summary>
    public string? TargetType { get; init; }

    /// <summary>
    /// 目标 Id 绑定的 Action 参数名 / 路由键名（如 <c>id</c>）：
    /// 过滤器从 Action 参数或路由值解析，long 可解析时写入 <c>OperationLogEntry.TargetId</c>。
    /// </summary>
    public string? TargetIdParam { get; init; }
}
