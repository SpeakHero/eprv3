/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：外部身份提供方配置来源契约（启动期加载 + 配置变更后按需刷新）
 */

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 外部身份提供方配置来源：读取启用中的配置行并解密凭据，返回可直接装配的描述符。
/// </summary>
public interface IExternalIdentityProviderSource
{
    /// <summary>
    /// 读取全部启用中的提供方（按展示排序升序）；无配置时返回空集合，不抛异常。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<ExternalIdentityProviderDescriptor>> GetEnabledAsync(CancellationToken cancellationToken);
}
