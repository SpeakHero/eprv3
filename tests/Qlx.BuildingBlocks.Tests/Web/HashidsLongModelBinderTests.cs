/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22
 * 描述：业务 Id 模型绑定器单元测试——长整型 Id 自动解码、纯数字兼容、非法/缺省边界、全局注册
 */

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Web;

namespace Qlx.BuildingBlocks.Tests.Web;

public sealed class HashidsLongModelBinderTests
{
    // 与 QlxIdJsonMetadataTests 共用同一盐值：注册表为进程级静态状态，测试并行时互换等价
    private const string TestSalt = "unit-test-global-id-salt";

    private static readonly HashidsObfuscator s_obfuscator = RegisterObfuscator();

    private static HashidsObfuscator RegisterObfuscator()
    {
        var obfuscator = new HashidsObfuscator(TestSalt, minLength: 8);
        IIdObfuscatorRegistry.Register(obfuscator);
        return obfuscator;
    }

    [Fact]
    public async Task BindObfuscatedValueDecodesToRealId()
    {
        var context = CreateContext("id", s_obfuscator.Encode(42L), typeof(long));

        await new HashidsLongModelBinder(isNullable: false).BindModelAsync(context);

        Assert.True(context.Result.IsModelSet);
        Assert.Equal(42L, context.Result.Model);
        Assert.Equal(0, context.ModelState.ErrorCount);
    }

    [Fact]
    public async Task BindPlainNumericValueIsBackwardCompatible()
    {
        var context = CreateContext("id", "123456789", typeof(long));

        await new HashidsLongModelBinder(isNullable: false).BindModelAsync(context);

        Assert.True(context.Result.IsModelSet);
        Assert.Equal(123456789L, context.Result.Model);
        Assert.Equal(0, context.ModelState.ErrorCount);
    }

    [Fact]
    public async Task BindSnowflakeScaleObfuscatedValueDecodesExactly()
    {
        // 1.9e18 量级雪花 Id 超出 JS 安全整数上限，解码后必须与真实值完全一致
        const long snowflakeId = 1_900_000_000_000_000_123L;
        var context = CreateContext("id", s_obfuscator.Encode(snowflakeId), typeof(long));

        await new HashidsLongModelBinder(isNullable: false).BindModelAsync(context);

        Assert.True(context.Result.IsModelSet);
        Assert.Equal(snowflakeId, context.Result.Model);
        Assert.Equal(0, context.ModelState.ErrorCount);
    }

    [Fact]
    public async Task BindNegativePlainNumericValuePassesThrough()
    {
        // 绑定层只负责解析，负数合法性交由业务层判定（不做静默修正）
        var context = CreateContext("id", "-5", typeof(long));

        await new HashidsLongModelBinder(isNullable: false).BindModelAsync(context);

        Assert.True(context.Result.IsModelSet);
        Assert.Equal(-5L, context.Result.Model);
        Assert.Equal(0, context.ModelState.ErrorCount);
    }

    [Theory]
    [InlineData("!!!not-valid!!!")]
    [InlineData("0x1F")]
    [InlineData("true")]
    public async Task BindInvalidValueAddsModelStateError(string raw)
    {
        var context = CreateContext("id", raw, typeof(long));

        await new HashidsLongModelBinder(isNullable: false).BindModelAsync(context);

        Assert.False(context.Result.IsModelSet);
        Assert.Contains("无效的资源 Id", ErrorMessages(context));
    }

    [Fact]
    public async Task BindMissingNullableReturnsNull()
    {
        var context = CreateContext("orgId", null, typeof(long?));

        await new HashidsLongModelBinder(isNullable: true).BindModelAsync(context);

        Assert.True(context.Result.IsModelSet);
        Assert.Null(context.Result.Model);
        Assert.Equal(0, context.ModelState.ErrorCount);
    }

    [Fact]
    public async Task BindMissingNonNullableAddsRequiredError()
    {
        var context = CreateContext("id", null, typeof(long));

        await new HashidsLongModelBinder(isNullable: false).BindModelAsync(context);

        Assert.False(context.Result.IsModelSet);
        Assert.Contains("缺少必填参数", ErrorMessages(context));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public async Task BindBlankValueFollowsNullabilityRule(string raw, bool isNullable)
    {
        var modelType = isNullable ? typeof(long?) : typeof(long);
        var context = CreateContext("id", raw, modelType);

        await new HashidsLongModelBinder(isNullable).BindModelAsync(context);

        if (isNullable)
        {
            Assert.True(context.Result.IsModelSet);
            Assert.Null(context.Result.Model);
            Assert.Equal(0, context.ModelState.ErrorCount);
        }
        else
        {
            Assert.False(context.Result.IsModelSet);
            Assert.Contains("缺少必填参数", ErrorMessages(context));
        }
    }

    [Theory]
    [InlineData(typeof(long), true)]
    [InlineData(typeof(long?), true)]
    [InlineData(typeof(int), false)]
    [InlineData(typeof(string), false)]
    [InlineData(typeof(Guid), false)]
    public void ProviderSelectsBinderOnlyForLongTypes(Type modelType, bool expectBinder)
    {
        var binder = new HashidsLongModelBinderProvider().GetBinder(new StubProviderContext(modelType));

        if (expectBinder)
        {
            Assert.IsType<HashidsLongModelBinder>(binder);
        }
        else
        {
            Assert.Null(binder);
        }
    }

    [Fact]
    public void AddQlxIdModelBindingInsertsProviderAtFirstPosition()
    {
        var services = new ServiceCollection();
        services.AddControllers().AddQlxIdModelBinding();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MvcOptions>>().Value;

        Assert.IsType<HashidsLongModelBinderProvider>(options.ModelBinderProviders[0]);
    }

    private static string[] ErrorMessages(ModelBindingContext context)
        => [.. context.ModelState.Values
            .SelectMany(entry => entry.Errors)
            .Select(error => error.ErrorMessage)];

    private static DefaultModelBindingContext CreateContext(string modelName, string? rawValue, Type modelType)
        => new DefaultModelBindingContext
        {
            ModelName = modelName,
            ModelMetadata = new EmptyModelMetadataProvider().GetMetadataForType(modelType),
            ModelState = new ModelStateDictionary(),
            ValueProvider = new StubValueProvider(modelName, rawValue),
        };

    /// <summary>固定返回单值的值提供者（模拟路由 / 查询参数命中）。</summary>
    private sealed class StubValueProvider(string modelName, string? rawValue) : IValueProvider
    {
        public bool ContainsPrefix(string prefix) => true;

        public ValueProviderResult GetValue(string key)
            => string.Equals(key, modelName, StringComparison.Ordinal)
                ? new ValueProviderResult(rawValue)
                : ValueProviderResult.None;
    }

    /// <summary>仅承载模型类型的绑定器提供器上下文（用于验证类型分派规则）。</summary>
    private sealed class StubProviderContext(Type modelType) : ModelBinderProviderContext
    {
        private static readonly EmptyModelMetadataProvider s_metadataProvider = new();

        public override BindingInfo BindingInfo => null!;

        public override ModelMetadata Metadata { get; } = s_metadataProvider.GetMetadataForType(modelType);

        public override IModelMetadataProvider MetadataProvider => s_metadataProvider;

        public override IModelBinder CreateBinder(ModelMetadata metadata) => throw new NotSupportedException();
    }
}
