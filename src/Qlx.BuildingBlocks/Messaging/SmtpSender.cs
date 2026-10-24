/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:05:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 16:05:00 +08:00 中国标准时间 星期三
 * 描述：MailKit 实现的 SMTP 邮件发送器（失败分类返回，不抛异常）
 */

using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

#pragma warning disable CA1848 // 使用 LoggerMessage 委托提高性能（阶段二暂用内联日志，后续集中迁移）

namespace Qlx.BuildingBlocks.Messaging;

/// <summary>
/// MailKit 实现的 SMTP 邮件发送器。读取 <see cref="SmtpOptions"/> 配置，
/// 对 MailKit 异常分类映射为 <see cref="SmtpSendStatus"/>，保证上层调用方可分类处理。
/// </summary>
public sealed class SmtpSender : ISmtpSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpSender> _logger;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="options">SMTP 配置（IOptions 注入）。</param>
    /// <param name="logger">日志器。</param>
    public SmtpSender(IOptions<SmtpOptions> options, ILogger<SmtpSender> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SmtpSendResult> SendAsync(SmtpMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_options.Enabled)
        {
            _logger.LogWarning("SMTP 未启用，邮件发送跳过。To={To}", MaskEmail(message.ToEmail));
            return new SmtpSendResult(SmtpSendStatus.ConfigurationMissing, "SMTP 未启用。");
        }

        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            _logger.LogError("SMTP Host 未配置。");
            return new SmtpSendResult(SmtpSendStatus.ConfigurationMissing, "SMTP Host 未配置。");
        }

        var mail = BuildMimeMessage(message);
        using var client = new SmtpClient();

        try
        {
            client.Timeout = _options.TimeoutSeconds * 1000;

            var secureOptions = _options.UseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            await client.ConnectAsync(_options.Host, _options.Port, secureOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                await client.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, cancellationToken);
            }

            var sender = await client.SendAsync(mail, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("SMTP 发送成功。MessageId={MessageId}, To={To}",
                mail.MessageId, MaskEmail(message.ToEmail));

            return new SmtpSendResult(SmtpSendStatus.Sent, null);
        }
        catch (MailKit.Security.AuthenticationException ex)
        {
            _logger.LogError(ex, "SMTP 认证失败。Username={Username}, To={To}",
                _options.Username, MaskEmail(message.ToEmail));
            return new SmtpSendResult(SmtpSendStatus.AuthenticationFailed, ex.Message);
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "SMTP 网络错误。Host={Host}, Port={Port}", _options.Host, _options.Port);
            return new SmtpSendResult(SmtpSendStatus.NetworkError, ex.Message);
        }
        catch (IOException ex) when (ex.InnerException is SocketException socketEx)
        {
            _logger.LogError(socketEx, "SMTP 网络 IO 错误。Host={Host}, Port={Port}", _options.Host, _options.Port);
            return new SmtpSendResult(SmtpSendStatus.NetworkError, socketEx.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP 发送异常。To={To}", MaskEmail(message.ToEmail));
            return new SmtpSendResult(SmtpSendStatus.ProtocolError, ex.Message);
        }
    }

    private MimeMessage BuildMimeMessage(SmtpMessage message)
    {
        var fromEmail = message.FromEmail ?? _options.FromEmail ?? throw new InvalidOperationException("发件邮箱未配置。");
        var fromName = message.FromName ?? _options.FromName ?? fromEmail;

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(fromName, fromEmail));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToEmail));
        mime.Subject = message.Subject;

        var body = new BodyBuilder();
        body.HtmlBody = message.HtmlBody;
        if (!string.IsNullOrWhiteSpace(message.TextBody))
            body.TextBody = message.TextBody;
        else
            body.TextBody = StripHtml(message.HtmlBody);

        mime.Body = body.ToMessageBody();
        return mime;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var span = html.AsSpan();
        var sb = new System.Text.StringBuilder(span.Length);
        bool inTag = false;
        foreach (var c in span)
        {
            if (c == '<') inTag = true;
            else if (c == '>') inTag = false;
            else if (!inTag) sb.Append(c);
        }
        return sb.ToString();
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "***";
        var idx = email.IndexOf('@', StringComparison.Ordinal);
        if (idx <= 0) return "***";
        var prefix = email[..Math.Min(3, idx)];
        return prefix + "***" + email[idx..];
    }
}
