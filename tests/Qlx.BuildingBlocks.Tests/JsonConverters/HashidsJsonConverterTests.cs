/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22
 * 描述：Hashids JSON 转换器单元测试——直接驱动 Utf8JsonReader/Utf8JsonWriter，
 *       覆盖正数编码、0/负数透传、JSON null、纯数字兼容、非法字符串与不支持的 TokenType，
 *       以及 long? 可空转换器的 null 语义。
 */

using System.Text;
using System.Text.Json;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.JsonConverters;

namespace Qlx.BuildingBlocks.Tests.JsonConverters;

public sealed class HashidsJsonConverterTests
{
    private const string TestSalt = "unit-test-global-id-salt";

    private static readonly HashidsObfuscator s_obfuscator = new(TestSalt, minLength: 8);

    private static readonly HashidsJsonConverter s_converter = new(s_obfuscator);

    private static readonly HashidsNullableJsonConverter s_nullableConverter = new(s_obfuscator);

    // ==================== long 转换器：写 ====================

    [Fact]
    public void WritePositiveIdWritesObfuscatedString()
    {
        const long id = 42L;

        Assert.Equal($"\"{s_obfuscator.Encode(id)}\"", Write(id));
    }

    [Fact]
    public void WriteSnowflakeScaleIdWritesObfuscatedString()
    {
        // 雪花量级（1.9e18）远超 JS 安全整数上限，必须输出字符串
        const long id = 1_900_000_000_000_000_123L;
        var json = Write(id);

        Assert.StartsWith("\"", json, StringComparison.Ordinal);
        Assert.Equal($"\"{s_obfuscator.Encode(id)}\"", json);
    }

    [Fact]
    public void WriteZeroWritesNumber()
    {
        Assert.Equal("0", Write(0L));
    }

    [Fact]
    public void WriteNegativeWritesNumber()
    {
        // 负数无业务意义（Hashids 不支持），保持数值透传，由业务层判定
        Assert.Equal("-5", Write(-5L));
        Assert.Equal(long.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture), Write(long.MinValue));
    }

    // ==================== long 转换器：读 ====================

    [Fact]
    public void ReadObfuscatedStringReturnsRealId()
    {
        Assert.Equal(42L, Read($"\"{s_obfuscator.Encode(42L)}\""));
    }

    [Fact]
    public void ReadSnowflakeObfuscatedStringRoundTripsExactly()
    {
        const long id = 1_900_000_000_000_000_123L;

        Assert.Equal(id, Read($"\"{s_obfuscator.Encode(id)}\""));
    }

    [Fact]
    public void ReadPlainNumericStringReturnsNumber()
    {
        // 历史接口兼容：裸数字字符串直接按 long 解析
        Assert.Equal(123456789L, Read("\"123456789\""));
        Assert.Equal(-1L, Read("\"-1\""));
    }

    [Fact]
    public void ReadNumberTokenReturnsRawValue()
    {
        Assert.Equal(1234L, Read("1234"));
        Assert.Equal(-9L, Read("-9"));
    }

    [Fact]
    public void ReadNullReturnsZero()
    {
        Assert.Equal(0L, Read("null"));
    }

    [Fact]
    public void ReadInvalidStringThrowsJsonException()
    {
        var exception = Assert.Throws<JsonException>(() => Read("\"!!!not-valid!!!\""));

        Assert.Contains("Hashids", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadEmptyStringThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => Read("\"\""));
    }

    [Fact]
    public void ReadUnsupportedTokenTypeThrowsJsonException()
    {
        // true / 数组等非 数字、字符串、null 的 TokenType 一律拒绝
        Assert.Throws<JsonException>(() => Read("true"));
        Assert.Throws<JsonException>(() => Read("[1]"));
    }

    // ==================== long? 可空转换器 ====================

    [Fact]
    public void NullableWriteNullWritesNull()
    {
        Assert.Equal("null", WriteNullable(null));
    }

    [Fact]
    public void NullableWritePositiveIdWritesObfuscatedString()
    {
        Assert.Equal($"\"{s_obfuscator.Encode(8L)}\"", WriteNullable(8L));
    }

    [Fact]
    public void NullableWriteZeroWritesNumber()
    {
        Assert.Equal("0", WriteNullable(0L));
    }

    [Fact]
    public void NullableReadNullReturnsNull()
    {
        Assert.Null(ReadNullable("null"));
    }

    [Fact]
    public void NullableReadObfuscatedStringReturnsRealId()
    {
        Assert.Equal(42L, ReadNullable($"\"{s_obfuscator.Encode(42L)}\""));
    }

    [Fact]
    public void NullableReadPlainNumberReturnsNumber()
    {
        Assert.Equal(1234L, ReadNullable("1234"));
    }

    [Fact]
    public void NullableReadInvalidStringThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => ReadNullable("\"!!!not-valid!!!\""));
    }

    // ==================== 辅助方法 ====================

    private static string Write(long value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            s_converter.Write(writer, value, JsonSerializerOptions.Default);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static long Read(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        Assert.True(reader.Read());

        return s_converter.Read(ref reader, typeof(long), JsonSerializerOptions.Default);
    }

    private static string WriteNullable(long? value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            s_nullableConverter.Write(writer, value, JsonSerializerOptions.Default);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static long? ReadNullable(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        Assert.True(reader.Read());

        return s_nullableConverter.Read(ref reader, typeof(long?), JsonSerializerOptions.Default);
    }
}
