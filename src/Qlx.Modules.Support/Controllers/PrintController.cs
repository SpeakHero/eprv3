/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 14:10:00 +08:00 中国标准时间 星期五
 * 描述：打印中心 API Controller（/api/support/printing）——Controller 层仅参数提取 + Service 调用 + 响应封装。
 *       路由参数中的业务 Id 声明为 long，由全局 HashidsLongModelBinder 自动解码（Controller 不做编解码）。
 */

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Abstractions.Printing;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Services.Printing;

namespace Qlx.Modules.Support.Controllers;

/// <summary>
/// 打印中心控制器。所有端点需租户级 Token（OpenIddict Validation Scheme），
/// 路由经模块许可过滤（support.print.* 权限点）。
/// </summary>
[ApiController]
[Route("api/support/printing/templates")]
public sealed class PrintController(
    ISupportPrintTemplateService printService,
    IPrintTemplateService renderService) : ControllerBase
{
    // ============ 查询 ============

    /// <summary>模板清单（全租户，按编码 + 版本排序）。</summary>
    /// <param name="ct">取消令牌。</param>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<PrintTemplateView>>> List(CancellationToken ct)
        => ApiResponse<IReadOnlyList<PrintTemplateView>>.Ok(await printService.ListAsync(ct));

    /// <summary>按模板编码查询最新版本定义。</summary>
    /// <param name="code">模板编码。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("code/{code}")]
    public async Task<ApiResponse<PrintTemplateView?>> GetByCode(string code, CancellationToken ct)
        => ApiResponse<PrintTemplateView?>.Ok(await printService.GetByCodeAsync(code, ct));

    // ============ 模板维护 ============

    /// <summary>新建打印模板（初始 Draft；编码唯一）。</summary>
    /// <param name="payload">创建入参。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Support, "PRINT_TEMPLATE_CREATE", "新建了打印模板", TargetType = "PrintTemplate")]
    [ProducesResponseType(typeof(ApiResponse<PrintTemplateView>), StatusCodes.Status200OK)]
    public async Task<ApiResponse<PrintTemplateView>> Create(
        [FromBody] PrintTemplateCreatePayload payload, CancellationToken ct)
        => ApiResponse<PrintTemplateView>.Ok(await printService.CreateAsync(
            new PrintTemplateCreateRequest(payload.TemplateCode, payload.Name, payload.BizType, payload.ContentHtml, payload.Css),
            ct));

    /// <summary>更新打印模板（内容变更版本自增）。</summary>
    /// <param name="id">模板 Id（混淆串自动解码）。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPut("{id}")]
    [AuditOperation(QlxModuleCodes.Support, "PRINT_TEMPLATE_UPDATE", "修改了打印模板", TargetType = "PrintTemplate", TargetIdParam = "id")]
    public async Task<ApiResponse<PrintTemplateView>> Update(
        long id, [FromBody] PrintTemplateUpdatePayload payload, CancellationToken ct)
        => ApiResponse<PrintTemplateView>.Ok(await printService.UpdateAsync(
            id, new PrintTemplateUpdateRequest(payload.Name, payload.BizType, payload.ContentHtml, payload.Css), ct));

    /// <summary>启用 / 停用打印模板（仅启用态承接渲染）。</summary>
    /// <param name="id">模板 Id（混淆串自动解码）。</param>
    /// <param name="payload">状态入参（Status 枚举 int：0=Draft / 1=Enabled / 2=Disabled）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("{id}/status")]
    [AuditOperation(QlxModuleCodes.Support, "PRINT_TEMPLATE_STATUS", "变更了打印模板启停状态", TargetType = "PrintTemplate", TargetIdParam = "id")]
    public async Task<ApiResponse> SetStatus(
        long id, [FromBody] PrintTemplateStatusPayload payload, CancellationToken ct)
    {
        await printService.SetStatusAsync(id, (PrintTemplateStatus)payload.Status, ct);
        return ApiResponse.Ok();
    }

    // ============ 渲染预览 ============

    /// <summary>
    /// 渲染打印模板为 HTML（启用态模板；请求体即模板变量字典，
    /// 值支持字符串 / 数值 / 布尔 / null / 嵌套对象 / 数组；双花括号 qrcode: 占位符渲染为内嵌 base64 二维码）。
    /// </summary>
    /// <param name="code">模板编码。</param>
    /// <param name="variables">模板变量（JSON 对象，键与模板占位符一致，大小写敏感）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("code/{code}/render")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public async Task<ApiResponse<string>> Render(string code, [FromBody] JsonElement variables, CancellationToken ct)
        => ApiResponse<string>.Ok(await renderService.RenderAsync(code, ConvertVariables(variables), ct));

    /// <summary>将请求体 JSON 对象展开为模板变量字典（嵌套对象转字典、数组转数组，标量取原始类型）。</summary>
    private static Dictionary<string, object?> ConvertVariables(JsonElement element)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = ConvertValue(property.Value);
        }

        return result;
    }

    /// <summary>JSON 元素 → 模板变量值（对象递归字典 / 数组递归数组 / 标量取原始类型）。</summary>
    private static object? ConvertValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var integerValue) ? integerValue : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => ConvertVariables(value),
        JsonValueKind.Array => value.EnumerateArray().Select(static item => ConvertValue(item)).ToArray(),
        _ => value.GetRawText(),
    };
}

/// <summary>打印模板创建入参。</summary>
/// <param name="TemplateCode">模板编码（租户内唯一）。</param>
/// <param name="Name">模板名称。</param>
/// <param name="BizType">业务类型编码（承载 2A 契约 ModuleCode 语义）。</param>
/// <param name="ContentHtml">模板 HTML（Scriban 占位 + qrcode: 占位）。</param>
/// <param name="Css">附加样式（可空）。</param>
public sealed record PrintTemplateCreatePayload(string TemplateCode, string Name, string BizType, string ContentHtml, string? Css);

/// <summary>打印模板更新入参。</summary>
/// <param name="Name">模板名称。</param>
/// <param name="BizType">业务类型编码。</param>
/// <param name="ContentHtml">模板 HTML。</param>
/// <param name="Css">附加样式（可空）。</param>
public sealed record PrintTemplateUpdatePayload(string Name, string BizType, string ContentHtml, string? Css);

/// <summary>打印模板启停入参。</summary>
/// <param name="Status">目标状态（枚举 int：0=Draft / 1=Enabled / 2=Disabled）。</param>
public sealed record PrintTemplateStatusPayload(int Status);
