/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 08:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 08:30:00 中国标准时间 星期三
 * 描述：门户系统参数服务：Cache-Aside 读取 por_parameters（键 _portal:params:{paramKey}）
 */

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Modules;
using Qlx.BuildingBlocks.Caching;
using Qlx.Modules.Portal.Domain.Configuration;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 门户系统参数键约定（点分小写）。种子服务按此键写入默认值，系统管理后续在线维护。
/// </summary>
public static class SystemParameterKeys
{
    /// <summary>密码连续错误锁定阈值（次）。</summary>
    public const string AuthMaxPasswordFailAttempts = "auth.max_password_fail_attempts";

    /// <summary>密码失败计数窗口（分钟）。</summary>
    public const string AuthPasswordFailWindowMinutes = "auth.password_fail_window_minutes";

    /// <summary>参数缓存键前缀（_portal:params:{paramKey}，读路径 Cache-Aside、写路径失效共用）。</summary>
    public const string CacheKeyPrefix = PortalConstants.CacheKeys.SystemParameterPrefix;
}

/// <summary>
/// 门户系统参数服务（<see cref="ISystemParameterProvider"/> Portal 实现）。
/// Cache-Aside：键规范 <c>_portal:params:{paramKey}</c>（门户级数据用 _portal 保留前缀），
/// TTL 5 分钟；参数在线修改后由管理端点负责失效（阶段 1C 仅建读路径）。
/// </summary>
public sealed partial class PortalSystemParameterService(PortalDbContext db, ICacheService cache, ILogger<PortalSystemParameterService> logger) : ISystemParameterProvider
{
    // LoggerMessage 源生成器要求 _logger 字段（主构造参数经字段初始化器接入）
    private readonly ILogger<PortalSystemParameterService> _logger = logger;

    private const string CacheKeyPrefix = SystemParameterKeys.CacheKeyPrefix;

    private static readonly TimeSpan CacheTtl = PortalConstants.CacheKeys.DefaultTtl;

    /// <inheritdoc />
    public async Task<string> GetStringAsync(string paramKey, string fallback, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramKey);

        var value = await GetRawAsync(paramKey, cancellationToken).ConfigureAwait(false);
        return value ?? fallback;
    }

    /// <inheritdoc />
    public async Task<int> GetIntAsync(string paramKey, int fallback, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramKey);

        var value = await GetRawAsync(paramKey, cancellationToken).ConfigureAwait(false);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    /// <summary>Cache-Aside 读单个参数原始值。</summary>
    private async Task<string?> GetRawAsync(string paramKey, CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync<string>(CacheKeyPrefix + paramKey, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var value = await db.SystemParameters
            .AsNoTracking()
            .Where(p => p.ParamKey == paramKey)
            .Select(p => p.ParamValue)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (value is null)
        {
            // 参数表未初始化：不缓存空结果，回落调用方 fallback
            LogParameterMissing(paramKey);
            return null;
        }

        await cache.SetAsync(CacheKeyPrefix + paramKey, value, cancellationToken, new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
        }).ConfigureAwait(false);

        return value;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "系统参数 {ParamKey} 未初始化，使用调用方 fallback")]
    private partial void LogParameterMissing(string paramKey);
}
