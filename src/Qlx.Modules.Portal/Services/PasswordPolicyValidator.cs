/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：密码复杂度与历史禁重策略校验器——复杂度规则从 QlxAuthOptions 读取，历史禁重用 VerifyHashedPassword 逐个对比。
 */

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Exceptions;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Domain.Identity;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 密码策略校验器。复杂度规则来自 <see cref="QlxAuthOptions"/>，
/// 历史禁重遍历历史密码哈希逐个调用 <see cref="IPasswordHasher{TUser}.VerifyHashedPassword"/> 匹配（PBKDF2 每次哈希不同，禁止直接比哈希）。
/// </summary>
public static partial class PasswordPolicyValidator
{
    /// <summary>特殊字符判定：非字母 / 数字 / 空白即视为特殊字符（源生成预编译，1 秒匹配超时）。</summary>
    [GeneratedRegex(@"[^\p{L}\p{N}\s]", RegexOptions.Compiled | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SpecialCharRegex();

    /// <summary>校验新密码复杂度（从 AuthOptions 读取规则）。</summary>
    /// <exception cref="ValidationException">复杂度不满足时抛出。</exception>
    public static void ValidateComplexity(string password, QlxAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(password) || password.Length < options.MinPasswordLength)
        {
            throw new ValidationException("newPassword",
                $"新密码至少 {options.MinPasswordLength} 位");
        }

        if (options.PasswordRequireLetter && !password.Any(char.IsAsciiLetter))
        {
            throw new ValidationException("newPassword", "新密码必须包含字母");
        }

        if (options.PasswordRequireDigit && !password.Any(char.IsAsciiDigit))
        {
            throw new ValidationException("newPassword", "新密码必须包含数字");
        }

        if (options.PasswordRequireSpecialChar && !SpecialCharRegex().IsMatch(password))
        {
            throw new ValidationException("newPassword", "新密码必须包含特殊字符");
        }
    }

    /// <summary>校验新密码不与最近 N 次历史密码相同（N=0 时跳过）。</summary>
    /// <exception cref="ValidationException">命中历史密码时抛出。</exception>
    public static void ValidateNotReused(
        string newPassword,
        PortalUser user,
        IReadOnlyList<PortalUserPasswordHistory> histories,
        IPasswordHasher<PortalUser> hasher,
        int mustNotRepeatLastN)
    {
        if (mustNotRepeatLastN <= 0 || histories.Count == 0)
        {
            return;
        }

        var checkedCount = Math.Min(mustNotRepeatLastN, histories.Count);
        for (var i = 0; i < checkedCount; i++)
        {
            var hash = histories[i].PasswordHash;
            if (string.IsNullOrWhiteSpace(hash))
            {
                continue;
            }

            var result = hasher.VerifyHashedPassword(user, hash, newPassword);
            if (result == PasswordVerificationResult.Success
                || result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                throw new ValidationException("newPassword",
                    $"新密码不能与最近 {mustNotRepeatLastN} 次历史密码相同");
            }
        }
    }
}
