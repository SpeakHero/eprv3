/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 描述：模块许可查询契约，许可过滤中间件按此判断租户是否可用某模块
 */

namespace Qlx.Abstractions.Modules;

/// <summary>
/// 模块许可查询契约（由 Portal 模块实现）。许可过滤中间件据此对
/// <c>/api/{moduleCode}/...</c> 形态的请求做租户级许可判定，未授权一律 404。
/// </summary>
public interface IModuleLicenseService
{
    /// <summary>
    /// 判断指定租户是否已启用（拥有有效许可）指定模块。
    /// </summary>
    /// <param name="tenantId">租户内部标识（真实 long）。</param>
    /// <param name="moduleCode">模块编码（如 pur / sal / wms）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已启用返回 <see langword="true"/>；无许可或已停用返回 <see langword="false"/>。</returns>
    Task<bool> HasLicenseAsync(long tenantId, string moduleCode, CancellationToken cancellationToken);
}
