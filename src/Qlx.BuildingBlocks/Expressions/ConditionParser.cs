/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 描述：条件表达式语法分析器（递归下降 + 优先级爬升，运算符绑定经注册表，含深度防护与变量收集）
 */

using System.Globalization;

using Qlx.Abstractions.Expressions;

namespace Qlx.BuildingBlocks.Expressions;

/// <summary>
/// 条件表达式语法分析器。文法（自底向上优先级递增）：
/// <code>
/// 三元   := 二元 [ '?' 三元 ':' 三元 ]
/// 二元   := 一元 { 运算符(注册表) 一元 }        ← 优先级爬升
/// 一元   := 前缀运算符 一元 | 原子
/// 原子   := 字面量 | 变量 | 函数调用 | 列表 | '(' 三元 ')'
/// </code>
/// 运算符 / 函数 / 字面量解析器全部经注册表解析，引擎核心不含任何业务运算符硬编码。
/// </summary>
internal sealed class ConditionParser
{
    private readonly IReadOnlyList<ConditionToken> _tokens;
    private readonly string _text;
    private readonly ConditionRegistry _registry;
    private readonly int _maxDepth;
    private readonly HashSet<string> _variables = new(StringComparer.OrdinalIgnoreCase);

    private int _tokenIndex;
    private int _depth;

    private ConditionParser(string text, ConditionRegistry registry, int maxDepth)
    {
        _text = text;
        _registry = registry;
        _maxDepth = maxDepth;
        _tokens = ConditionLexer.Tokenize(text, registry.SymbolicOperatorTexts);
    }

    /// <summary>
    /// 解析表达式全文为 AST。
    /// </summary>
    /// <param name="text">表达式源文本。</param>
    /// <param name="registry">条件注册表快照。</param>
    /// <param name="maxDepth">最大嵌套深度限制。</param>
    /// <returns>AST 根节点与表达式引用的全部变量名（去重、不区分大小写）。</returns>
    /// <exception cref="ConditionExpressionException">语法错误或超出深度限制时抛出。</exception>
    internal static ConditionParseResult Parse(string text, ConditionRegistry registry, int maxDepth)
    {
        var parser = new ConditionParser(text, registry, maxDepth);
        var root = parser.ParseTernaryExpression();
        parser.Expect(ConditionTokenType.End, "表达式末尾存在多余内容。");
        return new ConditionParseResult(root, [.. parser._variables]);
    }

    internal readonly record struct ConditionParseResult(ConditionNode Root, IReadOnlyList<string> Variables);

    private ConditionNode ParseTernaryExpression()
    {
        EnterNested();
        try
        {
            var condition = ParseBinaryExpression((int)ConditionPrecedence.LogicalOr);
            if (Match(ConditionTokenType.Operator, "?"))
            {
                var whenTrue = ParseTernaryExpression();
                Expect(ConditionTokenType.Operator, ":", "三元表达式缺少 ':'。");
                var whenFalse = ParseTernaryExpression();
                return new TernaryNode(condition, whenTrue, whenFalse);
            }

            return condition;
        }
        finally
        {
            _depth--;
        }
    }

    private ConditionNode ParseBinaryExpression(int minPrecedence)
    {
        var left = ParseUnaryExpression();
        while (true)
        {
            var binaryOperator = TryPeekBinaryOperator();
            if (binaryOperator is null || (int)binaryOperator.Precedence < minPrecedence)
            {
                return left;
            }

            _tokenIndex++;
            // 左结合：右操作数要求优先级严格更高；右结合：允许同级继续
            var nextMinPrecedence = binaryOperator.IsRightAssociative
                ? (int)binaryOperator.Precedence
                : (int)binaryOperator.Precedence + 1;
            var right = ParseBinaryExpression(nextMinPrecedence);
            left = new BinaryNode(left, binaryOperator.Symbol, right) { ResolvedOperator = binaryOperator };
        }
    }

    private ConditionNode ParseUnaryExpression()
    {
        var unaryOperator = TryPeekUnaryOperator();
        if (unaryOperator is not null)
        {
            _tokenIndex++;
            EnterNested();
            try
            {
                var operand = ParseUnaryExpression();
                return new UnaryNode(unaryOperator.Symbol, operand) { ResolvedOperator = unaryOperator };
            }
            finally
            {
                _depth--;
            }
        }

        return ParsePrimaryExpression();
    }

    private ConditionNode ParsePrimaryExpression()
    {
        var token = Current;
        switch (token.Type)
        {
            case ConditionTokenType.Number:
            case ConditionTokenType.String:
                _tokenIndex++;
                return ParseLiteral(token);

            case ConditionTokenType.Variable:
                _tokenIndex++;
                TrackVariable(token.Text);
                return new VariableNode(token.Text);

            case ConditionTokenType.Identifier:
                return ParseIdentifier(token);

            case ConditionTokenType.LParen:
                _tokenIndex++;
                EnterNested();
                try
                {
                    var inner = ParseTernaryExpression();
                    Expect(ConditionTokenType.RParen, "括号不配对：缺少 ')'。");
                    return inner;
                }
                finally
                {
                    _depth--;
                }

            case ConditionTokenType.LBracket:
                return ParseList();

            case ConditionTokenType.End:
                throw ParseError("表达式意外结束。", token.Position);

            default:
                throw ParseError($"意外的词素：'{token.Text}'。", token.Position);
        }
    }

