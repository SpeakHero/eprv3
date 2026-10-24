/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 09:25:00 +08:00 中国标准时间 星期五
 * 描述：消息模板渲染器测试——${var} 插值、未知变量 / 必填缺失 / 类型不符三段校验次序、
 *       可选变量缺失保留占位符、HTML 转义开关、空白必填值判缺失、var_schema 非法 JSON 拒绝
 */

using Qlx.Abstractions.Exceptions;
using Qlx.Modules.Support.Services.Messages;

namespace Qlx.BuildingBlocks.Tests.Messages;

/// <summary>
/// <see cref="TemplateRenderer"/> 单元测试（静态纯函数，无依赖）。
/// </summary>
public sealed class TemplateRendererTests
{
    private const string Schema = """[{"name":"title","type":"string","required":true},{"name":"percent","type":"number","required":false}]""";

    private static Dictionary<string, object?> Vars(params (string Key, object? Value)[] items)
        => items.ToDictionary(static i => i.Key, static i => i.Value);

    // ValidationException.Message 固定为通用文案，明细在 FieldErrors（FieldName+ErrorMessage）
    private static string ErrorsText(ValidationException exception)
        => string.Join("|", exception.FieldErrors.Select(static e => $"{e.FieldName}:{e.ErrorMessage}"));

    [Fact]
    public void RenderSubstitutesVariablesInSubjectAndContent()
    {
        var result = TemplateRenderer.Render(
            "任务进度更新：${title}", "任务 ${title} 进度已更新至 ${percent}%。", Schema,
            Vars(("title", "月度结账"), ("percent", 80)), escapeHtml: false);

        Assert.Equal("任务进度更新：月度结账", result.Subject);
        Assert.Equal("任务 月度结账 进度已更新至 80%。", result.Content);
    }

    [Fact]
    public void UnknownPlaceholderIsRejected()
    {
        // 模板包含未在 var_schema 注册的 ${extra}：防模板越权注入未审变量
        var exception = Assert.Throws<ValidationException>(() => TemplateRenderer.Render(
            "${title}", "${extra}", "[{\"name\":\"title\",\"type\":\"string\",\"required\":true}]",
            Vars(("title", "t"), ("extra", "x"))));

        Assert.Contains("未注册变量", ErrorsText(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRequiredVariableIsRejected()
    {
        var exception = Assert.Throws<ValidationException>(() => TemplateRenderer.Render(
            "${title}", "正文", Schema, Vars(("percent", 10))));

        Assert.Contains("必填变量缺失", ErrorsText(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void NumberTypeMismatchIsRejected()
    {
        var exception = Assert.Throws<ValidationException>(() => TemplateRenderer.Render(
            "${title}", "${percent}", Schema, Vars(("title", "t"), ("percent", "abc"))));

        Assert.Contains("percent", ErrorsText(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void BooleanTypeMismatchIsRejected()
    {
        var exception = Assert.Throws<ValidationException>(() => TemplateRenderer.Render(
            "${title}", "${flag}", "[{\"name\":\"title\",\"type\":\"string\",\"required\":false},{\"name\":\"flag\",\"type\":\"boolean\",\"required\":true}]",
            Vars(("title", "t"), ("flag", "not-a-bool"))));

        Assert.Contains("flag", ErrorsText(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void OptionalMissingVariableKeepsPlaceholderVisible()
    {
        // 未提供且非必填：保留原占位符（显式可见，便于排查）
        var result = TemplateRenderer.Render(
            "${title}", "进度 ${percent}%", Schema, Vars(("title", "t")));

        Assert.Equal("进度 ${percent}%", result.Content);
    }

    [Fact]
    public void HtmlEscapeAppliedWhenEnabled()
    {
        var result = TemplateRenderer.Render(
            "${title}", "${content}",
            "[{\"name\":\"title\",\"type\":\"string\",\"required\":false},{\"name\":\"content\",\"type\":\"string\",\"required\":false}]",
            Vars(("title", "<b>标题</b>"), ("content", "a & b")));

        Assert.Equal("&lt;b&gt;标题&lt;/b&gt;", result.Subject);
        Assert.Equal("a &amp; b", result.Content);
    }

    [Fact]
    public void HtmlEscapeSkippedWhenDisabled()
    {
        var result = TemplateRenderer.Render(
            "${title}", "${content}",
            "[{\"name\":\"title\",\"type\":\"string\",\"required\":false},{\"name\":\"content\",\"type\":\"string\",\"required\":false}]",
            Vars(("title", "<b>标题</b>"), ("content", "a & b")), escapeHtml: false);

        Assert.Equal("<b>标题</b>", result.Subject);
        Assert.Equal("a & b", result.Content);
    }

    [Fact]
    public void BlankRequiredValueIsTreatedAsMissing()
    {
        var exception = Assert.Throws<ValidationException>(() => TemplateRenderer.Render(
            "${title}", "正文", Schema, Vars(("title", "   "))));

        Assert.Contains("必填变量缺失", ErrorsText(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidSchemaJsonIsRejected()
    {
        var exception = Assert.Throws<ValidationException>(() => TemplateRenderer.Render(
            "${title}", "正文", "not-a-json", Vars(("title", "t"))));

        Assert.Contains("var_schema 解析失败", ErrorsText(exception), StringComparison.Ordinal);
    }
}
