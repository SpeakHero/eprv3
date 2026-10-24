/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:35:00 +08:00 中国标准时间 星期三
 * 描述：条件值 JSON 转换器（expr_json 中 object 字面量与标量 JSON 的双向映射），从 BuildingBlocks 下沉
 */

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Qlx.Abstractions.Expressions;

/// <summary>
/// 条件值 JSON 转换器：负责 <see cref="LiteralNode"/> 字面量（object 类型）的序列化与反序列化。
/// expr_json 中字面量仅支持标量：字符串 / 数值（long / decimal）/ 布尔 / null；
/// Guid 与日期族以不变文化 ISO 字符串形态存储。
/// </summary>
public sealed class ConditionValueJsonConverter : JsonConverter<object?>
{
    /// <inheritdoc />
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Null => null,
            JsonTokenType.Number => ReadNumber(ref reader),
            _ => throw new ConditionExpressionException(
                ConditionExpressionErrorKind.Parse,
                $"expr_json 字面量仅支持标量（字符串 / 数值 / 布尔 / null），遇到 {reader.TokenType}。")
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case byte or sbyte or short or ushort or int or uint or long:
                writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case ulong number:
                writer.WriteNumberValue(number);
                break;
            case decimal decimalValue:
                writer.WriteNumberValue(decimalValue);
                break;
            case double doubleValue when !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue):
                writer.WriteNumberValue(doubleValue);
                break;
            case float floatValue when !float.IsNaN(floatValue) && !float.IsInfinity(floatValue):
                writer.WriteNumberValue(floatValue);
                break;
            case Guid guid:
                writer.WriteStringValue(guid.ToString("D", CultureInfo.InvariantCulture));
                break;
            case DateTime dateTime:
                writer.WriteStringValue(dateTime.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset dateTimeOffset:
                writer.WriteStringValue(dateTimeOffset.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateOnly dateOnly:
                writer.WriteStringValue(dateOnly.ToString("O", CultureInfo.InvariantCulture));
                break;
            case TimeOnly timeOnly:
                writer.WriteStringValue(timeOnly.ToString("O", CultureInfo.InvariantCulture));
                break;
            default:
                throw new ConditionExpressionException(
                    ConditionExpressionErrorKind.Parse,
                    $"expr_json 不支持的字面量类型：{value.GetType().Name}（可注册自定义字面量解析器在文本表达式侧支持）。");
        }
    }

    private static object ReadNumber(ref Utf8JsonReader reader)
        => reader.TryGetInt64(out var longValue) ? longValue : reader.GetDecimal();
}
