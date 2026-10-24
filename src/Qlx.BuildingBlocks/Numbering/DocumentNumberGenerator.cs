/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-21 01:05:00 +08:00 中国标准时间 星期三
 * 描述：单据编号生成器默认实现，序号键含租户段并委托序号存储原子取号
 */

using Microsoft.Extensions.Options;
using Qlx.Abstractions.Tenancy;

namespace Qlx.BuildingBlocks.Numbering;

/// <summary>
/// 单据编号生成器默认实现。序号键为「租户ID:前缀:组织码:期间」，保证不同租户、
/// 不同单据类型、不同组织、不同期间的流水相互独立（租户共享库下各租户序号互不干扰）；
/// 序号键段内禁止出现冒号，防止键拼接歧义。编号文本格式不变：前缀-组织码-期间-流水。
/// </summary>
public sealed class DocumentNumberGenerator(
    ISerialSequenceStore sequenceStore,
    ITenantContext tenantContext,
    IOptions<DocumentNumberingOptions> options) : IDocumentNumberGenerator
{
    private readonly DocumentNumberingOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<string> NextAsync(string prefix, string orgCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(orgCode);

        if (prefix.Contains(':', StringComparison.Ordinal) || orgCode.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("业务前缀与组织码禁止包含冒号", nameof(prefix));
        }

        var tenantId = tenantContext.TenantId
            ?? throw new InvalidOperationException("未识别到租户上下文，禁止生成单据编号");

        if (tenantId <= 0)
        {
            throw new InvalidOperationException($"租户 ID 非法（{tenantId}），禁止生成单据编号");
        }

        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var period = DateTimeOffset.UtcNow.ToString(_options.PeriodFormat, invariant);
        // 租户段置于最前：共享库中不同租户同前缀+同组织码也不会撞号
        var sequenceKey = string.Create(invariant, $"{tenantId}:{prefix}:{orgCode}:{period}");
        var serial = await sequenceStore.GetNextAsync(sequenceKey, cancellationToken).ConfigureAwait(false);
        var serialText = serial.ToString(invariant).PadLeft(_options.SerialPadding, '0');

        return string.Create(
            invariant,
            $"{prefix}{_options.Separator}{orgCode}{_options.Separator}{period}{_options.Separator}{serialText}");
    }
}
