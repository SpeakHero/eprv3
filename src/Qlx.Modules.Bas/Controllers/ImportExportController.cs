/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 02:10:00 +08:00 中国标准时间 星期一
 * 描述：Bas 导入导出 API（/api/bas/import-export）——模板清单 / 模板下载 / 导入 / 导出；
 *       控制器只挂租户上下文，模板级权限在服务层二次校验
 */

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.ImportExport;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Bas.Services.ImportExport;

namespace Qlx.Modules.Bas.Controllers;

/// <summary>
/// 导入导出控制器。模板清单与下载、导入与导出均要求租户上下文；
/// 资源级权限（供应商 / 客户 / 人员 / 分类）由服务层按模板编码二次校验。
/// </summary>
/// <param name="service">Bas 导入导出服务。</param>
[ApiController]
[Route("api/bas/import-export")]
public sealed class ImportExportController(IBasImportExportService service) : ControllerBase
{
    /// <summary>导入模板清单（仅导入模板，含扁平列元数据）。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>模板清单。</returns>
    [HttpGet("templates")]
    public async Task<ApiResponse<IReadOnlyList<ImportTemplateInfo>>> GetTemplates(CancellationToken ct)
        => ApiResponse<IReadOnlyList<ImportTemplateInfo>>.Ok(await service.ListTemplatesAsync(ct));

    /// <summary>下载标准导入模板（xlsx）。</summary>
    /// <param name="templateCode">导入模板编码。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>模板文件流。</returns>
    [HttpGet("templates/{templateCode}/download")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_IMPORT_TEMPLATE_DOWNLOAD", "下载了导入模板", TargetType = "ImportTemplate")]
    public async Task<IActionResult> DownloadTemplate(string templateCode, CancellationToken ct)
    {
        ImportTemplateFile file = await service.DownloadTemplateAsync(templateCode, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>按模板导入数据。</summary>
    /// <param name="templateCode">导入模板编码。</param>
    /// <param name="file">上传的 xlsx 文件。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>导入结果（总行数 / 成功行数 / 行级错误）。</returns>
    [HttpPost("import")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_IMPORT_EXECUTE", "导入了基础数据", TargetType = "ImportTemplate")]
    public async Task<ApiResponse<ImportResult>> Import(
        [FromForm] string templateCode, [FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw new ValidationException("file", "导入文件不能为空");
        }

        await using Stream stream = file.OpenReadStream();
        return ApiResponse<ImportResult>.Ok(await service.ImportAsync(templateCode, stream, ct));
    }

    /// <summary>按模板导出数据（敏感字段脱敏）。</summary>
    /// <param name="templateCode">导出模板编码。</param>
    /// <param name="status">状态筛选（可选，数值）。</param>
    /// <param name="personType">人员类型筛选（可选，数值）。</param>
    /// <param name="employStatus">在职状态筛选（可选，数值）。</param>
    /// <param name="keyword">关键字筛选（可选）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>导出文件流。</returns>
    [HttpGet("export")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_EXPORT_EXECUTE", "导出了基础数据", TargetType = "ExportTemplate")]
    public async Task<IActionResult> Export(
        string templateCode,
        int? status,
        int? personType,
        int? employStatus,
        string? keyword,
        CancellationToken ct)
    {
        Dictionary<string, object?> criteria = new(StringComparer.Ordinal)
        {
            ["status"] = status,
            ["personType"] = personType,
            ["employStatus"] = employStatus,
            ["keyword"] = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim(),
        };

        ExportResult result = await service.ExportAsync(templateCode, criteria, ct);
        return File(result.FileStream, result.ContentType, result.FileName);
    }
}
