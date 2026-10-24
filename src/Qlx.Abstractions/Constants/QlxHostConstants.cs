/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 描述：宿主级协议常量（请求头名、健康检查路径、API 路由前缀），全局唯一入口
 */

namespace Qlx.Abstractions.Constants;

/// <summary>
/// 全局统一的宿主协议常量。跨模块共享，禁止在中间件 / 控制器 / 宿主内重复声明。
/// </summary>
public static class QlxHostConstants
{
    /// <summary>租户上下文请求头名（值对外为 Hashids 混淆串）。</summary>
    public const string TenantHeaderName = "X-Tenant-Id";

    /// <summary>健康检查端点路径。</summary>
    public const string HealthEndpointPath = "/health";

    /// <summary>API 路由固定前缀段。</summary>
    public const string ApiRoutePrefix = "api";

    /// <summary>租户实体/表上的租户归属属性名（EF 模型与 RLS 巡检共用约定）。</summary>
    public const string TenantEntityPropertyName = "TenantId";

    /// <summary>标准认证请求头名。</summary>
    public const string AuthorizationHeaderName = "Authorization";

    /// <summary>文件下载响应头名。</summary>
    public const string ContentDispositionHeaderName = "Content-Disposition";
}
