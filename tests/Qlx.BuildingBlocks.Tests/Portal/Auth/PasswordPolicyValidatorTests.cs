/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25
 * 描述：PasswordPolicyValidator 白盒测试——复杂度全分支（长度/字母/数字/特殊字符开关组合）
 *       与历史禁重全分支（N≤0 跳过、空历史跳过、空哈希跳过、命中拒绝、窗口外不检查）
 */

using Microsoft.AspNetCore.Identity;
using Qlx.Abstractions.Exceptions;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Domain.Identity;
using Qlx.Modules.Portal.Services;

namespace Qlx.BuildingBlocks.Tests.Portal.Auth;

public sealed class PasswordPolicyValidatorTests
{
    private static readonly PasswordHasher<PortalUser> Hasher = new();

    // ============ ValidateComplexity ============

    [Fact]
    public void ValidateComplexityNullOptionsThrows()
    {
        _ = Assert.Throws<ArgumentNullException>(
            () => PasswordPolicyValidator.ValidateComplexity("abc123", null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateComplexityNullOrWhitespaceThrows(string? password)
    {
        var ex = Assert.Throws<ValidationException>(
            () => PasswordPolicyValidator.ValidateComplexity(password!, new QlxAuthOptions()));
        Assert.Equal("newPassword", ex.FieldErrors[0].FieldName);
    }

    [Fact]
    public void ValidateComplexityShorterThanMinLengthThrows()
    {
        var options = new QlxAuthOptions { MinPasswordLength = 8 };

        var ex = Assert.Throws<ValidationException>(
            () => PasswordPolicyValidator.ValidateComplexity("Ab1!xyz", options)); // 7 位，边界-1
        Assert.Contains("至少 8 位", ex.FieldErrors[0].ErrorMessage);
    }

    [Fact]
    public void ValidateComplexityExactMinLengthPasses()
    {
        var options = new QlxAuthOptions { MinPasswordLength = 8 };

        // 恰好 8 位，边界值通过
        PasswordPolicyValidator.ValidateComplexity("Ab1!wxyz", options);
    }

    [Fact]
    public void ValidateComplexityRequireLetterMissingThrows()
    {
        var options = new QlxAuthOptions { PasswordRequireLetter = true };

        var ex = Assert.Throws<ValidationException>(
            () => PasswordPolicyValidator.ValidateComplexity("12345678", options));
        Assert.Contains("字母", ex.FieldErrors[0].ErrorMessage);
    }

    [Fact]
    public void ValidateComplexityRequireLetterOffPassesWithoutLetter()
    {
        var options = new QlxAuthOptions
        {
            PasswordRequireLetter = false,
            PasswordRequireDigit = false,
        };

        PasswordPolicyValidator.ValidateComplexity("12345678", options);
    }

    [Fact]
    public void ValidateComplexityRequireDigitMissingThrows()
    {
        var options = new QlxAuthOptions();

        var ex = Assert.Throws<ValidationException>(
            () => PasswordPolicyValidator.ValidateComplexity("abcdefgh", options));
        Assert.Contains("数字", ex.FieldErrors[0].ErrorMessage);
    }

    [Fact]
    public void ValidateComplexityRequireSpecialCharMissingThrows()
    {
        var options = new QlxAuthOptions { PasswordRequireSpecialChar = true };

        var ex = Assert.Throws<ValidationException>(
            () => PasswordPolicyValidator.ValidateComplexity("Abcdef12", options));
        Assert.Contains("特殊字符", ex.FieldErrors[0].ErrorMessage);
    }

    [Theory]
    [InlineData("Abcdef1!")] // 常见特殊字符
    [InlineData("Abcdef1@")]
    [InlineData("Abcdef1中")] // 中文按 \p{L} 归类为字母，不是特殊字符——应不命中
    public void ValidateSpecialCharClassification(string password)
    {
        var options = new QlxAuthOptions { PasswordRequireSpecialChar = true };

        if (password.EndsWith('中'))
        {
            // 汉字属于 \p{L}（字母），不满足特殊字符要求
            _ = Assert.Throws<ValidationException>(
                () => PasswordPolicyValidator.ValidateComplexity(password, options));
        }
        else
        {
            PasswordPolicyValidator.ValidateComplexity(password, options);
        }
    }

    [Fact]
    public void ValidateComplexityAllRulesSatisfiedPasses()
    {
        var options = new QlxAuthOptions { PasswordRequireSpecialChar = true };

        PasswordPolicyValidator.ValidateComplexity("Qlx@2026Safe", options);
    }

    // ============ ValidateNotReused ============

    private static PortalUserPasswordHistory HistoryOf(PortalUser user, string plainPassword, long id)
        => new(id, user.Id, Hasher.HashPassword(user, plainPassword));

    [Fact]
    public void ValidateNotReusedWithZeroNskips()
    {
        var user = new PortalUser("13800000001", "用户");

        // N=0 → 直接放行，即使历史中存在同密码
        PasswordPolicyValidator.ValidateNotReused(
            "same-pass", user, [HistoryOf(user, "same-pass", 1)], Hasher, 0);
    }

    [Fact]
    public void ValidateNotReusedWithEmptyHistorySkips()
    {
        var user = new PortalUser("13800000001", "用户");

        PasswordPolicyValidator.ValidateNotReused("any", user, [], Hasher, 3);
    }

    [Fact]
    public void ValidateNotReusedHitThrows()
    {
        var user = new PortalUser("13800000001", "用户");
        var histories = new[] { HistoryOf(user, "Old@Pass1", 1), HistoryOf(user, "Old@Pass2", 2) };

        var ex = Assert.Throws<ValidationException>(
            () => PasswordPolicyValidator.ValidateNotReused("Old@Pass2", user, histories, Hasher, 3));
        Assert.Contains("不能与最近 3 次", ex.FieldErrors[0].ErrorMessage);
    }

    [Fact]
    public void ValidateNotReusedOutsideWindowNotChecked()
    {
        var user = new PortalUser("13800000001", "用户");
        // 目标密码位于第 3 条历史，窗口 N=2 → 只检查前 2 条，不命中
        var histories = new[]
        {
            HistoryOf(user, "Recent1", 1),
            HistoryOf(user, "Recent2", 2),
            HistoryOf(user, "Target@9", 3),
        };

        PasswordPolicyValidator.ValidateNotReused("Target@9", user, histories, Hasher, 2);
    }

    [Fact]
    public void ValidateNotReusedWindowLargerThanHistoryChecksAll()
    {
        var user = new PortalUser("13800000001", "用户");
        var histories = new[] { HistoryOf(user, "Old@Pass1", 1) };

        // N=5 但历史只有 1 条 → checkedCount = 1，命中即拒
        _ = Assert.Throws<ValidationException>(
            () => PasswordPolicyValidator.ValidateNotReused("Old@Pass1", user, histories, Hasher, 5));
    }

    [Fact]
    public void ValidateNotReusedSkipsBlankHashEntries()
    {
        var user = new PortalUser("13800000001", "用户");
        // 历史记录哈希为空（外部 IdP 用户场景）：跳过该条不崩溃
        var blank = new PortalUserPasswordHistory(9, user.Id, "placeholder");
        typeof(PortalUserPasswordHistory)
            .GetProperty(nameof(PortalUserPasswordHistory.PasswordHash))!
            .SetValue(blank, string.Empty);
        var histories = new[] { blank, HistoryOf(user, "Real@Pass1", 2) };

        PasswordPolicyValidator.ValidateNotReused("New@Pass9", user, histories, Hasher, 3);

        // 空哈希被跳过后，后续真实哈希仍被检查 → 命中拒绝
        _ = Assert.Throws<ValidationException>(
            () => PasswordPolicyValidator.ValidateNotReused("Real@Pass1", user, histories, Hasher, 3));
    }

    [Fact]
    public void ValidateNotReusedNewPasswordPasses()
    {
        var user = new PortalUser("13800000001", "用户");
        var histories = new[] { HistoryOf(user, "Old@Pass1", 1), HistoryOf(user, "Old@Pass2", 2) };

        PasswordPolicyValidator.ValidateNotReused("Brand@New9", user, histories, Hasher, 3);
    }
}

