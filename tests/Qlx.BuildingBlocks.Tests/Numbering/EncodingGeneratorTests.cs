/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：通用业务编码生成器 EncodingGenerator 单元测试
 *       覆盖：拼音首字母/完整拼音、前缀、序号递增去重、最大长度截断、
 *             序号位数配置、非中文字符保留、重试耗尽异常、入参校验
 */

using Qlx.BuildingBlocks.Numbering;
using Qlx.BuildingBlocks.Numbering.Pinyin;

namespace Qlx.BuildingBlocks.Tests.Numbering;

public sealed class EncodingGeneratorTests
{
    private static EncodingGenerator CreateGenerator()
        => new(new PinyinConverter(), new InMemorySerialSequenceStore());

    /// <summary>始终返回 false 的唯一性校验器（表示编码可用、无冲突）。</summary>
    private static Func<string, CancellationToken, Task<bool>> AlwaysAvailable =>
        static (_, _) => Task.FromResult(false);

    [Fact]
    public async Task GenerateInitialsModeProducesPinyinInitials()
    {
        var generator = CreateGenerator();

        var code = await generator.GenerateAsync(
            "管理员", "role", AlwaysAvailable,
            new EncodingGenerationOptions { PinyinMode = PinyinMode.Initials },
            TestContext.Current.CancellationToken);

        Assert.Equal("gly", code);
    }

    [Fact]
    public async Task GenerateFullModeProducesFullPinyin()
    {
        var generator = CreateGenerator();

        var code = await generator.GenerateAsync(
            "管理员", "role", AlwaysAvailable,
            new EncodingGenerationOptions { PinyinMode = PinyinMode.Full },
            TestContext.Current.CancellationToken);

        Assert.Equal("guanliyuan", code);
    }

    [Fact]
    public async Task GeneratePrefixIsPrepended()
    {
        var generator = CreateGenerator();

        var code = await generator.GenerateAsync(
            "管理员", "role", AlwaysAvailable,
            new EncodingGenerationOptions { Prefix = "role-", PinyinMode = PinyinMode.Initials },
            TestContext.Current.CancellationToken);

        Assert.Equal("role-gly", code);
    }

    [Fact]
    public async Task GenerateNoConflictReturnsBaseCode()
    {
        var generator = CreateGenerator();

        var code = await generator.GenerateAsync(
            "财务", "role", AlwaysAvailable,
            new EncodingGenerationOptions { Prefix = "role-" },
            TestContext.Current.CancellationToken);

        Assert.Equal("role-cw", code);
    }

    [Fact]
    public async Task GenerateConflictAppendsIncrementingSequence()
    {
        var generator = CreateGenerator();
        var used = new HashSet<string>();

        Func<string, CancellationToken, Task<bool>> checker = (candidate, _) =>
        {
            bool exists = used.Contains(candidate);
            used.Add(candidate);
            return Task.FromResult(exists);
        };

        var first = await generator.GenerateAsync("财务", "role", checker,
            new EncodingGenerationOptions { Prefix = "role-" }, TestContext.Current.CancellationToken);
        Assert.Equal("role-cw", first);

        var second = await generator.GenerateAsync("财务", "role", checker,
            new EncodingGenerationOptions { Prefix = "role-" }, TestContext.Current.CancellationToken);
        Assert.Equal("role-cw-001", second);

        var third = await generator.GenerateAsync("财务", "role", checker,
            new EncodingGenerationOptions { Prefix = "role-" }, TestContext.Current.CancellationToken);
        Assert.Equal("role-cw-002", third);
    }

    [Fact]
    public async Task GenerateSequenceDigitsControlsPadding()
    {
        var generator = CreateGenerator();
        var used = new HashSet<string>();
        Func<string, CancellationToken, Task<bool>> checker = (candidate, _) =>
        {
            bool exists = used.Contains(candidate);
            used.Add(candidate);
            return Task.FromResult(exists);
        };

        var options = new EncodingGenerationOptions { Prefix = "role-", SequenceDigits = 5 };
        await generator.GenerateAsync("财务", "role", checker, options, TestContext.Current.CancellationToken);
        var second = await generator.GenerateAsync("财务", "role", checker, options, TestContext.Current.CancellationToken);

        Assert.Equal("role-cw-00001", second);
    }

