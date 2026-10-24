/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：拼音转换契约——将中文名称转换为拼音首字母或完整拼音，作为业务编码生成的基础
 */

namespace Qlx.BuildingBlocks.Numbering.Pinyin;

/// <summary>
/// 拼音转换契约。将中文字符串转换为拼音形式，用于业务编码自动生成。
/// 非中文字符（字母、数字、符号）按原样保留或按规则过滤。
/// </summary>
public interface IPinyinConverter
{
    /// <summary>
    /// 获取文本的拼音首字母串（如「管理员」→ "gly"）。
    /// 非中文字符按原样保留，结果统一小写。
    /// </summary>
    /// <param name="text">源文本。</param>
    /// <returns>拼音首字母串（小写）。</returns>
    string GetInitials(string text);

    /// <summary>
    /// 获取文本的完整拼音串（如「管理员」→ "guanliyuan"）。
    /// 非中文字符按原样保留，结果统一小写。
    /// </summary>
    /// <param name="text">源文本。</param>
    /// <returns>完整拼音串（小写）。</returns>
    string GetFullPinyin(string text);
}
