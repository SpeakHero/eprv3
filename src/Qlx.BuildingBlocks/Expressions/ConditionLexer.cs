/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 描述：条件表达式词法器（数字/字符串/标识符/变量/符号最长匹配切分，位置感知错误报告）
 */

using Qlx.Abstractions.Expressions;

namespace Qlx.BuildingBlocks.Expressions;

/// <summary>词素种类。</summary>
internal enum ConditionTokenType
{
    /// <summary>数字词素（整数 / 小数 / 科学计数）。</summary>
    Number,

    /// <summary>字符串词素（已去引号并还原转义）。</summary>
    String,

    /// <summary>标识符（变量名 / 关键字 / 词运算符候选）。</summary>
    Identifier,

    /// <summary>参数变量（${name}，Text 为去掉包装后的名称）。</summary>
    Variable,

    /// <summary>符号运算符（已按注册表做最长匹配切分）。</summary>
    Operator,

    /// <summary>左圆括号。</summary>
    LParen,

    /// <summary>右圆括号。</summary>
    RParen,

    /// <summary>逗号。</summary>
    Comma,

    /// <summary>左方括号。</summary>
    LBracket,

    /// <summary>右方括号。</summary>
    RBracket,

    /// <summary>输入结束哨兵。</summary>
    End
}

/// <summary>词素（种类 + 原始文本 + 起始位置）。</summary>
internal readonly record struct ConditionToken(ConditionTokenType Type, string Text, int Position);

/// <summary>
/// 条件表达式词法器。符号类运算符按注册表提供的符号集做最长匹配切分（支持业务自定义符号），
/// 词运算符（AND / OR / 自定义词符号）先切为标识符、由语法层结合注册表判定。
/// </summary>
internal sealed class ConditionLexer
{
    private readonly string _text;
    private readonly IReadOnlyList<string> _symbolicOperatorsByLengthDesc;

    private int _position;

    private ConditionLexer(string text, IReadOnlyCollection<string> symbolicOperators)
    {
        _text = text;
        // 符号类运算符按长度降序预排序，切分时做最长匹配（构造期一次，词法期零排序开销）
        _symbolicOperatorsByLengthDesc = [.. symbolicOperators.OrderByDescending(static s => s.Length)];
    }

    /// <summary>
    /// 对表达式全文执行词法切分。
    /// </summary>
    /// <param name="text">表达式源文本。</param>
    /// <param name="symbolicOperators">符号类运算符全集（非纯字母）。</param>
    /// <returns>词素序列（以 End 哨兵结尾）。</returns>
    /// <exception cref="ConditionExpressionException">出现非法字符、未闭合字符串、非法数字时抛出。</exception>
    internal static IReadOnlyList<ConditionToken> Tokenize(string text, IReadOnlyCollection<string> symbolicOperators)
    {
        var lexer = new ConditionLexer(text, symbolicOperators);
        return lexer.TokenizeAll();
    }

    private List<ConditionToken> TokenizeAll()
    {
        List<ConditionToken> tokens = [];
        while (true)
        {
            SkipWhitespace();
            var start = _position;
            if (_position >= _text.Length)
            {
                tokens.Add(new ConditionToken(ConditionTokenType.End, string.Empty, start));
                return tokens;
            }

            var current = _text[_position];
            switch (current)
            {
                case '(':
                    Advance();
                    tokens.Add(new ConditionToken(ConditionTokenType.LParen, "(", start));
                    break;

                case ')':
                    Advance();
                    tokens.Add(new ConditionToken(ConditionTokenType.RParen, ")", start));
                    break;

                case ',':
                    Advance();
                    tokens.Add(new ConditionToken(ConditionTokenType.Comma, ",", start));
                    break;

                case '[':
                    Advance();
                    tokens.Add(new ConditionToken(ConditionTokenType.LBracket, "[", start));
                    break;

                case ']':
                    Advance();
                    tokens.Add(new ConditionToken(ConditionTokenType.RBracket, "]", start));
                    break;

                case '?':
                case ':':
                    // 三元运算符专用符号：切分为 Operator 词素，由语法层消费（不进注册表，避免污染运算符命名空间）
                    Advance();
                    tokens.Add(new ConditionToken(ConditionTokenType.Operator, current.ToString(), start));
                    break;

                case '\'':
                case '"':
                    tokens.Add(ReadString(start));
                    break;

                case '$':
                    tokens.Add(ReadVariable(start));
                    break;

                default:
                    tokens.Add(ReadWordOrSymbol(start));
                    break;
            }
        }
    }

    private void SkipWhitespace()
    {
        while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
        {
            _position++;
        }
    }

