/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 22:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 22:30:00 +08:00 中国标准时间 星期四
 * 描述：模块 Manifest 同步服务契约，宿主启动期将各业务模块清单物化到 Portal 库
 */

using Qlx.Abstractions.Modules;

namespace Qlx.BuildingBlocks.Modules;

/// <summary>
/// 模块 Manifest → Portal 库同步服务。
/// 宿主启动期扫描并装配完所有 <see cref="IModule"/> 实现后，
/// 通过本服务将模块清单中的 ModuleDefinition / PermissionDefinition / ModuleMenu / NavigationGroup
/// 等元数据一次性物化到 Portal 管理库；拦截器（<see cref="ManifestDefinitionReadOnlyGuardInterceptor"/>）
/// 在非同步上下文会阻止对这些表的直接写入，以保证 Manifest 来源唯一。
/// </summary>
public interface IModuleManifestSyncService
{
    /// <summary>
    /// 同步所有模块 Manifest 到 Portal 库。
    /// 内部将循环调用 <see cref="SyncModuleAsync"/>，使用事务保证单次同步的原子性。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SyncAllAsync(CancellationToken ct = default);

    /// <summary>
    /// 同步单个模块的 Manifest 到 Portal 库。
    /// 内部会设置 <see cref="ManifestDefinitionReadOnlyGuardInterceptor.BypassGuard"/> 为 true，
    /// 让拦截器放行本次写入。
    /// </summary>
    /// <param name="moduleInstance">已装配的模块实例。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SyncModuleAsync(IModule moduleInstance, CancellationToken ct = default);
}
