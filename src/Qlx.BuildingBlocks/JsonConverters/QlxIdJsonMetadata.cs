/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-22
 * 修改时间：2026-10-23 12:30:00 +08:00 中国标准时间 星期五
 * 描述：全局 JSON 元数据约定——按属性命名约定自动为业务 Id（long / long? / Id 集合）挂载 Hashids 转换器，
 *       消除各 DTO 逐个标注 [JsonConverter(typeof(HashidsJsonConverter))] 的重复代码。
 */

using System.Text.Json.Serialization.Metadata;

namespace Qlx.BuildingBlocks.JsonConverters;

/// <summary>
/// 业务 Id JSON 元数据约定。
/// <para>匹配规则：属性类型为 <see cref="long"/> / <see cref="long"/>?，且属性名等于 <c>Id</c> 或以 <c>Id</c> 结尾（如 TenantId / ParentId）；
/// 属性类型为 <see cref="IReadOnlyList{Long}"/> 且属性名以 <c>Ids</c> 结尾（如 UserIds / RoleIds）时挂载集合混淆转换器。</para>
/// <para>非 Id 语义的 long（如 Total / RowsCopied / Value）不会被混淆，避免污染计数与指标字段。</para>
/// </summary>
public static class QlxIdJsonMetadata
{
    /// <summary>
    /// 创建带业务 Id 约定的 JSON 类型信息解析器（替代裸 <see cref="DefaultJsonTypeInfoResolver"/>）。
    /// </summary>
    /// <returns>可直接赋给 <c>JsonSerializerOptions.TypeInfoResolver</c> 的解析器。</returns>
    public static IJsonTypeInfoResolver CreateResolver()
        => new DefaultJsonTypeInfoResolver { Modifiers = { static typeInfo => Apply(typeInfo) } };

    /// <summary>
    /// 对单个类型应用业务 Id 转换器约定（供自定义解析器复用）。
    /// </summary>
    /// <param name="typeInfo">类型元数据。</param>
    public static void Apply(JsonTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (!IsBusinessId(property.PropertyType, property.Name)
                && !IsIdCollection(property.PropertyType, property.Name))
            {
                continue;
            }

            // 每次解析新建实例：转换器不缓存混淆器，运行期按需读取注册表，启动顺序无关
            if (property.PropertyType == typeof(long))
            {
                property.CustomConverter = new HashidsJsonConverter();
            }
            else if (property.PropertyType == typeof(long?))
            {
                property.CustomConverter = new HashidsNullableJsonConverter();
            }
            else if (property.PropertyType == typeof(IReadOnlyList<long>))
            {
                property.CustomConverter = new HashidsLongListConverter();
            }
        }
    }

    /// <summary>判定属性是否属于业务 Id：类型为 long / long?，且命名符合 Id 约定。</summary>
    private static bool IsBusinessId(Type propertyType, string propertyName)
        => (propertyType == typeof(long) || propertyType == typeof(long?)) && IsIdName(propertyName);

    /// <summary>判定属性是否属于业务 Id 集合：类型为 IReadOnlyList&lt;long&gt;，且命名符合 Ids 约定。</summary>
    private static bool IsIdCollection(Type propertyType, string propertyName)
        => propertyType == typeof(IReadOnlyList<long>) && IsIdCollectionName(propertyName);

    /// <summary>
    /// 命名约定：<c>Id</c>（含大小写变体）或以大写 <c>Id</c> 结尾（PascalCase / camelCase 均成立）。
    /// 刻意不做全小写后缀匹配，避免把 paid / valid 等普通词汇误判为 Id。
    /// </summary>
    private static bool IsIdName(string propertyName)
        => string.Equals(propertyName, "Id", StringComparison.OrdinalIgnoreCase)
        || propertyName.EndsWith("Id", StringComparison.Ordinal);

    /// <summary>
    /// 集合命名约定：以大写 <c>Ids</c> 结尾（PascalCase / camelCase 均成立，如 UserIds / userIds）。
    /// 刻意不做全小写后缀匹配，避免把 digits / weights 等普通词汇误判为 Id 集合。
    /// </summary>
    private static bool IsIdCollectionName(string propertyName)
        => propertyName.EndsWith("Ids", StringComparison.Ordinal);
}
