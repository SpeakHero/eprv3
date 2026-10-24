/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 19:10:00 +08:00 中国标准时间 星期四
 * 描述：审批快照变量解析器（IVariableResolver）——从实例 SnapshotJson 取值供条件引擎求值（R3）
 */

using System.Text.Json;
using Qlx.Abstractions.Expressions;

namespace Qlx.Modules.Support.Services.Approvals;

/// <summary>
/// 审批快照变量解析器：条件引擎求值 <c>变量</c> 节点时经 <see cref="IVariableResolver.TryResolve"/>
/// 从已加载的实例快照 JSON 文档取值（大小写不敏感）。快照字段缺失时返回 false，
/// 引擎严格模式下抛出「变量未解析」异常——满足"快照字段缺失报错"验收（R3）。
/// </summary>
public sealed class ApprovalSnapshotResolver : IVariableResolver
{
    private readonly JsonElement _root;

    /// <summary>
    /// 从快照 JSON 字符串构造解析器。
    /// </summary>
    /// <param name="snapshotJson">实例快照 JSON（<c>{}</c> 或对象形态）。</param>
    /// <exception cref="ArgumentException">快照 JSON 非对象形态时抛出。</exception>
    public ApprovalSnapshotResolver(string snapshotJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        var document = JsonDocument.Parse(snapshotJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("审批快照必须是 JSON 对象形态。", nameof(snapshotJson));
        }

        // 克隆根元素：JsonDocument 释放后 JsonElement 失效，必须脱离生命周期持有
        _root = document.RootElement.Clone();
    }

    /// <inheritdoc />
    public bool TryResolve(string name, out object? value)
    {
        value = null;
        if (_root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in _root.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = ToClrValue(property.Value);
            return true;
        }

        return false;
    }

    /// <summary>把 JSON 元素转为引擎可识别的 CLR 值（string / long / double / bool / null）。</summary>
    /// <param name="element">JSON 元素。</param>
    /// <returns>CLR 值。</returns>
    /// <remarks>
    /// 整数分支必须显式 <c>(object)</c> 装箱：三元表达式在 long/double 分支间存在隐式转换时
    /// 结果类型会被推断为 double，导致金额类整数丢失 long 语义（比较精度受损）。
    /// </remarks>
    private static object? ToClrValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var longValue) ? (object)longValue : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText(),
    };
}
