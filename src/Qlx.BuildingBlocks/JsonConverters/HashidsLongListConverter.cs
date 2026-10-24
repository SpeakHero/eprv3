/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 12:30:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 12:30:00 +08:00 中国标准时间 星期五
 * 描述：Hashids 混淆器 JSON 集合转换器——IReadOnlyList<long> 业务 Id 集合与混淆字符串数组互转，
 *       由 QlxIdJsonMetadata 按属性命名约定（*Ids）自动挂载
 */

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Qlx.BuildingBlocks.JsonConverters;

/// <summary>
/// Hashids 混淆器 JSON 集合转换器（<see cref="IReadOnlyList{Long}"/>）。
/// <para>由 <see cref="QlxIdJsonMetadata"/> 按命名约定（以 <c>Ids</c> 结尾）自动挂载；
/// 请求体兼容混淆字符串与裸数字混合数组，响应体统一下发混淆字符串数组。</para>
/// </summary>
public sealed class HashidsLongListConverter : JsonConverter<IReadOnlyList<long>>
{
    /// <summary>元素级转换器：复用单值 Read（兼容数字/字符串）与 Write（非正数下发数值）。</summary>
    private static readonly HashidsJsonConverter s_elementConverter = new();

    /// <inheritdoc />
    public override IReadOnlyList<long> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return [];
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"HashidsLongListConverter 期望数组起始，实际 TokenType：{reader.TokenType}");
        }

        var values = new List<long>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return values;
            }

            values.Add(s_elementConverter.Read(ref reader, typeof(long), options));
        }

        throw new JsonException("JSON 数组未正常闭合");
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer, IReadOnlyList<long> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            s_elementConverter.Write(writer, item, options);
        }

        writer.WriteEndArray();
    }
}