    private ConditionNode ParseIdentifier(ConditionToken token)
    {
        // 函数调用：标识符 + '('（函数名不区分大小写，经注册表解析）；未注册的函数调用在解析期即报错
        if (Peek(1).Type == ConditionTokenType.LParen)
        {
            if (_registry.TryGetFunction(token.Text) is not { } function)
            {
                throw ParseError($"函数未注册：{token.Text}。", token.Position);
            }

            _tokenIndex += 2;
            return ParseFunctionArguments(function.Name);
        }

        // 布尔 / 空值关键字（不区分大小写）
        var upper = token.Text.ToUpperInvariant();
        switch (upper)
        {
            case "TRUE":
                _tokenIndex++;
                return new LiteralNode(true);
            case "FALSE":
                _tokenIndex++;
                return new LiteralNode(false);
            case "NULL":
                _tokenIndex++;
                return new LiteralNode(null);
        }

        // 其余标识符一律视为变量引用（词运算符不会到达此处：已在二元 / 一元层消费）
        _tokenIndex++;
        TrackVariable(token.Text);
        return new VariableNode(token.Text);
    }

    private FunctionNode ParseFunctionArguments(string functionName)
    {
        EnterNested();
        try
        {
            List<ConditionNode> arguments = [];
            if (!Check(ConditionTokenType.RParen))
            {
                arguments.Add(ParseTernaryExpression());
                while (Match(ConditionTokenType.Comma, ","))
                {
                    arguments.Add(ParseTernaryExpression());
                }
            }

            Expect(ConditionTokenType.RParen, $"函数 {functionName} 参数列表缺少 ')'。");
            return new FunctionNode(functionName, arguments);
        }
        finally
        {
            _depth--;
        }
    }

    private ListNode ParseList()
    {
        var startToken = Current;
        _tokenIndex++;
        EnterNested();
        try
        {
            List<ConditionNode> items = [];
            if (!Check(ConditionTokenType.RBracket))
            {
                items.Add(ParseTernaryExpression());
                while (Match(ConditionTokenType.Comma, ","))
                {
                    items.Add(ParseTernaryExpression());
                }
            }

            Expect(ConditionTokenType.RBracket, "列表字面量缺少 ']'。");
            return new ListNode(items);
        }
        finally
        {
            _depth--;
        }
    }

    private LiteralNode ParseLiteral(ConditionToken token)
    {
        // 自定义字面量解析器先执行（注册顺序），未命中再走内置解析
        var tokenKind = token.Type == ConditionTokenType.Number
            ? ConditionLiteralTokenKind.Number
            : ConditionLiteralTokenKind.Text;
        foreach (var parser in _registry.LiteralParsers)
        {
            if (parser.TryParse(tokenKind, token.Text, out var customValue))
            {
                return new LiteralNode(customValue);
            }
        }

        if (token.Type == ConditionTokenType.String)
        {
            return new LiteralNode(token.Text);
        }

        // 内置数字解析：整数 → long，小数 → decimal，科学计数 / 超范围 → double
        if (long.TryParse(token.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return new LiteralNode(intValue);
        }

        if (decimal.TryParse(token.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
        {
            return new LiteralNode(decimalValue);
        }

        if (double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return new LiteralNode(doubleValue);
        }

        throw ParseError($"非法数字字面量：'{token.Text}'。", token.Position);
    }

    private IConditionBinaryOperator? TryPeekBinaryOperator()
    {
        var token = Current;
        switch (token.Type)
        {
            case ConditionTokenType.Operator:
                return _registry.TryGetBinaryOperator(token.Text);
            case ConditionTokenType.Identifier when token.Text.Equals("NOT", StringComparison.OrdinalIgnoreCase)
                && Peek(1).Type == ConditionTokenType.Identifier
                && Peek(1).Text.Equals("IN", StringComparison.OrdinalIgnoreCase):
                // 复合词运算符 NOT IN：一次消费两个词素
                _tokenIndex++;
                return _registry.TryGetBinaryOperator("NOT IN");
            case ConditionTokenType.Identifier:
                return _registry.TryGetBinaryOperator(token.Text);
            default:
                return null;
        }
    }

    private IConditionUnaryOperator? TryPeekUnaryOperator()
    {
        var token = Current;
        return token.Type switch
        {
            ConditionTokenType.Operator => _registry.TryGetUnaryOperator(token.Text),
            ConditionTokenType.Identifier => _registry.TryGetUnaryOperator(token.Text),
            _ => null
        };
    }

    private void TrackVariable(string name)
    {
        if (!name.Equals("true", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("false", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            _variables.Add(name);
        }
    }

    private void EnterNested()
    {
        _depth++;
        if (_depth > _maxDepth)
        {
            throw ParseError($"表达式嵌套深度超出限制（{_maxDepth}层）。", Current.Position);
        }
    }

    private ConditionToken Current => _tokens[_tokenIndex];

    private ConditionToken Peek(int offset)
    {
        var index = Math.Min(_tokenIndex + offset, _tokens.Count - 1);
        return _tokens[index];
    }

    private bool Check(ConditionTokenType type) => Current.Type == type;

    private bool Match(ConditionTokenType type, string text)
    {
        if (Check(type) && string.Equals(Current.Text, text, StringComparison.Ordinal))
        {
            _tokenIndex++;
            return true;
        }

        return false;
    }

    private void Expect(ConditionTokenType type, string message)
    {
        if (!Check(type))
        {
            throw ParseError(message, Current.Position);
        }

        _tokenIndex++;
    }

    private void Expect(ConditionTokenType type, string text, string message)
    {
        if (!Check(type) || !string.Equals(Current.Text, text, StringComparison.Ordinal))
        {
            throw ParseError(message, Current.Position);
        }

        _tokenIndex++;
    }

    private ConditionExpressionException ParseError(string message, int position)
        => new(ConditionExpressionErrorKind.Parse, message, position, _text);
}
