/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 20:15:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 20:15:00 +08:00 中国标准时间 星期五
 * 描述：基于预编译正则的 DataAnnotations 校验特性单元测试——null 放行 / 有效通过 / 无效报错消息
 */

using System.ComponentModel.DataAnnotations;
using Qlx.BuildingBlocks.Validation;

namespace Qlx.BuildingBlocks.Tests.Validation;

public sealed class CommonValidationAttributesTests
{
    private static readonly ValidationContext s_context = new(new object()) { DisplayName = "测试字段" };

    [Fact]
    public void NullValueIsAccepted()
    {
        // 空值归 [Required] 管辖，格式特性对 null 一律放行（与内置 RegularExpressionAttribute 语义对齐）
        Assert.Equal(ValidationResult.Success, new CnMobilePhoneAttribute().GetValidationResult(null, s_context));
        Assert.Equal(ValidationResult.Success, new EmailFormatAttribute().GetValidationResult(null, s_context));
        Assert.Equal(ValidationResult.Success, new CnIdCardAttribute().GetValidationResult(null, s_context));
        Assert.Equal(ValidationResult.Success, new IPv4AddressAttribute().GetValidationResult(null, s_context));
        Assert.Equal(ValidationResult.Success, new UrlFormatAttribute().GetValidationResult(null, s_context));
    }

    [Theory]
    [InlineData("13800138000", true)]
    [InlineData("23800138000", false)]
    public void CnMobilePhoneAttribute(string input, bool valid)
    {
        var result = new CnMobilePhoneAttribute().GetValidationResult(input, s_context);
        Assert.Equal(valid, result == ValidationResult.Success);
        if (!valid)
        {
            Assert.Contains("测试字段", result!.ErrorMessage);
        }
    }

    [Theory]
    [InlineData("a@b.com", true)]
    [InlineData("a@b", false)]
    public void EmailFormatAttribute(string input, bool valid)
        => Assert.Equal(valid, new EmailFormatAttribute().GetValidationResult(input, s_context) == ValidationResult.Success);

    [Theory]
    [InlineData("11010119900307777X", true)]
    [InlineData("12345", false)]
    public void CnIdCardAttribute(string input, bool valid)
        => Assert.Equal(valid, new CnIdCardAttribute().GetValidationResult(input, s_context) == ValidationResult.Success);

    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("10.0.0.256", false)]
    public void IPv4AddressAttribute(string input, bool valid)
        => Assert.Equal(valid, new IPv4AddressAttribute().GetValidationResult(input, s_context) == ValidationResult.Success);

    [Theory]
    [InlineData("https://erp.example.com/login", true)]
    [InlineData("ftp://x.com", false)]
    public void UrlFormatAttribute(string input, bool valid)
        => Assert.Equal(valid, new UrlFormatAttribute().GetValidationResult(input, s_context) == ValidationResult.Success);

    [Fact]
    public void NonStringValueIsConvertedBeforeMatching()
    {
        // 与内置 RegularExpressionAttribute 一致：非字符串值经 ToString 兜底后再匹配
        // 13800138000L → "13800138000"，命中宽松手机模式
        var result = new CnMobilePhoneAttribute().GetValidationResult(13800138000L, s_context);
        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void CustomErrorMessageOverridesDefault()
    {
        var attribute = new CnMobilePhoneAttribute { ErrorMessage = "自定义错误" };
        var result = attribute.GetValidationResult("bad", s_context);
        Assert.Equal("自定义错误", result!.ErrorMessage);
    }
}
