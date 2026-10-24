/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 00:15:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 00:15:00 +08:00 中国标准时间 星期三
 * 描述：敏感数据脱敏静态工具，内置手机号/身份证号/银行卡号/邮箱规则，支持预编译正则自定义扩展
 */

using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Qlx.BuildingBlocks.Utilities;

/// <summary>
/// 敏感数据类型（内置脱敏规则标识）
/// </summary>
public enum SensitiveDataType
{
    /// <summary>中国大陆手机号：保留前 3 位 + 后 4 位，中间 4 位星号（138****8000）</summary>
    Phone,

    /// <summary>身份证号：保留前 6 位 + 后 3~4 位，中间按实际长度星号（兼容 15 / 18 位）</summary>
    IdCard,

    /// <summary>银行卡号：保留前 4 位 + 后 4 位，中间按实际长度星号</summary>
    BankCard,

    /// <summary>邮箱：本地段保留至多前 3 个字符 + 3 星号 + 完整域名（spe***@qq.com）</summary>
    Email
}

/// <summary>
/// 敏感数据脱敏器（静态工具，线程安全）
/// </summary>
/// <remarks>
/// <para>内置手机号 / 身份证号 / 银行卡号 / 邮箱四类规则，全部基于源生成预编译正则。</para>
/// <para>自定义规则经 <see cref="RegisterRule"/> 注册，其正则必须启用 <see cref="RegexOptions.Compiled"/> 预编译选项，否则拒绝注册。</para>
/// <para>输入不匹配规则（非法格式）时按原长度全星返回，确保任何场景不泄露原文；null / 空白输入返回 <see cref="string.Empty"/>。</para>
/// <para>线程安全：内置规则为冻结字典只读；自定义规则注册经锁 + 写时复制完成，读取无锁。</para>
/// </remarks>
public static partial class SensitiveDataMasker
{
    /// <summary>默认脱敏字符</summary>
    public const char DefaultMaskChar = '*';

    /// <summary>内置规则正则（源生成预编译）</summary>
    private static readonly FrozenDictionary<SensitiveDataType, Regex> s_builtInPatterns = new Dictionary<SensitiveDataType, Regex>
    {
        [SensitiveDataType.Phone] = PhonePattern(),
        [SensitiveDataType.IdCard] = IdCardPattern(),
        [SensitiveDataType.BankCard] = BankCardPattern(),
        [SensitiveDataType.Email] = EmailPattern(),
    }.ToFrozenDictionary();

    /// <summary>自定义规则写时复制锁</summary>
    private static readonly object s_syncRoot = new();

    /// <summary>自定义规则表（冻结字典，写时复制）</summary>
    private static volatile FrozenDictionary<string, CustomMaskRule> s_customRules =
        FrozenDictionary<string, CustomMaskRule>.Empty;

    /// <summary>
    /// 按内置敏感数据类型脱敏
    /// </summary>
    /// <param name="value">原始值，允许 null</param>
    /// <param name="type">敏感数据类型</param>
    /// <returns>脱敏结果；null / 空白输入返回 <see cref="string.Empty"/>；格式不匹配时返回等长全星串</returns>
    public static string Mask(string? value, SensitiveDataType type)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Match match = s_builtInPatterns[type].Match(value);
        if (!match.Success)
        {
            return new string(DefaultMaskChar, value.Length);
        }

