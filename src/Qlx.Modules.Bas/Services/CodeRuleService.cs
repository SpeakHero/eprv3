/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 00:20:00 +08:00 中国标准时间 星期一
 * 描述：编码规则服务实现——读穿缓存（coderule:list）+ 启停写路径提交后按前缀失效，
 *       规则内容不可修改（N-1：编码统一由后端发号器生成）
 */

using System.Globalization;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Caching;
using Qlx.Modules.Bas.Domain.Numbering;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Repositories;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 编码规则服务实现。读路径经 <c>GetOrCreateAsync</c> 读穿缓存（C-2/C-3/C-4），
/// 启停提交后按 <c>bas:coderule:</c> 前缀失效（C-5 失效优先）；规则分段模板原样下发，不做展示加工（B-2）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="cache">统一缓存服务。</param>
internal sealed class CodeRuleService(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    ICacheService cache) : ICodeRuleService
{
    private const string CacheSubPrefix = "coderule:";

    private static readonly CacheEntryOptions s_cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        NullValueExpiration = TimeSpan.FromSeconds(30),
    };

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    /// <inheritdoc />
    public async Task<IReadOnlyList<CodeRuleRow>> ListAsync(
        string? bizType, int? status, CancellationToken ct)
    {
        string normalizedBizType = bizType?.Trim() ?? string.Empty;
        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId,
            QlxModuleCodes.Bas,
            $"{CacheSubPrefix}list:{Format(normalizedBizType)}:{Format(status)}");

        return await cache.GetOrCreateAsync<IReadOnlyList<CodeRuleRow>>(
            cacheKey,
            factory: async token =>
            {
                IReadOnlyList<CodeRule> rules = await repo
                    .ListCodeRulesAsync(
                        normalizedBizType.Length == 0 ? null : normalizedBizType, status, token)
                    .ConfigureAwait(false);
                return [.. rules.Select(Map)];
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<CodeRuleRow> EnableAsync(string bizType, CancellationToken ct)
        => ToggleAsync(bizType, enable: true, ct);

    /// <inheritdoc />
    public Task<CodeRuleRow> DisableAsync(string bizType, CancellationToken ct)
        => ToggleAsync(bizType, enable: false, ct);

    private async Task<CodeRuleRow> ToggleAsync(string bizType, bool enable, CancellationToken ct)
    {
        string normalized = RequireBizType(bizType);
        CodeRule rule = await repo.FindCodeRuleAsync(normalized, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("编码规则", normalized);

        if (enable)
        {
            rule.Enable();
        }
        else
        {
            rule.Disable();
        }

        await repo.UpdateCodeRuleAsync(rule, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        // 失效优先：写库提交后按子前缀删除，读请求经 GetOrCreateAsync 重建（C-5-1）
        _ = await cache.RemoveByPrefixAsync(
            $"{QlxCacheKey.TenantKeyPrefix}:{TenantId.ToString(CultureInfo.InvariantCulture)}:{QlxModuleCodes.Bas}:{CacheSubPrefix}",
            ct).ConfigureAwait(false);

        return Map(rule);
    }

    private static string RequireBizType(string? bizType)
        => string.IsNullOrWhiteSpace(bizType)
            ? throw new ValidationException("bizType", "业务类型不能为空")
            : bizType.Trim();

    private static string Format(string value) => value.Length == 0 ? "-" : value;

    private static string Format(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static CodeRuleRow Map(CodeRule rule)
        => new(
            rule.BizType,
            rule.Prefix,
            rule.SegmentsJson,
            (int)rule.Status,
            rule.UpdatedAt);
}
