/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 20:05:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 20:05:00 +08:00 中国标准时间 星期五
 * 描述：基于公共层预编译正则的 DataAnnotations 校验特性——DTO 参数格式校验统一入口，替代零散 [RegularExpression] 与手写正则校验
 */

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Qlx.BuildingBlocks.Validation;

/// <summary>
/// 基于预编译正则的校验特性基类。子类经 <see cref="Pattern"/> 提供源生成预编译正则（禁止每次验证临时构造）。
/// <para>语义与内置 <see cref="RegularExpressionAttribute"/> 对齐：null 放行（空值归 <see cref="RequiredAttribute"/> 管辖），
/// 非字符串值经 <see cref="Convert.ToString(object?, IFormatProvider?)"/>（InvariantCulture）兜底后匹配。</para>
/// </summary>
public abstract class RegexValidationAttribute : ValidationAttribute
{
    /// <summary>以默认错误消息初始化。</summary>
    /// <param name="defaultErrorMessage">默认错误消息模板（支持 {0} 字段名占位）。</param>
    protected RegexValidationAttribute(string defaultErrorMessage) => ErrorMessage = defaultErrorMessage;

    /// <summary>预编译正则（子类必须返回源生成 / 静态缓存实例）。</summary>
    protected abstract Regex Pattern { get; }

    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        var text = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return Pattern.IsMatch(text)
            ? ValidationResult.Success
            : new ValidationResult(FormatErrorMessage(validationContext.DisplayName));
    }
}

/// <summary>中国大陆手机号格式校验（宽松：1 开头共 11 位数字）。</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class CnMobilePhoneAttribute() : RegexValidationAttribute("字段 {0} 不是有效的中国大陆手机号")
{
    /// <inheritdoc />
    protected override Regex Pattern => CommonValidationPatterns.CnMobilePhone;
}

/// <summary>邮箱格式校验（本地段@域名.后缀）。</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class EmailFormatAttribute() : RegexValidationAttribute("字段 {0} 不是有效的邮箱地址")
{
    /// <inheritdoc />
    protected override Regex Pattern => CommonValidationPatterns.Email;
}

/// <summary>中国大陆身份证号格式校验（仅格式：15 位数字或 18 位末位可 X，不含校验位算法）。</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class CnIdCardAttribute() : RegexValidationAttribute("字段 {0} 不是有效的中国大陆身份证号")
{
    /// <inheritdoc />
    protected override Regex Pattern => CommonValidationPatterns.CnIdCard;
}

/// <summary>IPv4 地址格式校验。</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class IPv4AddressAttribute() : RegexValidationAttribute("字段 {0} 不是有效的 IPv4 地址")
{
    /// <inheritdoc />
    protected override Regex Pattern => CommonValidationPatterns.IPv4;
}

/// <summary>URL 格式校验（仅 http/https）。</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class UrlFormatAttribute() : RegexValidationAttribute("字段 {0} 不是有效的 URL（仅支持 http/https）")
{
    /// <inheritdoc />
    protected override Regex Pattern => CommonValidationPatterns.Url;
}

/// <summary>统一社会信用代码（USCC）格式校验（18 位大写字母或数字）。</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class CnUsccAttribute() : RegexValidationAttribute("字段 {0} 不是有效的统一社会信用代码（须为 18 位大写字母或数字）")
{
    /// <inheritdoc />
    protected override Regex Pattern => CommonValidationPatterns.CnUscc;
}
