/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：通用业务编码生成器默认实现——拼音提取 + 冲突递增序号去重
 */

using System.Globalization;
using System.Text;
using Qlx.BuildingBlocks.Numbering.Pinyin;

namespace Qlx.BuildingBlocks.Numbering;

/// <summary>
/// 通用业务编码生成器默认实现。
/// <para>
/// 生成流程：
/// 1. 从业务名称提取拼音（首字母或完整拼音）作为编码基础；
/// 2. 拼接前缀、规范化（小写、过滤非法字符）、按最大长度截断；
/// 3. 通过业务方提供的唯一性校验委托检查编码是否已存在；
/// 4. 若冲突，经 <see cref="ISerialSequenceStore"/> 原子取号，追加递增序号（如 001、002）后重试；
/// 5. 序号键按 <c>encoding:{scope}:{base}</c> 隔离，不同业务域 / 不同基础编码互不干扰。
/// </para>
/// </summary>
public sealed class EncodingGenerator(
    IPinyinConverter pinyinConverter,
    ISerialSequenceStore sequenceStore) : IEncodingGenerator
{
    /// <summary>序号键前缀，与单据编号等其他序号域隔离。</summary>
    private const string SequenceKeyPrefix = "encoding";

    /// <summary>编码允许的字符集：小写字母、数字、短横线、下划线、点。</summary>
    private static readonly char[] AllowedChars =
    [
        'a','b','c','d','e','f','g','h','i','j','k','l','m',
        'n','o','p','q','r','s','t','u','v','w','x','y','z',
        '0','1','2','3','4','5','6','7','8','9',
        '-','_','.',
    ];

    /// <inheritdoc />
    public async Task<string> GenerateAsync(
        string sourceName,
        string scope,
        Func<string, CancellationToken, Task<bool>> uniquenessChecker,
        EncodingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(uniquenessChecker);

        options ??= new EncodingGenerationOptions();
        ValidateOptions(options);

        // 1. 提取拼音
        string pinyin = options.PinyinMode == PinyinMode.Full
            ? pinyinConverter.GetFullPinyin(sourceName)
            : pinyinConverter.GetInitials(sourceName);

        // 2. 规范化：小写 + 过滤非法字符
        string normalized = Normalize(pinyin, options);

        // 3. 拼接前缀，得到基础编码
        string baseCode = (options.Prefix + normalized).Trim();

        // 4. 按最大长度截断基础编码（需为后续序号预留空间）
        int reserveLen = options.Separator.Length + options.SequenceDigits;
        int maxBaseLen = Math.Max(1, options.MaxLength - reserveLen);
        if (baseCode.Length > maxBaseLen)
        {
            baseCode = baseCode[..maxBaseLen];
        }

        // 5. 尝试基础编码（无序号）
        if (!await uniquenessChecker(baseCode, cancellationToken).ConfigureAwait(false))
        {
            return baseCode;
        }

        // 6. 冲突 → 递增序号去重
        string sequenceKey = $"{SequenceKeyPrefix}:{scope}:{baseCode}";
        for (int attempt = 0; attempt < options.MaxRetries; attempt++)
        {
            long serial = await sequenceStore.GetNextAsync(sequenceKey, cancellationToken).ConfigureAwait(false);
            string serialText = serial.ToString(CultureInfo.InvariantCulture)
                .PadLeft(options.SequenceDigits, '0');
            string candidate = $"{baseCode}{options.Separator}{serialText}";

            if (!await uniquenessChecker(candidate, cancellationToken).ConfigureAwait(false))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"编码生成失败：在 {options.MaxRetries} 次重试后仍无法为「{sourceName}」生成唯一编码（scope={scope}, base={baseCode}）");
    }

    /// <summary>
    /// 规范化拼音串：统一小写、过滤非允许字符、压缩连续分隔符。
    /// </summary>
    private static string Normalize(string pinyin, EncodingGenerationOptions options)
    {
        if (pinyin.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(pinyin.Length);
        foreach (char ch in pinyin)
        {
            char c = options.Lowercase ? char.ToLowerInvariant(ch) : ch;

            if (Array.IndexOf(AllowedChars, c) >= 0)
            {
                sb.Append(c);
            }
            else if (options.KeepNonChinese && char.IsAsciiLetterOrDigit(c))
            {
                // 非中文字符（已由拼音库保留的字母数字）直接保留
                sb.Append(c);
            }
            // 其他字符（空格、标点、声调符号等）丢弃
        }

        string result = sb.ToString();

        // 压缩连续的分隔符（如 --- → -），并去除首尾分隔符
        result = CompressSeparators(result);

        return result;
    }

    /// <summary>压缩连续的短横线/下划线/点，并去除首尾的分隔字符。</summary>
    private static string CompressSeparators(string input)
    {
        if (input.Length == 0)
        {
            return input;
        }

        var sb = new StringBuilder(input.Length);
        char prev = '\0';
        foreach (char c in input)
        {
            bool isSep = c is '-' or '_' or '.';
            if (isSep && (prev is '-' or '_' or '.'))
            {
                continue; // 跳过连续分隔符
            }
            sb.Append(c);
            prev = c;
        }

        // 去除首尾分隔符
        int start = 0;
        int end = sb.Length;
        while (start < end && (sb[start] is '-' or '_' or '.'))
        {
            start++;
        }
        while (end > start && (sb[end - 1] is '-' or '_' or '.'))
        {
            end--;
        }

        return start >= end ? string.Empty : sb.ToString(start, end - start);
    }

    private static void ValidateOptions(EncodingGenerationOptions options)
    {
        if (options.SequenceDigits < 1)
        {
            throw new ArgumentException($"序号位数必须 ≥ 1（当前 {options.SequenceDigits}）", nameof(options));
        }
        if (options.MaxLength < 1)
        {
            throw new ArgumentException($"最大长度必须 ≥ 1（当前 {options.MaxLength}）", nameof(options));
        }
        if (options.MaxRetries < 1)
        {
            throw new ArgumentException($"最大重试次数必须 ≥ 1（当前 {options.MaxRetries}）", nameof(options));
        }
        // 前缀长度 + 序号位数 + 分隔符不能超过最大长度
        int minRequired = options.Prefix.Length + options.Separator.Length + options.SequenceDigits;
        if (options.MaxLength <= minRequired)
        {
            throw new ArgumentException(
                $"最大长度 {options.MaxLength} 不足以容纳前缀({options.Prefix.Length})+分隔符({options.Separator.Length})+序号({options.SequenceDigits})",
                nameof(options));
        }
    }
}
