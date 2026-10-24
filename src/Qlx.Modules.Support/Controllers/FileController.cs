/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 03:20:00 +08:00 中国标准时间 星期五
 * 描述：Support 文件中心 API Controller（/api/support/files）——Controller 层仅参数提取 + Service 调用 + 响应封装。
 *       路由/查询参数中的业务 Id 声明为 long，由全局 HashidsLongModelBinder 自动解码（Controller 不做编解码）。
 *       全部文件能力经 ISupportFileService 任务化提交（S18/D22 红线），控制器不直接触碰存储后端。
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Files;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Support.Services.Files;

namespace Qlx.Modules.Support.Controllers;

/// <summary>
/// 文件中心控制器。所有端点需租户级 Token（OpenIddict Validation Scheme），
/// 路由经模块许可过滤（support.file.* 权限点）。
/// </summary>
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
[ApiController]
[Route("api/support/files")]
public sealed class FileController(
    ISupportFileService fileService,
    IFileAclService aclService) : ControllerBase
{
    // ============ 上传 / 查询 ============

    /// <summary>上传文件（multipart/form-data；bizModule/docType 驱动归类规则 R2）。</summary>
    /// <param name="file">上传文件。</param>
    /// <param name="bizModule">业务模块编码（可空，未命中规则落 unclassified）。</param>
    /// <param name="docType">单据类型编码（可空）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("upload")]
    [AuditOperation(QlxModuleCodes.Support, "FILE_UPLOAD", "上传了文件", TargetType = "StorageObject")]
    [ProducesResponseType(typeof(ApiResponse<SupportFileUploadResult>), StatusCodes.Status200OK)]
    public async Task<ApiResponse<SupportFileUploadResult>> Upload(
        IFormFile file, [FromQuery] string? bizModule, [FromQuery] string? docType, CancellationToken ct)
    {
        var stream = file.OpenReadStream();
        await using (stream.ConfigureAwait(false))
        {
            var request = new FileUploadRequest(
                stream, file.FileName,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                file.Length);
            return ApiResponse<SupportFileUploadResult>.Ok(
                await fileService.UploadAsync(request, bizModule, docType, ct).ConfigureAwait(false));
        }
    }

    /// <summary>查询文件元数据（需 Read 权限）。</summary>
    /// <param name="id">文件 Id（混淆串自动解码）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("metadata/{id}")]
    public async Task<ApiResponse<SupportFileMetadata>> GetMetadata(long id, CancellationToken ct)
        => ApiResponse<SupportFileMetadata>.Ok(await fileService.GetMetadataAsync(id, ct));

    /// <summary>目录列表（parentId 空=根目录；keyword 模式全租户按名检索）。</summary>
    /// <param name="parentId">父目录 Id（可空）。</param>
    /// <param name="keyword">关键字（可空）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("list")]
    public async Task<ApiResponse<IReadOnlyList<SupportFileMetadata>>> List(
        [FromQuery] long? parentId, [FromQuery] string? keyword, CancellationToken ct)
        => ApiResponse<IReadOnlyList<SupportFileMetadata>>.Ok(await fileService.ListAsync(parentId, keyword, ct));

    // ============ 下载 / 预览 ============

    /// <summary>下载文件（需 Read 权限；流式返回原始内容）。</summary>
    /// <param name="id">文件 Id。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("download/{id}")]
    public async Task<IActionResult> Download(long id, CancellationToken ct)
    {
        var content = await fileService.OpenDownloadAsync(id, ct).ConfigureAwait(false);
        return new FileStreamResult(content.Content, content.ContentType)
        {
            FileDownloadName = content.FileName,
        };
    }

    /// <summary>在线预览（2B 与下载同流；2C 接入格式转换预览）。</summary>
    /// <param name="id">文件 Id。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("preview/{id}")]
    public async Task<IActionResult> Preview(long id, CancellationToken ct)
    {
        var content = await fileService.OpenPreviewAsync(id, ct).ConfigureAwait(false);
        return new FileStreamResult(content.Content, content.ContentType)
        {
            EnableRangeProcessing = false,
        };
    }

    // ============ 回收站 ============

    /// <summary>回收站列表（管理员全量，普通用户仅本人删除项）。</summary>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("recycle/list")]
    public async Task<ApiResponse<IReadOnlyList<SupportFileMetadata>>> RecycleList(CancellationToken ct)
        => ApiResponse<IReadOnlyList<SupportFileMetadata>>.Ok(await fileService.ListRecycleAsync(ct));

    /// <summary>软删进回收站（需 Delete 权限；目录须无存活子级）。</summary>
    /// <param name="id">文件/目录 Id。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpDelete("{id}")]
    [AuditOperation(QlxModuleCodes.Support, "FILE_DELETE", "删除了文件（移入回收站）", TargetType = "StorageObject", TargetIdParam = "id")]
    public async Task<ApiResponse<SupportFileMetadata>> Delete(long id, CancellationToken ct)
        => ApiResponse<SupportFileMetadata>.Ok(await fileService.DeleteAsync(id, ct));

    /// <summary>从回收站还原（需 Write 权限）。</summary>
    /// <param name="id">文件 Id。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("{id}/restore")]
    [AuditOperation(QlxModuleCodes.Support, "FILE_RESTORE", "从回收站还原了文件", TargetType = "StorageObject", TargetIdParam = "id")]
    public async Task<ApiResponse<SupportFileMetadata>> Restore(long id, CancellationToken ct)
        => ApiResponse<SupportFileMetadata>.Ok(await fileService.RestoreAsync(id, ct));

    // ============ ACL 授权 ============

    /// <summary>查询 ACL 条目（需 Manage 权限由服务层校验）。</summary>
    /// <param name="id">文件 Id。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("acl/{id}")]
    public async Task<ApiResponse<IReadOnlyList<FileAclEntryInfo>>> ListAcl(long id, CancellationToken ct)
        => ApiResponse<IReadOnlyList<FileAclEntryInfo>>.Ok(
            await aclService.ListAclAsync(id.ToString(System.Globalization.CultureInfo.InvariantCulture), ct));

    /// <summary>授予 ACL 权限（file.acl.set grant，需 Manage 权限）。</summary>
    /// <param name="id">文件 Id。</param>
    /// <param name="payload">授权入参。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("acl/{id}/grant")]
    [AuditOperation(QlxModuleCodes.Support, "FILE_ACL_GRANT", "授予了文件 ACL 权限", TargetType = "StorageObject", TargetIdParam = "id")]
    public async Task<ApiResponse> GrantAcl(
        long id, [FromBody] FileAclPayload payload, CancellationToken ct)
    {
        await aclService.GrantAsync(
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (FileSubjectType)payload.SubjectType, payload.SubjectId,
            (FilePermission)payload.Permission, ct).ConfigureAwait(false);
        return ApiResponse.Ok();
    }

    /// <summary>撤销 ACL 权限（file.acl.set revoke，需 Manage 权限；撤销该主体全部 Allow 授权）。</summary>
    /// <param name="id">文件 Id。</param>
    /// <param name="payload">撤销入参（SubjectType/SubjectId 定位条目）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("acl/{id}/revoke")]
    [AuditOperation(QlxModuleCodes.Support, "FILE_ACL_REVOKE", "撤销了文件 ACL 权限", TargetType = "StorageObject", TargetIdParam = "id")]
    public async Task<ApiResponse> RevokeAcl(
        long id, [FromBody] FileAclPayload payload, CancellationToken ct)
    {
        await aclService.RevokeAsync(
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (FileSubjectType)payload.SubjectType, payload.SubjectId, ct).ConfigureAwait(false);
        return ApiResponse.Ok();
    }
}

/// <summary>ACL 授予/撤销入参（枚举以 int 传输，前端按字典映射）。</summary>
/// <param name="SubjectType">主体类型（1=用户 / 2=角色 / 3=组织）。</param>
/// <param name="SubjectId">主体 Id（<c>*</c> 表示全部主体）。</param>
/// <param name="Permission">权限位掩码（Read=1 / Write=2 / Delete=4 / Manage=8）。</param>
public sealed record FileAclPayload(int SubjectType, string SubjectId, int Permission);