    private ConditionToken ReadString(int start)
    {
        var quote = _text[_position];
        Advance();
        var builder = new System.Text.StringBuilder();
        while (true)
        {
            if (_position >= _text.Length)
            {
                throw ParseError("字符串未闭合。", start);
            }

            var current = _text[_position];
            if (current == quote)
            {
                // 双写引号转义（'' → '），否则视为字符串结束
                if (_position + 1 < _text.Length && _text[_position + 1] == quote && quote == '\'')
                {
                    builder.Append(quote);
                    _position += 2;
                    continue;
                }

                Advance();
                return new ConditionToken(ConditionTokenType.String, builder.ToString(), start);
            }

            if (current == '\\' && _position + 1 < _text.Length)
            {
                builder.Append(ReadEscapeCharacter());
                continue;
            }

            builder.Append(current);
            Advance();
        }
    }

    private char ReadEscapeCharacter()
    {
        var escaped = _text[_position + 1];
        _position += 2;
        return escaped switch
        {
            'n' => '\n',
            't' => '\t',
            'r' => '\r',
            var other => other
        };
    }

    private ConditionToken ReadVariable(int start)
    {
        // ${name}：名称内允许除 '}' 与 '{' 外的任意字符（含中文与点号）
        if (_position + 1 >= _text.Length || _text[_position + 1] != '{')
        {
            throw ParseError("'$' 后必须跟随 '{'（参数变量语法 ${name}）。", start);
        }

        _position += 2;
        var nameStart = _position;
        while (_position < _text.Length && _text[_position] is not ('}' or '{'))
        {
            _position++;
        }

        if (_position >= _text.Length)
        {
            throw ParseError("参数变量未闭合（缺少 '}'）。", start);
        }

        var name = _text[nameStart.._position].Trim();
        if (name.Length == 0)
        {
            throw ParseError("参数变量名不能为空。", start);
        }

        Advance();
        return new ConditionToken(ConditionTokenType.Variable, name, start);
    }

    private ConditionToken ReadWordOrSymbol(int start)
    {
        var current = _text[_position];
        if (char.IsLetter(current) || current == '_')
        {
            return ReadIdentifier(start);
        }

        if (char.IsDigit(current))
        {
            return ReadNumber(start);
        }

        return ReadSymbol(start);
    }

    private ConditionToken ReadIdentifier(int start)
    {
        while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_'))
        {
            _position++;
        }

        return new ConditionToken(ConditionTokenType.Identifier, _text[start.._position], start);
    }

    private ConditionToken ReadNumber(int start)
    {
        while (_position < _text.Length && char.IsDigit(_text[_position]))
        {
            _position++;
        }

        // 小数部分：'.' 后必须跟数字（"1."、"1.2.3" 均为非法数字）
        if (_position < _text.Length && _text[_position] == '.')
        {
            if (_position + 1 >= _text.Length || !char.IsDigit(_text[_position + 1]))
            {
                throw ParseError("非法数字字面量：小数点后必须有数字。", start);
            }

            _position++;
            while (_position < _text.Length && char.IsDigit(_text[_position]))
            {
                _position++;
            }
        }

        // 科学计数部分
        if (_position < _text.Length && (_text[_position] is 'e' or 'E'))
        {
            var exponentStart = _position;
            _position++;
            if (_position < _text.Length && _text[_position] is '+' or '-')
            {
                _position++;
            }

            if (_position >= _text.Length || !char.IsDigit(_text[_position]))
            {
                throw ParseError("非法科学计数法字面量：指数后必须有数字。", exponentStart);
            }

            while (_position < _text.Length && char.IsDigit(_text[_position]))
            {
                _position++;
            }
        }

        // 数字后紧跟字母 / 下划线：保留为单个扩展数字词素（支持业务扩展 0x1F 等自定义数字语法），
        // 交由自定义 / 内置字面量解析器判定；内置解析失败仍报 Parse 错误，123abc 这类歧义输入不会被静默接受
        if (_position < _text.Length && (char.IsLetter(_text[_position]) || _text[_position] == '_'))
        {
            while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_'))
            {
                _position++;
            }
        }

        return new ConditionToken(ConditionTokenType.Number, _text[start.._position], start);
    }

    private ConditionToken ReadSymbol(int start)
    {
        // 符号类运算符最长匹配：按预排序的长度降序逐个尝试前缀命中
        foreach (var symbol in _symbolicOperatorsByLengthDesc)
        {
            if (_position + symbol.Length <= _text.Length
                && string.CompareOrdinal(_text, _position, symbol, 0, symbol.Length) == 0)
            {
                _position += symbol.Length;
                return new ConditionToken(ConditionTokenType.Operator, symbol, start);
            }
        }

        throw ParseError($"无法识别的字符：'{_text[_position]}'。", start);
    }

    private void Advance() => _position++;

    private ConditionExpressionException ParseError(string message, int position)
        => new(ConditionExpressionErrorKind.Parse, message, position, _text);
}
