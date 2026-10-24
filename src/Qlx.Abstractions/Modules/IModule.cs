/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：模块装配契约，宿主在启动期发现并调用以注册模块服务与清单
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Qlx.Abstractions.Modules;

/// <summary>
/// 模块装配契约。业务模块实现本接口并在程序集中导出，
/// 宿主启动时经程序集扫描发现并依次装配。
/// 模块只允许引用 Qlx.Abstractions 与 Qlx.BuildingBlocks。
/// </summary>
public interface IModule
{
    /// <summary>
    /// 模块不可变清单。
    /// </summary>
    ModuleManifest Manifest { get; }

    /// <summary>
    /// 注册模块内部服务。模块服务生命周期必须按多租户要求注册为 Scoped。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">分层加载后的配置根。</param>
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}
