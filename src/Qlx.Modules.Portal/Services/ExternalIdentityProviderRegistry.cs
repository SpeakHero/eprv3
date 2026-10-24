/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：外部身份提供方运行期注册表（单例内存快照），启动期一次性加载，未配置即空集
 */

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 外部身份提供方运行期注册表（Singleton）。
/// <para>
/// 启动期由 <see cref="ExternalIdentityProviderLoader"/> 一次性装载；配置变更后可调用
/// <see cref="Replace"/> 原子替换快照。空表表示「未启用任何外部登录」，不视为错误。
/// </para>
/// </summary>
public sealed class ExternalIdentityProviderRegistry
{
    private volatile IReadOnlyList<ExternalIdentityProviderDescriptor> _providers = [];

    /// <summary>当前启用中的提供方（按展示排序）。</summary>
    public IReadOnlyList<ExternalIdentityProviderDescriptor> Providers => _providers;

    /// <summary>按编码查找单条配置。</summary>
    /// <param name="providerCode">提供方编码。</param>
    /// <returns>描述符；不存在时为 <c>null</c>。</returns>
    public ExternalIdentityProviderDescriptor? Find(string providerCode)
        => string.IsNullOrWhiteSpace(providerCode)
            ? null
            : _providers.FirstOrDefault(p => string.Equals(p.ProviderCode, providerCode, StringComparison.Ordinal));

    /// <summary>原子替换快照（返回替换后的条目数）。</summary>
    /// <param name="providers">新的提供方集合。</param>
    /// <returns>替换后的条目数。</returns>
    public int Replace(IReadOnlyList<ExternalIdentityProviderDescriptor> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = [.. providers];
        return _providers.Count;
    }
}