    [Fact]
    public async Task GenerateMaxLengthTruncatesBasePinyin()
    {
        var generator = CreateGenerator();

        var code = await generator.GenerateAsync(
            "管理员", "portal", AlwaysAvailable,
            new EncodingGenerationOptions
            {
                Prefix = "portal.",
                PinyinMode = PinyinMode.Full,
                MaxLength = 12,
            },
            TestContext.Current.CancellationToken);

        Assert.StartsWith("portal.", code);
        Assert.True(code.Length <= 12);
    }

    [Fact]
    public async Task GenerateKeepsNonChineseLettersAndDigits()
    {
        var generator = CreateGenerator();

        var code = await generator.GenerateAsync(
            "VIP管理员", "role", AlwaysAvailable,
            new EncodingGenerationOptions { PinyinMode = PinyinMode.Initials },
            TestContext.Current.CancellationToken);

        Assert.Equal("vipgly", code);
    }

    [Fact]
    public async Task GenerateStripsInvalidCharacters()
    {
        var generator = CreateGenerator();

        var code = await generator.GenerateAsync(
            "管理 员!", "role", AlwaysAvailable,
            new EncodingGenerationOptions { PinyinMode = PinyinMode.Initials },
            TestContext.Current.CancellationToken);

        Assert.Equal("gly", code);
    }

    [Fact]
    public async Task GenerateMaxRetriesExhaustedThrows()
    {
        var generator = CreateGenerator();

        Func<string, CancellationToken, Task<bool>> alwaysConflict = static (_, _) => Task.FromResult(true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(
            "财务", "role", alwaysConflict,
            new EncodingGenerationOptions { Prefix = "role-", MaxRetries = 3 },
            TestContext.Current.CancellationToken));

        Assert.Contains("无法为", ex.Message);
    }

    [Fact]
    public async Task GenerateNullSourceNameThrows()
    {
        var generator = CreateGenerator();
        await Assert.ThrowsAsync<ArgumentNullException>(() => generator.GenerateAsync(
            null!, "role", AlwaysAvailable, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GenerateEmptyScopeThrows()
    {
        var generator = CreateGenerator();
        await Assert.ThrowsAsync<ArgumentException>(() => generator.GenerateAsync(
            "管理员", " ", AlwaysAvailable, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GenerateNullCheckerThrows()
    {
        var generator = CreateGenerator();
        await Assert.ThrowsAsync<ArgumentNullException>(() => generator.GenerateAsync(
            "管理员", "role", null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GenerateDefaultOptionsUsesInitialsAnd3Digits()
    {
        var generator = CreateGenerator();
        var used = new HashSet<string>();
        Func<string, CancellationToken, Task<bool>> checker = (candidate, _) =>
        {
            bool exists = used.Contains(candidate);
            used.Add(candidate);
            return Task.FromResult(exists);
        };

        await generator.GenerateAsync("财务", "role", checker, cancellationToken: TestContext.Current.CancellationToken);
        var second = await generator.GenerateAsync("财务", "role", checker, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("cw-001", second);
    }

    [Fact]
    public async Task GenerateSeparatorConfigurable()
    {
        var generator = CreateGenerator();
        var used = new HashSet<string>();
        Func<string, CancellationToken, Task<bool>> checker = (candidate, _) =>
        {
            bool exists = used.Contains(candidate);
            used.Add(candidate);
            return Task.FromResult(exists);
        };

        var options = new EncodingGenerationOptions { Prefix = "pos_", Separator = "_" };
        await generator.GenerateAsync("会计", "position", checker, options, TestContext.Current.CancellationToken);
        var second = await generator.GenerateAsync("会计", "position", checker, options, TestContext.Current.CancellationToken);

        Assert.Equal("pos_kj_001", second);
    }
}
