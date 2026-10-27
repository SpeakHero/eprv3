/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 20:05:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 20:05:00 +08:00 中国标准时间 星期五
 * 描述：通用校验正则公共层——跨模块通用格式校验（手机号/邮箱/身份证/IPv4/URL/统一社会信用代码）统一定义，全部源生成预编译
 */

using System.Text.RegularExpressions;

namespace Qlx.BuildingBlocks.Validation;

/// <summary>
/// 跨模块通用校验正则（全部经 <see cref="GeneratedRegexAttribute"/> 源生成预编译，统一 200ms 匹配超时防 ReDoS）。
/// <para>业务代码禁止各自重复定义通用格式正则，统一经本类引用；
/// DataAnnotations 校验场景使用 <see cref="RegexValidationAttribute"/> 派生特性。</para>
/// <para>匹配语义约定：null / 空白输入的放行与否由 <c>IsXxx</c> 方法逐一注明；所有模式均为整串匹配（含 ^ $ 锚点）。</para>
/// <para>当前覆盖：中国大陆手机号 / 邮箱 / 中国大陆身份证号 / IPv4 / URL / 统一社会信用代码。</para>
/// </summary>
public static partial class CommonValidationPatterns
{
    /// <summary>中国大陆手机号（宽松模式：1 开头共 11 位数字，与登录账号校验场景既有语义一致）。</summary>
    public static Regex CnMobilePhone => CnMobilePhoneRegex();

    /// <summary>邮箱（宽松模式：本地段@域名.后缀，各段不含空白与 @，与登录账号校验场景既有语义一致）。</summary>
    public static Regex Email => EmailRegex();

    /// <summary>中国大陆身份证号（仅格式：15 位纯数字，或 18 位且末位允许 X/x；不含校验位算法）。</summary>
    public static Regex CnIdCard => CnIdCardRegex();

    /// <summary>IPv4 地址（四段点分十进制，每段 0~255，不允许前导零以外的非法形式）。</summary>
    public static Regex IPv4 => IPv4Regex();

    /// <summary>URL（仅 http/https：协议头 + 域名段 + 可选端口 + 可选路径，不含空白）。</summary>
    public static Regex Url => UrlRegex();

    /// <summary>统一社会信用代码（18 位大写字母或数字，与门户租户落库既有规则一致）。</summary>
    public static Regex CnUscc => CnUsccRegex();

    /// <summary>判定是否为中国大陆手机号；null / 空白返回 false。</summary>
    public static bool IsCnMobilePhone(string? value) =>
        !string.IsNullOrWhiteSpace(value) && CnMobilePhone.IsMatch(value);

    /// <summary>判定是否为邮箱格式；null / 空白返回 false。</summary>
    public static bool IsEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Email.IsMatch(value);

    /// <summary>判定是否为中国大陆身份证号（仅格式）；null / 空白返回 false。</summary>
    public static bool IsCnIdCard(string? value) =>
        !string.IsNullOrWhiteSpace(value) && CnIdCard.IsMatch(value);

    /// <summary>判定是否为 IPv4 地址；null / 空白返回 false。</summary>
    public static bool IsIPv4(string? value) =>
        !string.IsNullOrWhiteSpace(value) && IPv4.IsMatch(value);

    /// <summary>判定是否为 http/https URL；null / 空白返回 false。</summary>
    public static bool IsUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Url.IsMatch(value);

    /// <summary>判定是否为统一社会信用代码；null / 空白返回 false。</summary>
    public static bool IsCnUscc(string? value) =>
        !string.IsNullOrWhiteSpace(value) && CnUscc.IsMatch(value);

    [GeneratedRegex(@"^1\d{10}$", RegexOptions.CultureInvariant, 200)]
    private static partial Regex CnMobilePhoneRegex();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant, 200)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^\d{15}$|^\d{17}[\dXx]$", RegexOptions.CultureInvariant, 200)]
    private static partial Regex CnIdCardRegex();

    [GeneratedRegex(@"^((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)$", RegexOptions.CultureInvariant, 200)]
    private static partial Regex IPv4Regex();

    [GeneratedRegex(@"^https?://[\w\-]+(\.[\w\-]+)*(:\d{1,5})?(/[^\s]*)?$", RegexOptions.CultureInvariant, 200)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"^[0-9A-Z]{18}$", RegexOptions.CultureInvariant, 200)]
    private static partial Regex CnUsccRegex();
}
