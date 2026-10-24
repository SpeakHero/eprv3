/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22
 * 描述：可空 long Id 的 Hashids JSON 转换器（非空分支委托 HashidsJsonConverter，避免解析逻辑重复）
 */

using System.Text.Json;
using System.Text.Json.Serialization;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.BuildingBlocks.JsonConverters;

/// <summary>
/// Hashids 混淆器 JSON 转换器（<see cref="long"/>?）。由 <see cref="QlxIdJsonMetadata"/> 自动挂载。
/// </summary>
public sealed class HashidsNullableJsonConverter : JsonConverter<long?>
{
    private readonly HashidsJsonConverter _inner;

    /// <summary>无参构造：非空值解析委托 <see cref="HashidsJsonConverter"/> 的运行期注册表逻辑。</summary>
    public HashidsNullableJsonConverter()
    {
        _inner = new HashidsJsonConverter();
    }

    /// <summary>显式指定混淆器。</summary>
    public HashidsNullableJsonConverter(IIdObfuscator obfuscator)
    {
        _inner = new HashidsJsonConverter(obfuscator);
    }

    /// <inheritdoc />
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : _inner.Read(ref reader, typeof(long), options);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        _inner.Write(writer, value.Value, options);
    }
}
