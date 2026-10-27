/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 02:00:00 +08:00 中国标准时间 星期一
 * 描述：Bas 导入导出服务契约——模板清单 / 模板下载 / 导入 / 导出，服务层二次校验模板权限
 */

using Qlx.Abstractions.ImportExport;

namespace Qlx.Modules.Bas.Services.ImportExport;

/// <summary>
/// Bas 导入导出服务。控制器只挂租户上下文，模板级权限在此二次校验（不得依赖前端或路由声明）。
/// </summary>
public interface IBasImportExportService
{
    /// <summary>导入模板清单（仅 <c>*.import</c> 模板，扁平列元数据）。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>模板清单。</returns>
    Task<IReadOnlyList<ImportTemplateInfo>> ListTemplatesAsync(CancellationToken ct);

    /// <summary>下载标准导入模板（xlsx 字节）。</summary>
    /// <param name="templateCode">导入模板编码。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>模板文件内容与文件名。</returns>
    Task<ImportTemplateFile> DownloadTemplateAsync(string templateCode, CancellationToken ct);

    /// <summary>按模板导入数据。</summary>
    /// <param name="templateCode">导入模板编码。</param>
    /// <param name="stream">上传文件流。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>导入结果。</returns>
    Task<ImportResult> ImportAsync(string templateCode, Stream stream, CancellationToken ct);

    /// <summary>按模板导出数据（敏感字段脱敏）。</summary>
    /// <param name="templateCode">导出模板编码。</param>
    /// <param name="criteria">导出条件。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>导出结果（含文件流）。</returns>
    Task<ExportResult> ExportAsync(
        string templateCode, IReadOnlyDictionary<string, object?> criteria, CancellationToken ct);
}

/// <summary>
/// 导入模板下载结果（字节内容 + 文件名 + MIME 类型）。
/// </summary>
/// <param name="Content">模板文件字节。</param>
/// <param name="FileName">下载文件名。</param>
/// <param name="ContentType">MIME 类型。</param>
public sealed record ImportTemplateFile(byte[] Content, string FileName, string ContentType);
