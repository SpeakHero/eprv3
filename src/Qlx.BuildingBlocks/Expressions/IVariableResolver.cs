/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:20:00 +08:00 中国标准时间 星期三
 * 描述：变量解析器内置实现（字典解析器 + 组合解析器），接口契约已下沉至 Abstractions.Expressions
 */

using System.Collections.Frozen;
using Qlx.Abstractions.Expressions;

namespace Qlx.BuildingBlocks.Expressions;

/// <summary>
/// 字典变量解析器：以 <see cref="IReadOnlyDictionary{TKey, TValue}"/> 承载变量表。
/// 默认键匹配不区分大小写（可通过构造参数切换）。
/// </summary>
public sealed class DictionaryVariableResolver : IVariableResolver
{
    private readonly FrozenDictionary<string, object?> _values;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="values">变量表（允许 null，视同空表）。</param>
    /// <param name="ignoreCase">键匹配是否不区分大小写，默认 true。</param>
    public DictionaryVariableResolver(IEnumerable<KeyValuePair<string, object?>>? values = null, bool ignoreCase = true)
    {
        var builder = new Dictionary<string, object?>(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (values is not null)
        {
            foreach (var pair in values)
            {
                builder[pair.Key] = pair.Value;
            }
        }

        _values = builder.ToFrozenDictionary(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public bool TryResolve(string name, out object? value) => _values.TryGetValue(name, out value);
}

/// <summary>
/// 组合变量解析器：按顺序查询多个解析器，首个命中即返回。
/// 用于「业务字段 + 系统参数 + 环境变量」等多来源叠加场景。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="resolvers">解析器链（按优先级从高到低排列，先注册先命中）。</param>
public sealed class CompositeVariableResolver(params IVariableResolver[] resolvers) : IVariableResolver
{
    private readonly IReadOnlyList<IVariableResolver> _resolvers = resolvers;

    /// <inheritdoc />
    public bool TryResolve(string name, out object? value)
    {
        foreach (var resolver in _resolvers)
        {
            if (resolver.TryResolve(name, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }
}
