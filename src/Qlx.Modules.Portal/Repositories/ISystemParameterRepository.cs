/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 12:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 12:30:00 +08:00 中国标准时间 星期三
 * 描述：系统参数仓储契约：por_parameters 持久化读写（仅 Portal 模块内使用）
 */

using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Portal.Domain.Configuration;

namespace Qlx.Modules.Portal.Repositories;

/// <summary>
/// 系统参数仓储契约。
/// <para>
/// CancellationToken 传播约定（global-csharp-standard §6.13）：仓储为数据访问边界，
/// 所有公开异步方法 <b>必须</b> 携带调用方传入的令牌（禁止 <c>= default</c>），
/// 并透传给每一个 EF Core 异步调用——客户端断开 / 请求超时时及时终止数据库 IO、
/// 释放连接，避免无效慢查询占用 DB 资源。
/// </para>
/// </summary>
public interface ISystemParameterRepository
{
    /// <summary>按参数键查单个参数（读模型，AsNoTracking）。</summary>
    Task<SystemParameter?> FindByKeyAsync(string paramKey, CancellationToken cancellationToken);

    /// <summary>按参数键查单个参数（跟踪态，供写路径修改后 SaveChanges）。</summary>
    Task<SystemParameter?> FindTrackedByKeyAsync(string paramKey, CancellationToken cancellationToken);

    /// <summary>分页查询参数（可选关键字过滤 ParamKey，AsNoTracking）。</summary>
    Task<PagedResult<SystemParameter>> QueryAsync(string? keyword, PagedRequest request, CancellationToken cancellationToken);

    /// <summary>新增参数（调用方负责 SaveChanges）。</summary>
    Task AddAsync(SystemParameter parameter, CancellationToken cancellationToken);

    /// <summary>持久化对跟踪实体所做的修改（Update 后调用）。</summary>
    Task UpdateAsync(SystemParameter parameter, CancellationToken cancellationToken);

    /// <summary>删除参数，返回是否实际删除。</summary>
    Task<bool> DeleteAsync(string paramKey, CancellationToken cancellationToken);
}