        return type switch
        {
            SensitiveDataType.Phone => $"{match.Groups["prefix"].Value}****{match.Groups["suffix"].Value}",
            SensitiveDataType.IdCard or SensitiveDataType.BankCard =>
                $"{match.Groups["prefix"].Value}{new string(DefaultMaskChar, match.Groups["mask"].Length)}{match.Groups["suffix"].Value}",
            SensitiveDataType.Email => $"{match.Groups["prefix"].Value}***{match.Groups["domain"].Value}",
            _ => new string(DefaultMaskChar, value.Length),
        };
    }

    /// <summary>
    /// 按规则名称脱敏（内置类型名或已注册的自定义规则名）
    /// </summary>
    /// <param name="value">原始值，允许 null</param>
    /// <param name="ruleName">规则名：内置规则为 Phone / IdCard / BankCard / Email（大小写不敏感），或已注册的自定义规则名</param>
    /// <returns>脱敏结果；null / 空白输入返回 <see cref="string.Empty"/>；格式不匹配时返回等长全星串</returns>
    /// <exception cref="ArgumentException">规则名为空或未注册时抛出</exception>
    public static string Mask(string? value, string ruleName)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
        {
            throw new ArgumentException("脱敏规则名不能为空", nameof(ruleName));
        }

        if (Enum.TryParse(ruleName, true, out SensitiveDataType builtIn) && Enum.IsDefined(builtIn))
        {
            return Mask(value, builtIn);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (s_customRules.TryGetValue(ruleName, out CustomMaskRule? rule))
        {
            Match match = rule.Pattern.Match(value);
            return match.Success ? match.Result(rule.Replacement) : new string(DefaultMaskChar, value.Length);
        }

        throw new ArgumentException($"未注册的脱敏规则：{ruleName}", nameof(ruleName));
    }

    /// <summary>
    /// 注册（或覆盖）自定义脱敏规则
    /// </summary>
    /// <param name="ruleName">规则名（大小写不敏感，禁止与内置类型同名）</param>
    /// <param name="pattern">匹配正则：必须启用 <see cref="RegexOptions.Compiled"/> 预编译选项，建议配置匹配超时</param>
    /// <param name="replacement">替换表达式，支持 ${分组名} 引用，如 "${prefix}****${suffix}"</param>
    /// <exception cref="ArgumentException">参数非法、与内置规则同名或正则未启用 <see cref="RegexOptions.Compiled"/> 时抛出</exception>
    /// <exception cref="ArgumentNullException">pattern / replacement 为 null 时抛出</exception>
    public static void RegisterRule(string ruleName, Regex pattern, string replacement)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
        {
            throw new ArgumentException("脱敏规则名不能为空", nameof(ruleName));
        }

        if (Enum.TryParse(ruleName, true, out SensitiveDataType builtIn) && Enum.IsDefined(builtIn))
        {
            throw new ArgumentException($"不允许覆盖内置脱敏规则：{ruleName}", nameof(ruleName));
        }

        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(replacement);

        if (!pattern.Options.HasFlag(RegexOptions.Compiled))
        {
            throw new ArgumentException(
                "自定义脱敏正则必须启用 RegexOptions.Compiled 预编译选项",
                nameof(pattern));
        }

        lock (s_syncRoot)
        {
            Dictionary<string, CustomMaskRule> snapshot = new(s_customRules, StringComparer.OrdinalIgnoreCase)
            {
                [ruleName] = new CustomMaskRule(pattern, replacement)
            };
            s_customRules = snapshot.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 通用定长保留脱敏：保留头部与尾部指定字符数，中间按原始长度星号替换
    /// </summary>
    /// <param name="value">原始值，允许 null</param>
    /// <param name="keepPrefix">头部保留字符数（≥ 0）</param>
    /// <param name="keepSuffix">尾部保留字符数（≥ 0）</param>
    /// <param name="maskChar">脱敏字符，默认 <see cref="DefaultMaskChar"/></param>
    /// <returns>脱敏结果；长度不足保留位数之和时返回等长全星串；null / 空白输入返回 <see cref="string.Empty"/></returns>
    /// <exception cref="ArgumentOutOfRangeException">keepPrefix / keepSuffix 为负数时抛出</exception>
    public static string MaskPartial(string? value, int keepPrefix, int keepSuffix, char maskChar = DefaultMaskChar)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(keepPrefix);
        ArgumentOutOfRangeException.ThrowIfNegative(keepSuffix);

        if (value.Length <= keepPrefix + keepSuffix)
        {
            return new string(maskChar, value.Length);
        }

        return string.Concat(
            value.AsSpan(0, keepPrefix),
            new string(maskChar, value.Length - keepPrefix - keepSuffix),
            value.AsSpan(value.Length - keepSuffix));
    }

    /// <summary>手机号：1 开头 11 位，前 3 + 后 4</summary>
    [GeneratedRegex(@"^(?<prefix>1[3-9]\d)\d{4}(?<suffix>\d{4})$", RegexOptions.Compiled | RegexOptions.CultureInvariant, 200)]
    private static partial Regex PhonePattern();

    /// <summary>身份证号：15 / 18 位，前 6 + 后 3~4（末位允许 X）</summary>
    [GeneratedRegex(@"^(?<prefix>\d{6})(?<mask>\d{5,8})(?<suffix>[\dXx]{3,4})$", RegexOptions.Compiled | RegexOptions.CultureInvariant, 200)]
    private static partial Regex IdCardPattern();

    /// <summary>银行卡号：纯数字 ≥ 10 位，前 4 + 后 4</summary>
    [GeneratedRegex(@"^(?<prefix>\d{4})(?<mask>\d{2,})(?<suffix>\d{4})$", RegexOptions.Compiled | RegexOptions.CultureInvariant, 200)]
    private static partial Regex BankCardPattern();

    /// <summary>邮箱：本地段保留至多前 3 字符 + 完整域名</summary>
    [GeneratedRegex(@"^(?<prefix>[A-Za-z0-9._%+-]{1,3})[A-Za-z0-9._%+-]*(?<domain>@.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant, 200)]
    private static partial Regex EmailPattern();

    /// <summary>自定义脱敏规则（预编译正则 + 替换表达式）</summary>
    /// <param name="Pattern">匹配正则（已强制 Compiled）</param>
    /// <param name="Replacement">替换表达式，支持 ${分组名} 引用</param>
    private sealed record CustomMaskRule(Regex Pattern, string Replacement);
}
