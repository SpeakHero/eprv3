/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 00:20:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 00:20:00 +08:00 中国标准时间 星期三
 * 描述：Hashids 纯算法服务实现（按 hashids 规范，盐洗牌 + 守卫/分隔符）
 */

using System.Text;

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// Hashids 算法核心实现。按 hashids 规范：字母表经盐一致性洗牌，
/// 编码过程引入守卫符与分隔符，同一盐下编码结果稳定且可逆。
/// 非加密手段，仅用于对外隐藏真实主键量级与顺序。
/// </summary>
public sealed class HashidsService : IHashidsService
{
    /// <summary>默认字母表。</summary>
    public const string DefaultAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";

    private const string DefaultSeparators = "cfhistuCFHISTU";
    private const int MinAlphabetLength = 16;
    private const double SeparatorDivisor = 3.5;
    private const double GuardDivisor = 12.0;

    private readonly string _salt;
    private readonly int _minLength;
    private readonly char[] _alphabet;
    private readonly char[] _separators;
    private readonly char[] _guards;

    /// <summary>
    /// 创建 Hashids 服务实例。
    /// </summary>
    /// <param name="salt">混淆盐。</param>
    /// <param name="minLength">混淆字符串最小长度；0 表示不强制。</param>
    /// <param name="alphabet">自定义字母表；<see langword="null"/> 或空白时使用默认字母表（容忍配置占位空串）。</param>
    /// <exception cref="ArgumentException">盐为空或字母表非法（字符重复 / 长度不足 16）时抛出。</exception>
    public HashidsService(string salt, int minLength = 0, string? alphabet = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(salt);

        char[] alphabetChars = (string.IsNullOrWhiteSpace(alphabet) ? DefaultAlphabet : alphabet).ToCharArray();

        if (alphabetChars.Distinct().Count() != alphabetChars.Length)
        {
            throw new ArgumentException("字母表存在重复字符", nameof(alphabet));
        }

        if (alphabetChars.Length < MinAlphabetLength)
        {
            throw new ArgumentException($"字母表至少需要 {MinAlphabetLength} 个字符", nameof(alphabet));
        }

        _salt = salt;
        _minLength = Math.Max(0, minLength);

        // 分隔符：仅保留字母表中存在的字符，并从字母表剔除后按盐洗牌
        char[] seps = [.. DefaultSeparators.Where(alphabetChars.Contains)];
        alphabetChars = [.. alphabetChars.Where(c => !seps.Contains(c))];
        seps = ConsistentShuffle(seps, _salt);

        if (seps.Length == 0 || (alphabetChars.Length / (double)seps.Length) > SeparatorDivisor)
        {
            int sepsNeeded = (int)Math.Ceiling(alphabetChars.Length / SeparatorDivisor);
            if (sepsNeeded > seps.Length)
            {
                int deficit = sepsNeeded - seps.Length;
                seps = [.. seps, .. alphabetChars.Take(deficit)];
                alphabetChars = [.. alphabetChars.Skip(deficit)];
            }
            else
            {
                seps = [.. seps.Take(sepsNeeded)];
            }
        }

        alphabetChars = ConsistentShuffle(alphabetChars, _salt);

        int guardCount = (int)Math.Ceiling(alphabetChars.Length / GuardDivisor);
        if (alphabetChars.Length < 3)
        {
            _guards = [.. seps.Take(guardCount)];
            _separators = [.. seps.Skip(guardCount)];
            _alphabet = alphabetChars;
        }
        else
        {
            _guards = [.. alphabetChars.Take(guardCount)];
            _separators = seps;
            _alphabet = [.. alphabetChars.Skip(guardCount)];
        }
    }

    /// <inheritdoc />
    public string Encode(long number)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(number);

        ulong n = (ulong)number;
        int numbersHashInt = (int)(n % 100UL);

        char lottery = _alphabet[numbersHashInt % _alphabet.Length];
        char[] alphabet = _alphabet;
        var builder = new StringBuilder();
        builder.Append(lottery);

