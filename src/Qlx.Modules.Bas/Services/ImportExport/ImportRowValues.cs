/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 01:00:00 +08:00 中国标准时间 星期一
 * 描述：导入行 / 导出条件的取值与租户还原助手——行号推算、Guid→long 租户还原并交叉校验、容错取值
 */

using System.Globalization;

using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.Modules.Bas.Services.ImportExport;

/// <summary>
/// 导入行取值助手。导入契约 <c>IImportRowSink</c> 不携带 Excel 行号，
/// 按数据行序号推算（表头为第 1 行，首条数据为第 2 行）；租户 Id 由 Guid 还原并与当前上下文交叉校验。
/// </summary>
internal static class ImportRowValues
{
    /// <summary>按数据行序号推算 Excel 行号（表头行固定为 1）。</summary>
    /// <param name="index">数据行序号（自 0 起）。</param>
    /// <returns>Excel 行号（自 2 起）。</returns>
    internal static int RowNumber(int index) => index + 2;

    /// <summary>
    /// 将导入 / 导出契约的 Guid 租户 Id 还原为 long，并与当前租户上下文交叉校验。
    /// </summary>
    /// <param name="tenantId">契约传入的租户 Guid。</param>
    /// <param name="expectedTenantId">当前租户上下文 Id。</param>
    /// <returns>还原后的租户 Id。</returns>
    /// <exception cref="ValidationException">Guid 无法还原为有效租户 Id。</exception>
    /// <exception cref="ForbiddenException">还原的租户与当前上下文不一致（跨租户越权）。</exception>
    internal static long ResolveTenantId(Guid tenantId, long expectedTenantId)
    {
        if (!GuidIdMapper.TryToLong(tenantId, out long resolved))
        {
            throw new ValidationException("tenantId", "租户标识非法：无法还原为有效租户 Id");
        }

        if (resolved != expectedTenantId)
        {
            throw new ForbiddenException("租户上下文不一致，禁止导入导出其他租户数据");
        }

        return resolved;
    }

    /// <summary>读取单元格文本（缺失 / 空白返回 null，两端去空白）。</summary>
    /// <param name="row">导入行。</param>
    /// <param name="key">列字段名。</param>
    /// <returns>文本值。</returns>
    internal static string? ReadString(IReadOnlyDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out object? value) || value is null)
        {
            return null;
        }

        string text = value as string ?? value.ToString() ?? string.Empty;
        text = text.Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>读取日期列（兼容 Excel 原生日期与文本日期）。</summary>
    /// <param name="row">导入行。</param>
    /// <param name="key">列字段名。</param>
    /// <returns>日期（缺失 / 非法返回 null）。</returns>
    internal static DateOnly? ReadDateOnly(IReadOnlyDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out object? value) || value is null)
        {
            return null;
        }

        return value switch
        {
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            DateOnly dateOnly => dateOnly,
            string text => DateTime.TryParse(
                textTrimmed(text), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
                ? DateOnly.FromDateTime(parsed)
                : null,
            _ => null,
        };

        static string textTrimmed(string text) => text.Trim();
    }

    /// <summary>读取导出条件中的整数（缺失 / 非法返回 null）。</summary>
    /// <param name="criteria">导出条件。</param>
    /// <param name="key">条件键。</param>
    /// <returns>整数值。</returns>
    internal static int? CriteriaInt32(IReadOnlyDictionary<string, object?> criteria, string key)
    {
        if (!criteria.TryGetValue(key, out object? value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int intValue => intValue,
            long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            decimal decimalValue => (int)decimalValue,
            string text when int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                => parsed,
            _ => null,
        };
    }

    /// <summary>读取导出条件中的文本（缺失 / 空白返回 null）。</summary>
    /// <param name="criteria">导出条件。</param>
    /// <param name="key">条件键。</param>
    /// <returns>文本值。</returns>
    internal static string? CriteriaString(IReadOnlyDictionary<string, object?> criteria, string key)
    {
        if (!criteria.TryGetValue(key, out object? value) || value is null)
        {
            return null;
        }

        string text = (value as string ?? value.ToString() ?? string.Empty).Trim();
        return text.Length == 0 ? null : text;
    }
}
