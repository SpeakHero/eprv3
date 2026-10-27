/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 描述：当前用户上下文接口，审计拦截器从这里读 CreatedBy/UpdatedBy
 */

namespace Qlx.Abstractions.Domain;

/// <summary>
/// 当前请求的用户上下文。由认证中间件填充请求作用域实例，
/// 供审计拦截器、业务逻辑读取当前操作人标识。
/// </summary>
public interface ICurrentUserContext
{
    /// <summary>当前用户唯一标识（UserId），未认证为 null。</summary>
    string? UserId { get; }

    /// <summary>当前用户登录账号（login_account），未认证为 null。</summary>
    string? UserName { get; }

    /// <summary>当前用户显示名称，未认证为 null。</summary>
    string? UserDisplayName { get; }
    /// <summary>获取当前用户IP。</summary>
    string? CurrentUserIp { get;}

    /// <summary>当前请求是否来自匿名用户。</summary>
    bool IsAuthenticated { get; }
}