        // 与 TryDecode 对称：散列前按（lottery + 盐 + 当前字母表）重洗字母表
        var shuffleSalt = $"{lottery}{_salt}{new string(alphabet)}";
        alphabet = ConsistentShuffle(alphabet, shuffleSalt[..alphabet.Length]);

        var hashBuilder = new StringBuilder();
        BuildHash(n, alphabet, hashBuilder);
        builder.Append(hashBuilder);

        if (builder.Length < _minLength)
        {
            var guardIndex = (numbersHashInt + builder[0]) % _guards.Length;
            builder.Insert(0, _guards[guardIndex]);

            if (builder.Length < _minLength)
            {
                guardIndex = (numbersHashInt + builder[2]) % _guards.Length;
                builder.Append(_guards[guardIndex]);
            }
        }

        while (builder.Length < _minLength)
        {
            int halfLength = alphabet.Length / 2;
            alphabet = ConsistentShuffle(alphabet, new string(alphabet));

            var padded = new StringBuilder();
            padded.Append(alphabet, halfLength, alphabet.Length - halfLength);
            padded.Append(builder);
            padded.Append(alphabet, 0, halfLength);

            var excess = padded.Length - _minLength;
            builder = excess > 0
                ? new StringBuilder(padded.ToString(excess / 2, _minLength))
                : padded;
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public bool TryDecode(string? obfuscated, out long number)
    {
        number = 0L;
        if (string.IsNullOrEmpty(obfuscated))
        {
            return false;
        }

        List<string> guardBreakdown = SplitByAny(obfuscated, _guards);
        int segmentIndex = guardBreakdown.Count is 2 or 3 ? 1 : 0;
        if (segmentIndex >= guardBreakdown.Count)
        {
            return false;
        }

        string segment = guardBreakdown[segmentIndex];
        if (segment.Length == 0)
        {
            return false;
        }

        char lottery = segment[0];
        List<string> subHashes = SplitByAny(segment[1..], _separators);
        if (subHashes.Count != 1)
        {
            return false;
        }

        char[] alphabet = _alphabet;
        string shuffleSalt = $"{lottery}{_salt}{new string(alphabet)}";
        alphabet = ConsistentShuffle(alphabet, shuffleSalt[..alphabet.Length]);

        if (!TryUnhash(subHashes[0], alphabet, out ulong decoded) || decoded > long.MaxValue)
        {
            return false;
        }

        number = (long)decoded;

        // 回编码校验：防止非法输入被错误接受
        return string.Equals(Encode(number), obfuscated, StringComparison.Ordinal);
    }

    private void BuildHash(ulong number, char[] alphabet, StringBuilder result)
    {
        Span<char> buffer = stackalloc char[64];
        int position = buffer.Length;
        ulong alphabetLength = (ulong)alphabet.Length;

        do
        {
            buffer[--position] = alphabet[number % alphabetLength];
            number /= alphabetLength;
        }
        while (number > 0UL);

        result.Append(buffer[position..]);
    }

    private static bool TryUnhash(string input, char[] alphabet, out ulong number)
    {
        number = 0UL;
        foreach (char c in input)
        {
            int index = Array.IndexOf(alphabet, c);
            if (index < 0)
            {
                return false;
            }

            number = (number * (ulong)alphabet.Length) + (ulong)index;
        }

        return true;
    }

    private static char[] ConsistentShuffle(char[] alphabet, string salt)
    {
        if (salt.Length == 0)
        {
            return alphabet;
        }

        char[] shuffled = (char[])alphabet.Clone();
        int saltIndex = 0;
        int accumulated = 0;

        for (int i = shuffled.Length - 1; i > 0; i--)
        {
            saltIndex %= salt.Length;
            char saltValue = salt[saltIndex];
            accumulated += saltValue;
            int j = (saltValue + saltIndex + accumulated) % i;
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            saltIndex++;
        }

        return shuffled;
    }

    private static List<string> SplitByAny(string input, char[] delimiters)
    {
        List<string> parts = [];
        int start = 0;

        for (int i = 0; i < input.Length; i++)
        {
            if (delimiters.Contains(input[i]))
            {
                parts.Add(input[start..i]);
                start = i + 1;
            }
        }

        parts.Add(input[start..]);
        return parts;
    }
}
