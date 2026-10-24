/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：编码生成配置选项——可配置前缀、拼音模式、序号位数、最大长度等
 */

namespace Qlx.BuildingBlocks.Numbering;

/// <summary>
/// 拼音提取模式。
/// </summary>
public enum PinyinMode
{
    /// <summary>拼音首字母（如「管理员」→ gly）。</summary>
    Initials,

    /// <summary>完整拼音（如「管理员」→ guanliyuan）。</summary>
    Full,
}

/// <summary>
/// 编码生成配置选项。所有属性为不可变 init，遵循强类型 Options 规范。
/// 用于 <see cref="IEncodingGenerator.GenerateAsync"/> 的生成行为定制。
/// </summary>
public sealed record EncodingGenerationOptions
{
    /// <summary>默认序号位数（3 位，如 001）。</summary>
    public const int DefaultSequenceDigits = 3;

    /// <summary>默认最大编码长度。</summary>
    public const int DefaultMaxLength = 64;

    /// <summary>序号与基础编码之间的默认分隔符。</summary>
    public const string DefaultSeparator = "-";

    /// <summary>编码前缀（如 "role-"、"portal."、"pos-"），可为空。</summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>拼音提取模式（首字母 / 完整拼音），默认首字母。</summary>
    public PinyinMode PinyinMode { get; init; } = PinyinMode.Initials;

    /// <summary>重复时追加的序号位数（默认 3，如 001、002）。</summary>
    public int SequenceDigits { get; init; } = DefaultSequenceDigits;

    /// <summary>编码最大长度（含前缀与序号），超出截断基础拼音部分。</summary>
    public int MaxLength { get; init; } = DefaultMaxLength;

    /// <summary>基础编码与序号之间的分隔符（默认 "-"）。</summary>
    public string Separator { get; init; } = DefaultSeparator;

    /// <summary>是否将结果统一转为小写（默认 true）。</summary>
    public bool Lowercase { get; init; } = true;

    /// <summary>是否保留源文本中的非中文字符（字母/数字），默认 true。</summary>
    public bool KeepNonChinese { get; init; } = true;

    /// <summary>最大重试次数（序号递增去重的循环上限，防止极端冲突导致死循环）。</summary>
    public int MaxRetries { get; init; } = 100;
}
