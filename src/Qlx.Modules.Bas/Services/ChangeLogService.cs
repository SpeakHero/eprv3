/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 00:40:00 +08:00 中国标准时间 星期一
 * 描述：变更日志服务实现——审计库变更快照只读查询 + 服务层硬上限截断 + 敏感字段 JSON 递归脱敏
 */

using System.Text.Json;
using System.Text.Json.Nodes;

using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Bas.Dtos;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 变更日志服务实现。审计库无分页能力，服务层固定取前 <see cref="HardLimit"/> 条后按页码切片，
/// <c>Total</c> 取截断后条数（避免全表扫描）；快照 JSON 中证件号 / 手机号 / 电话 / 银行卡递归脱敏（C-7-3）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="audit">审计服务（变更快照查询）。</param>
internal sealed class ChangeLogService(
    ITenantContext tenantCtx,
    IAuditService audit) : IChangeLogService
{
    /// <summary>单次查询硬上限（审计库无分页，超限截断）。</summary>
    internal const int HardLimit = 500;

    /// <summary>脱敏键集合（键名不区分大小写包含即脱敏）。</summary>
    private static readonly string[] s_sensitiveKeyTokens =
        ["idno", "mobile", "phone", "bankcard"];

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    /// <inheritdoc />
    public async Task<PagedResult<ChangeLogRow>> QueryAsync(
        string? entityType,
        long? entityId,
        string? changedBy,
        DateTimeOffset? from,
        DateTimeOffset? toTime,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        int safePage = Math.Max(page, 1);
        int safeSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 200);
        int skip = (safePage - 1) * safeSize;

        IReadOnlyList<AuditLogEntry> changes = await audit.QueryChangesAsync(
            TenantId,
            ct,
            NullIfBlank(entityType),
            entityId,
            NullIfBlank(changedBy),
            from,
            toTime).ConfigureAwait(false);

        // 结果已按变更时间倒序，取前 N 条后切片（Total = 截断后条数）
        List<ChangeLogRow> capped = [.. changes.Take(HardLimit).Select(Map)];
        IReadOnlyList<ChangeLogRow> items = [.. capped.Skip(skip).Take(safeSize)];
        return new PagedResult<ChangeLogRow>(items, capped.Count, safePage, safeSize);
    }

    private static ChangeLogRow Map(AuditLogEntry entry)
        => new(
            entry.EventId,
            entry.EntityType,
            entry.EntityId,
            entry.Operation,
            Sanitize(entry.OldValues),
            Sanitize(entry.NewValues),
            entry.ChangedBy,
            entry.ChangedAt);

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// 快照 JSON 递归脱敏：命中敏感键的取值整体掩码（保留后 4 位）；不是合法 JSON 时原样返回，不抛异常。
    /// </summary>
    /// <param name="json">原始快照 JSON。</param>
    /// <returns>脱敏后的 JSON（解析失败时原样返回）。</returns>
    private static string? Sanitize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            JsonNode? node = JsonNode.Parse(json);
            if (node is null)
            {
                return json;
            }

            SanitizeNode(node);
            return node.ToJsonString();
        }
        catch (JsonException)
        {
            return json;
        }
        catch (NotSupportedException)
        {
            return json;
        }
    }

    private static void SanitizeNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (string key in obj.Select(static pair => pair.Key).ToList())
            {
                JsonNode? child = obj[key];
                if (IsSensitiveKey(key))
                {
                    obj[key] = MaskValue(child);
                }
                else if (child is not null)
                {
                    SanitizeNode(child);
                }
            }

            return;
        }

        if (node is JsonArray array)
        {
            for (int index = 0; index < array.Count; index++)
            {
                JsonNode? child = array[index];
                if (child is not null)
                {
                    SanitizeNode(child);
                }
            }
        }
    }

    private static bool IsSensitiveKey(string key)
        => s_sensitiveKeyTokens.Any(token => key.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static JsonValue MaskValue(JsonNode? value)
    {
        string? text = value is JsonValue jsonValue && jsonValue.TryGetValue(out string? parsed)
            ? parsed
            : null;

        if (string.IsNullOrEmpty(text))
        {
            return JsonValue.Create("****")!;
        }

        string masked = text.Length <= 4
            ? "****"
            : string.Concat("****", text.AsSpan(text.Length - 4));
        return JsonValue.Create(masked)!;
    }
}
