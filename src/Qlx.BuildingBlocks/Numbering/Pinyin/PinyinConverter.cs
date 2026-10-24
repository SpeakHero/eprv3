/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：拼音转换默认实现，基于 ToolGood.Words.Pinyin，支持首字母与完整拼音
 */

using System.Globalization;
using ToolGood.Words.Pinyin;

namespace Qlx.BuildingBlocks.Numbering.Pinyin;

/// <summary>
/// 拼音转换默认实现。基于 <c>ToolGood.Words.Pinyin</c> 库，
/// 支持 CJK 统一汉字（0x3400~0x9FA5）的拼音转换，多音字按常用发音处理。
/// 结果统一转为小写，非中文字符按原样保留。
/// </summary>
public sealed class PinyinConverter : IPinyinConverter
{
    /// <inheritdoc />
    public string GetInitials(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return string.Empty;
        }

        // WordsHelper.GetFirstPinyin 返回大写首字母串（如 GLY），统一转小写
        return WordsHelper.GetFirstPinyin(text).ToLowerInvariant();
    }

    /// <inheritdoc />
    public string GetFullPinyin(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return string.Empty;
        }

        // WordsHelper.GetPinyin 返回带大小写的完整拼音（如 GuanLiYuan），统一转小写
        return WordsHelper.GetPinyin(text, tone: false).ToLowerInvariant();
    }
}
