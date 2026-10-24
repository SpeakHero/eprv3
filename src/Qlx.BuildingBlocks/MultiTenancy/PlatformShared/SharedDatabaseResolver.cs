/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 06:25:00 +08:00 中国标准时间 星期五
 * 描述：共享库（qlx_shared）连接解析器——固定返回共享库连接信息（平台写、租户只读），
 *       供 PlatformSharedDbContext 运行时与迁移计划复用（禁止第五库红线：glb_* 只进共享库）
 */

using Microsoft.Extensions.Options;

namespace Qlx.BuildingBlocks.MultiTenancy.PlatformShared
{
    /// <summary>
    /// 共享库连接解析器。Resolve 恒返回 <see cref="SharedDatabaseOptions"/> 组装的共享库连接，
    /// 不触达门户库租户路由表（共享库连接来自静态配置，无路由行）。
    /// </summary>
    /// <param name="sharedDatabase">共享库配置。</param>
    public sealed class SharedDatabaseResolver(IOptions<SharedDatabaseOptions> sharedDatabase) : ITenantDatabaseResolver
    {
        private readonly IOptions<SharedDatabaseOptions> _sharedDatabase = sharedDatabase;

        /// <inheritdoc />
        public TenantConnectionInfo Resolve(long tenantId)
            => _sharedDatabase.Value.ToConnectionInfo(tenantId);

        /// <inheritdoc />
        public Task PreloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <inheritdoc />
        public void Invalidate(long tenantId)
        {
        }
    }
}
