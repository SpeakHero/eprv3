/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 描述：缓存键规范工具，统一「租户码:模块前缀:业务键」格式
 */

namespace Qlx.BuildingBlocks.Caching;

/// <summary>
/// 缓存键规范工具。全平台缓存键统一格式：
/// 租户级 <c>tenant:{tenantId}:{modulePrefix}:{businessKey}</c>（使用 <see cref="BuildTenantKey"/>）；
/// 门户级保留租户码 <c>_portal</c>（使用 <see cref="Build"/>）。
/// </summary>
public static class QlxCacheKey
{
    /// <summary>
    /// 门户级（无租户）缓存键使用的保留租户码。
    /// </summary>
    public const string PortalTenantCode = "_portal";

    /// <summary>
    /// 租户级缓存键固定前缀。
    /// </summary>
    public const string TenantKeyPrefix = "tenant";

    /// <summary>
    /// Token 黑名单键前缀（架构规格 6.6.4；写入侧 Portal 登出/吊销，读取侧认证中间件共用）。
    /// </summary>
    public const string TokenBlacklistPrefix = "token:blacklist:";

    /// <summary>
    /// 构建租户级缓存键：<c>tenant:{tenantId}:{modulePrefix}:{businessKey}</c>。
    /// 模块禁止自行拼接租户缓存键，统一经本方法生成，避免键格式漂移与跨租户串读。
    /// </summary>
    /// <param name="tenantId">租户 ID（必须大于 0）。</param>
    /// <param name="modulePrefix">模块前缀（如 pur、sal）。</param>
    /// <param name="businessKey">业务键。</param>
    /// <returns>规范租户缓存键。</returns>
    /// <exception cref="ArgumentOutOfRangeException">tenantId 不大于 0。</exception>
    /// <exception cref="ArgumentException">模块前缀为空或包含分隔符 ':'；业务键为空。</exception>
    public static string BuildTenantKey(long tenantId, string modulePrefix, string businessKey)
    {
        if (tenantId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tenantId), tenantId, "租户 ID 必须大于 0");
        }

        ValidateSegment(modulePrefix, nameof(modulePrefix));

        if (string.IsNullOrWhiteSpace(businessKey))
        {
            throw new ArgumentException("业务键不能为空", nameof(businessKey));
        }

        return $"{TenantKeyPrefix}:{tenantId}:{modulePrefix}:{businessKey}";
    }

    /// <summary>
    /// 构建门户 / 平台级缓存键：<c>_portal:{modulePrefix}:{businessKey}</c>（保留租户码 <see cref="PortalTenantCode"/>）。
    /// 平台侧无租户码的缓存统一经本方法生成（规则文件 v1.2 C-1-2）。
    /// </summary>
    /// <param name="modulePrefix">模块前缀（如 pur、sal、msg）。</param>
    /// <param name="businessKey">业务键。</param>
    /// <returns>规范门户级缓存键。</returns>
    /// <exception cref="ArgumentException">模块前缀为空或包含分隔符 ':'；业务键为空。</exception>
    public static string BuildPortalKey(string modulePrefix, string businessKey)
    {
        ValidateSegment(modulePrefix, nameof(modulePrefix));

        if (string.IsNullOrWhiteSpace(businessKey))
        {
            throw new ArgumentException("业务键不能为空", nameof(businessKey));
        }

        return $"{PortalTenantCode}:{modulePrefix}:{businessKey}";
    }

    /// <summary>
    /// 按规范构建缓存键。
    /// </summary>
    /// <param name="tenantCode">租户码；门户级传 <see cref="PortalTenantCode"/>。</param>
    /// <param name="modulePrefix">模块前缀（如 pur、sal）。</param>
    /// <param name="businessKey">业务键。</param>
    /// <returns>规范缓存键。</returns>
    /// <exception cref="ArgumentException">任一段为空或包含分隔符 ':' 时抛出。</exception>
    [Obsolete("租户级缓存键统一使用 BuildTenantKey(tenantId, modulePrefix, businessKey)；本方法仅门户级缓存过渡使用。")]
    public static string Build(string tenantCode, string modulePrefix, string businessKey)
    {
        ValidateSegment(tenantCode, nameof(tenantCode));
        ValidateSegment(modulePrefix, nameof(modulePrefix));

        if (string.IsNullOrWhiteSpace(businessKey))
        {
            throw new ArgumentException("业务键不能为空", nameof(businessKey));
        }

        return $"{tenantCode}:{modulePrefix}:{businessKey}";
    }

    private static void ValidateSegment(string segment, string paramName)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            throw new ArgumentException("键段不能为空", paramName);
        }

        if (segment.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("键段不允许包含分隔符 ':'", paramName);
        }
    }
}
