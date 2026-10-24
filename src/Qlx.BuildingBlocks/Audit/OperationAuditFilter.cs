/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 12:30:00 +08:00 中国标准时间 星期五
 * 描述：方法级审计过滤器——[AuditOperation] 标注方法执行后写操作日志（成功 / 失败双留痕），无标注方法零开销跳过
 */

using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Tenancy;

namespace Qlx.BuildingBlocks.Audit;

/// <summary>
/// 方法级操作审计过滤器。规则：
/// <list type="bullet">
/// <item>Action 方法标注 <see cref="AuditOperationAttribute"/> 且执行成功 → 写操作日志
/// （含模块 / 操作 / 描述 / TargetType / TargetId / 操作人 / 租户 / IP 元数据）。</item>
/// <item>执行异常 → 写失败审计（操作码追加 <see cref="FailedOperationSuffix"/>），异常原样上抛由
/// 全局异常中间件包装响应——保证验收「成功 / 异常分支均留痕」，且失败动作绝不被记为成功。</item>
/// <item>未标注 → 直接放行，零开销。</item>
/// </list>
/// </summary>
public sealed partial class OperationAuditFilter(
    IAuditService auditService,
    ICurrentUserContext userContext,
    ITenantContext tenantContext,
    ILogger<OperationAuditFilter> logger) : IAsyncActionFilter
{
    /// <summary>失败审计操作码后缀（追加在原操作编码之后，如 <c>DELETE:FAILED</c>）。</summary>
    public const string FailedOperationSuffix = ":FAILED";

    private readonly IAuditService _auditService = auditService;
    private readonly ICurrentUserContext _userContext = userContext;
    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly ILogger<OperationAuditFilter> _logger = logger;

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var attribute = ResolveAttribute(context);
        if (attribute is null)
        {
            await next().ConfigureAwait(false);
            return;
        }

        var executed = await next().ConfigureAwait(false);
        var failed = executed.Exception is not null;
        var operatedAt = DateTimeOffset.UtcNow;

        var entry = new OperationLogEntry(
            EventId: 0,
            ModuleCode: attribute.Module,
            Operation: failed ? attribute.Operation + FailedOperationSuffix : attribute.Operation,
            Description: failed
                ? $"{attribute.Description}（执行失败：{executed.Exception?.GetType().Name}）"
                : attribute.Description,
            OperatedBy: _userContext.UserId ?? "anonymous",
            OperatedAt: operatedAt,
            TenantId: _tenantContext.TenantId ?? 0,
            TargetType: attribute.TargetType,
            TargetId: ResolveTargetId(attribute, context),
            Metadata: BuildMetadata(context.HttpContext));

        // 审计失败不阻断业务（与 AuditSaveChangesInterceptor 一致）；异常分支先留失败痕再原样上抛
        try
        {
            await _auditService.CaptureOperationAsync(entry, context.HttpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAuditWriteFailed(attribute.Module, attribute.Operation, ex);
        }

        // 失败时不处理 Exception（保持原样上抛，由全局异常中间件包装响应）
    }

    /// <summary>解析当前 Action 上的 <see cref="AuditOperationAttribute"/>（未标注返回 null）。</summary>
    private static AuditOperationAttribute? ResolveAttribute(ActionExecutingContext context)
        => context.ActionDescriptor is ControllerActionDescriptor descriptor
            ? descriptor.MethodInfo.GetCustomAttributes(inherit: true)
                .OfType<AuditOperationAttribute>()
                .FirstOrDefault()
            : null;

    /// <summary>
    /// 解析审计目标 Id：按特性声明的参数名从 Action 参数 → 路由值顺序查找，
    /// long 可解析时返回（混淆串由全局模型绑定器已解码为 long，此处仅兜底纯数字路由值）。
    /// </summary>
    private static long? ResolveTargetId(AuditOperationAttribute attribute, ActionExecutingContext context)
    {
        if (attribute.TargetIdParam is not { } paramName)
        {
            return null;
        }

        var raw = context.ActionArguments.TryGetValue(paramName, out var argumentValue)
            ? argumentValue
            : context.RouteData.Values.TryGetValue(paramName, out var routeValue) ? routeValue : null;
        return raw switch
        {
            long idValue => idValue,
            int intValue => intValue,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>构建请求元数据（IP / HTTP 方法 / 路径，JSON 结构化）。</summary>
    private static string BuildMetadata(HttpContext httpContext)
    {
        var metadata = new
        {
            ip = httpContext.Connection.RemoteIpAddress?.ToString(),
            method = httpContext.Request.Method,
            path = httpContext.Request.Path.Value,
        };
        return JsonSerializer.Serialize(metadata, QlxJsonDefaults.Default);
    }

    [LoggerMessage(
        Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "方法级审计写入失败（模块 {Module}，操作 {Operation}），不阻断业务")]
    private partial void LogAuditWriteFailed(string module, string operation, Exception exception);
}
