/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：拼音转换器 PinyinConverter 单元测试
 *       覆盖：首字母提取、完整拼音提取、大小写统一、空串处理、非中文字符保留
 */

using Qlx.BuildingBlocks.Numbering.Pinyin;

namespace Qlx.BuildingBlocks.Tests.Numbering;

public sealed class PinyinConverterTests
{
    private static readonly PinyinConverter Converter = new();

    [Fact]
    public void GetInitialsReturnsLowerCaseFirstLetters()
    {
        Assert.Equal("gly", Converter.GetInitials("管理员"));
    }

    [Fact]
    public void GetInitialsMultipleWords()
    {
        Assert.Equal("cw", Converter.GetInitials("财务"));
    }

    [Fact]
    public void GetFullPinyinReturnsLowerCaseFullPinyin()
    {
        Assert.Equal("guanliyuan", Converter.GetFullPinyin("管理员"));
    }

    [Fact]
    public void GetFullPinyinMultipleWords()
    {
        Assert.Equal("caiwu", Converter.GetFullPinyin("财务"));
    }

    [Fact]
    public void GetInitialsEmptyStringReturnsEmpty()
    {
        Assert.Equal(string.Empty, Converter.GetInitials(string.Empty));
    }

    [Fact]
    public void GetFullPinyinEmptyStringReturnsEmpty()
    {
        Assert.Equal(string.Empty, Converter.GetFullPinyin(string.Empty));
    }

    [Fact]
    public void GetInitialsPreservesNonChineseAscii()
    {
        Assert.Equal("vipgly", Converter.GetInitials("VIP管理员"));
    }

    [Fact]
    public void GetFullPinyinPreservesNonChineseAscii()
    {
        Assert.Equal("vipguanliyuan", Converter.GetFullPinyin("VIP管理员"));
    }

    [Fact]
    public void GetInitialsNullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => Converter.GetInitials(null!));
    }

    [Fact]
    public void GetFullPinyinNullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => Converter.GetFullPinyin(null!));
    }

    [Fact]
    public void GetInitialsAllLowercase()
    {
        string result = Converter.GetInitials("重庆");
        Assert.All(result, c => Assert.True(char.IsLower(c) || char.IsDigit(c), $"字符 '{c}' 不是小写"));
    }
}
