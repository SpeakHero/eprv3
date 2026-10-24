/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-22
 * 描述：Hashids 混淆器 JSON 转换器——业务 long Id 与混淆字符串互转。
 *       由 QlxIdJsonMetadata 按属性名约定（Id / *Id）自动挂载，DTO 无需逐个标注 [JsonConverter]。
 */

using System.Text.Json;
using System.Text.Json.Serialization;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.BuildingBlocks.JsonConverters;

/// <summary>
/// Hashids 混淆器 JSON 转换器（long）。
/// <para>常规场景由 <see cref="QlxIdJsonMetadata"/> 在序列化元数据阶段自动挂载到 Id 命名属性；</para>
/// <para>如确需在非 Id 命名的属性上使用，可显式标注 <c>[JsonConverter(typeof(HashidsJsonConverter))]</c>。</para>
/// </summary>
public sealed class HashidsJsonConverter : JsonConverter<long>
{
    private readonly IIdObfuscator? _obfuscator;

    /// <summary>无参构造：运行期从全局注册表取混淆器，未注册（如单元测试）时回退裸数字。</summary>
    public HashidsJsonConverter()
    {
    }

    /// <summary>显式指定混淆器（宿主启动阶段或单元测试注入）。</summary>
    public HashidsJsonConverter(IIdObfuscator obfuscator)
    {
        _obfuscator = obfuscator;
    }

    /// <inheritdoc />
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return 0L;
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetInt64();
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            if (ObfuscatedIdParser.TryParse(reader.GetString(), out var id))
            {
                return id;
            }

            throw new JsonException($"无法将字符串 \"{reader.GetString()}\" 解码为业务 Id（Hashids 格式不匹配）");
        }

        throw new JsonException($"HashidsJsonConverter 不支持的 TokenType：{reader.TokenType}");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        var obfuscator = _obfuscator
            ?? (IIdObfuscatorRegistry.IsRegistered ? IIdObfuscatorRegistry.Current : null);
        if (value <= 0 || obfuscator is null)
        {
            writer.WriteNumberValue(value);
            return;
        }

        writer.WriteStringValue(obfuscator.Encode(value));
    }
}
