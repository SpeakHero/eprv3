/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 12:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 12:30:00 +08:00 中国标准时间 星期三
 * 描述：系统参数管理端点（阶段 1D）：RESTful CRUD，CancellationToken 由框架绑定 RequestAborted
 */

using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;
using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Portal.Dtos;
using Qlx.Modules.Portal.Services;

namespace Qlx.Modules.Portal.Controllers;

/// <summary>
/// 系统参数管理端点（/api/portal/system-parameters）。
/// <para>
/// CancellationToken 传播约定（global-csharp-standard §6.13）：Action 的
/// <see cref="CancellationToken"/> 参数由 ASP.NET Core 模型绑定自动关联
/// <c>HttpContext.RequestAborted</c>（框架层的"自动注入"，无需过滤器封装），
/// 调用服务时<b>原样显式透传</b>——客户端断开 / 请求超时时及时终止数据库 IO，
/// 释放数据库连接，避免无效慢查询占用 DB 资源。禁止 <c>= default</c> 默认值。
/// </para>
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
[Route("api/portal/system-parameters")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class SystemParametersController(ISystemParameterAdminService service) : ControllerBase
{
    /// <summary>分页查询系统参数（关键字过滤参数键 / 说明）。</summary>
    /// <param name="request">分页与过滤参数（query）。</param>
    /// <param name="cancellationToken">请求中止令牌（框架绑定 RequestAborted）。</param>
    [HttpGet]
    public async Task<ApiResponse<PagedResult<SystemParameterItem>>> Query(
        [FromQuery] SystemParameterQueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = await service.QueryAsync(request, cancellationToken).ConfigureAwait(false);
        return ApiResponse<PagedResult<SystemParameterItem>>.Ok(page);
    }

    /// <summary>按参数键查询单个参数。</summary>
    /// <param name="paramKey">参数键（路由）。</param>
    /// <param name="cancellationToken">请求中止令牌（框架绑定 RequestAborted）。</param>
    [HttpGet("{paramKey}")]
    public async Task<ApiResponse<SystemParameterItem>> Get(
        string paramKey, CancellationToken cancellationToken)
    {
        var item = await service.GetAsync(paramKey, cancellationToken).ConfigureAwait(false);
        return ApiResponse<SystemParameterItem>.Ok(item);
    }

    /// <summary>保存参数（Upsert：存在即更新，不存在即新建）。</summary>
    /// <param name="paramKey">参数键（路由）。</param>
    /// <param name="request">参数值与说明（body）。</param>
    /// <param name="cancellationToken">请求中止令牌（框架绑定 RequestAborted）。</param>
    [HttpPut("{paramKey}")]
    public async Task<ApiResponse<SystemParameterItem>> Save(
        string paramKey, [FromBody] SaveSystemParameterRequest request, CancellationToken cancellationToken)
    {
        var item = await service.UpsertAsync(paramKey, request, cancellationToken).ConfigureAwait(false);
        return ApiResponse<SystemParameterItem>.Ok(item, "保存成功");
    }

    /// <summary>删除参数。</summary>
    /// <param name="paramKey">参数键（路由）。</param>
    /// <param name="cancellationToken">请求中止令牌（框架绑定 RequestAborted）。</param>
    [HttpDelete("{paramKey}")]
    public async Task<ApiResponse> Delete(string paramKey, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(paramKey, cancellationToken).ConfigureAwait(false);
        return ApiResponse.Ok("删除成功");
    }
}
