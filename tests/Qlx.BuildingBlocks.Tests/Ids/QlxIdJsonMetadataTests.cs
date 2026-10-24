/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22
 * 描述：全局 JSON 业务 Id 约定单元测试——Id 命名属性混淆、非 Id 的 long 保持数值、反序列化兼容
 */

using System.Text.Json;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.JsonConverters;

namespace Qlx.BuildingBlocks.Tests.Ids;

public sealed class QlxIdJsonMetadataTests
{
    // 与 HashidsLongModelBinderTests 共用同一盐值：注册表为进程级静态状态，测试并行时互换等价
    private const string TestSalt = "unit-test-global-id-salt";

    private static readonly HashidsObfuscator s_obfuscator = RegisterObfuscator();

    private static HashidsObfuscator RegisterObfuscator()
    {
        var obfuscator = new HashidsObfuscator(TestSalt, minLength: 8);
        IIdObfuscatorRegistry.Register(obfuscator);
        return obfuscator;
    }

    private static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = QlxIdJsonMetadata.CreateResolver(),
    };

    /// <summary>测试契约：Id / ParentId / TenantId 为业务 Id；Total / RowsCopied 为计数指标。</summary>
    public sealed record SampleContract(
        long Id,
        long? ParentId,
        long TenantId,
        long Total,
        long RowsCopied,
        string Name);

    [Fact]
    public void SerializeEncodesIdNamedPropertiesOnly()
    {
        var json = JsonSerializer.Serialize(
            new SampleContract(42L, 7L, 100L, 1234L, 99L, "测试"), CreateOptions());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(s_obfuscator.Encode(42L), root.GetProperty("id").GetString());
        Assert.Equal(s_obfuscator.Encode(7L), root.GetProperty("parentId").GetString());
        Assert.Equal(s_obfuscator.Encode(100L), root.GetProperty("tenantId").GetString());

        // 非 Id 语义的 long 必须保持数值，不能被混淆
        Assert.Equal(JsonValueKind.Number, root.GetProperty("total").ValueKind);
        Assert.Equal(1234L, root.GetProperty("total").GetInt64());
        Assert.Equal(99L, root.GetProperty("rowsCopied").GetInt64());
    }

    [Fact]
    public void SerializeNullableIdWithNullValueWritesNull()
    {
        var json = JsonSerializer.Serialize(
            new SampleContract(42L, null, 100L, 1L, 0L, "测试"), CreateOptions());
        using var document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("parentId").ValueKind);
    }

    [Fact]
    public void DeserializeAcceptsObfuscatedAndPlainNumericIds()
    {
        var json = $$"""
            {
              "id": "{{s_obfuscator.Encode(42L)}}",
              "parentId": 7,
              "tenantId": "100",
              "total": 1234,
              "rowsCopied": 99,
              "name": "测试"
            }
            """;

        var model = JsonSerializer.Deserialize<SampleContract>(json, CreateOptions());

        Assert.NotNull(model);
        Assert.Equal(42L, model.Id);
        Assert.Equal(7L, model.ParentId);
        Assert.Equal(100L, model.TenantId);
        Assert.Equal(1234L, model.Total);
        Assert.Equal(99L, model.RowsCopied);
    }

    [Fact]
    public void DeserializeInvalidObfuscatedIdThrowsJsonException()
    {
        const string json = """{"id":"!!!not-valid!!!","total":1,"rowsCopied":0,"name":"x"}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SampleContract>(json, CreateOptions()));
    }

    [Fact]
    public void SerializeEncodesIdsInNestedObjects()
    {
        var json = JsonSerializer.Serialize(
            new NestedContract(1L, 10L, new NestedChild(2L, 3L, 20L)), CreateOptions());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var child = root.GetProperty("child");

        Assert.Equal(s_obfuscator.Encode(1L), root.GetProperty("id").GetString());
        Assert.Equal(s_obfuscator.Encode(2L), child.GetProperty("id").GetString());
        Assert.Equal(s_obfuscator.Encode(3L), child.GetProperty("parentId").GetString());

        // 各层非 Id 的 long 均保持数值
        Assert.Equal(JsonValueKind.Number, root.GetProperty("total").ValueKind);
        Assert.Equal(JsonValueKind.Number, child.GetProperty("amount").ValueKind);
        Assert.Equal(20L, child.GetProperty("amount").GetInt64());
    }

    [Fact]
    public void SerializeEncodesIdsInsideCollections()
    {
        var json = JsonSerializer.Serialize(
            new CollectionContract([new NestedContract(1L, 10L, new NestedChild(2L, null, 20L))], 99L),
            CreateOptions());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var item = root.GetProperty("items")[0];

        Assert.Equal(s_obfuscator.Encode(1L), item.GetProperty("id").GetString());
        Assert.Equal(s_obfuscator.Encode(2L), item.GetProperty("child").GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Number, root.GetProperty("total").ValueKind);
        Assert.Equal(99L, root.GetProperty("total").GetInt64());
    }

    [Fact]
    public void SerializeZeroOrNegativeIdWritesNumber()
    {
        var json = JsonSerializer.Serialize(
            new SampleContract(0L, -5L, 0L, 1L, 0L, "x"), CreateOptions());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // 非正数不参与混淆（Hashids 无业务意义），保持数值以兼容哨兵值 / 缺省值
        Assert.Equal(JsonValueKind.Number, root.GetProperty("id").ValueKind);
        Assert.Equal(0L, root.GetProperty("id").GetInt64());
        Assert.Equal(JsonValueKind.Number, root.GetProperty("parentId").ValueKind);
        Assert.Equal(-5L, root.GetProperty("parentId").GetInt64());
    }

    [Fact]
    public void DeserializeNullForNonNullableIdReadsAsZero()
    {
        const string json = """
            {"id":null,"parentId":null,"tenantId":1,"total":1,"rowsCopied":0,"name":"x"}
            """;

        var model = JsonSerializer.Deserialize<SampleContract>(json, CreateOptions());

        Assert.NotNull(model);
        Assert.Equal(0L, model.Id);
        Assert.Null(model.ParentId);
    }

    [Fact]
    public void SnowflakeIdRoundTripsExactAsString()
    {
        // 1.9e18 量级雪花 ID 超出 JS Number 安全整数上限（2^53-1），必须以字符串往返
        const long snowflakeId = 1_900_000_000_000_000_123L;

        var json = JsonSerializer.Serialize(
            new SampleContract(snowflakeId, null, 1L, 0L, 0L, "x"), CreateOptions());
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("id").ValueKind);
        Assert.Equal(s_obfuscator.Encode(snowflakeId), document.RootElement.GetProperty("id").GetString());

        var model = JsonSerializer.Deserialize<SampleContract>(json, CreateOptions());
        Assert.NotNull(model);
        Assert.Equal(snowflakeId, model.Id);
    }

    [Fact]
    public void NamesContainingIdLettersButNotIdSuffixStayNumeric()
    {
        var json = JsonSerializer.Serialize(new NamingGuardContract(1L, 2L, 3L), CreateOptions());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // Paid / Valid / Subtotal 含 "id" 字母但不符合 Id 命名约定，不得被误混淆
        Assert.Equal(JsonValueKind.Number, root.GetProperty("paid").ValueKind);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("valid").ValueKind);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("subtotal").ValueKind);
    }

    [Fact]
    public void IdCollectionRoundTripsObfuscatedStringsAndPlainNumbers()
    {
        // 序列化：UserIds（*Ids 命名 + IReadOnlyList<long>）下发混淆字符串数组
        var json = JsonSerializer.Serialize(
            new IdCollectionContract([42L, 1_900_000_000_000_000_123L], [7L, 8L]), CreateOptions());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var userIds = root.GetProperty("userIds");
        Assert.Equal(JsonValueKind.Array, userIds.ValueKind);
        Assert.Equal(s_obfuscator.Encode(42L), userIds[0].GetString());
        Assert.Equal(s_obfuscator.Encode(1_900_000_000_000_000_123L), userIds[1].GetString());

        // 非 Ids 命名的 long 集合保持数值数组
        Assert.Equal(7L, root.GetProperty("counts")[0].GetInt64());
        Assert.Equal(8L, root.GetProperty("counts")[1].GetInt64());

        // 反序列化：混淆字符串与裸数字混合数组均可解析
        var model = JsonSerializer.Deserialize<IdCollectionContract>(
            $$"""{"userIds":["{{s_obfuscator.Encode(42L)}}",100],"counts":[7,8]}""", CreateOptions());

        Assert.NotNull(model);
        Assert.Equal(42L, model.UserIds[0]);
        Assert.Equal(100L, model.UserIds[1]);
    }

    /// <summary>集合契约：UserIds 为业务 Id 集合（*Ids 命名），Counts 为普通数值集合。</summary>
    public sealed record IdCollectionContract(IReadOnlyList<long> UserIds, IReadOnlyList<long> Counts);

    /// <summary>嵌套契约：外层与内层各有 Id 命名属性，Amount / Total 为非 Id。</summary>
    public sealed record NestedContract(long Id, long Total, NestedChild Child);

    /// <summary>嵌套子契约。</summary>
    public sealed record NestedChild(long Id, long? ParentId, long Amount);

    /// <summary>集合契约：元素内 Id 需混淆，外层 Total 保持数值。</summary>
    public sealed record CollectionContract(IReadOnlyList<NestedContract> Items, long Total);

    /// <summary>命名守卫契约：仅名称含 id 字母、并非 Id 语义的 long。</summary>
    public sealed record NamingGuardContract(long Paid, long Valid, long Subtotal);
}
