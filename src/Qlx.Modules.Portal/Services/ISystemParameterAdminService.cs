/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 12:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 12:30:00 +08:00 中国标准时间 星期三
 * 描述：系统参数管理服务契约：参数在线维护（阶段 1D）写路径 + 管理端读路径
 */

using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Portal.Dtos;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 系统参数管理服务契约（阶段 1D 门户管理端第一批）。
/// <para>
/// CancellationToken 传播约定（global-csharp-standard §6.13）：服务层为业务编排层，
/// 公开异步方法签名 <b>必须</b> 携带 <c>CancellationToken cancellationToken</c>（禁止
/// <c>= default</c>），向下原样透传给仓储，向上由 Controller 从 <c>HttpContext.RequestAborted</c>
/// 传入——客户端断开 / 请求超时时及时终止数据库 IO，释放连接，避免无效慢查询占用 DB 资源。
/// </para>
/// </summary>
public interface ISystemParameterAdminService
{
    /// <summary>分页查询参数。</summary>
    Task<PagedResult<SystemParameterItem>> QueryAsync(SystemParameterQueryRequest request, CancellationToken cancellationToken);

    /// <summary>按键查单个参数（不存在抛 <see cref="Qlx.Abstractions.Exceptions.NotFoundException"/>）。</summary>
    Task<SystemParameterItem> GetAsync(string paramKey, CancellationToken cancellationToken);

    /// <summary>保存参数（Upsert：存在即更新，不存在即新建）；成功后失效对应缓存键。</summary>
    Task<SystemParameterItem> UpsertAsync(string paramKey, SaveSystemParameterRequest request, CancellationToken cancellationToken);

    /// <summary>删除参数（不存在抛 <see cref="Qlx.Abstractions.Exceptions.NotFoundException"/>）；成功后失效对应缓存键。</summary>
    Task DeleteAsync(string paramKey, CancellationToken cancellationToken);
}
