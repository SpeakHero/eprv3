/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:05:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 16:05:00 +08:00 中国标准时间 星期三
 * 描述：SMTP 配置选项（record + init 属性）
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.Messaging;

/// <summary>
/// SMTP 邮件发送配置选项。所有属性 init 不可变。
/// 配置节名：<c>Qlx:Messaging:Smtp</c>。
/// </summary>
#pragma warning disable CA1805
public sealed record SmtpOptions
{
    /// <summary>是否启用 SMTP 发送，默认 false。</summary>
    public bool Enabled { get; init; }

    /// <summary>SMTP 主机（启用时必填）。</summary>
    public string? Host { get; init; }

    /// <summary>SMTP 端口，默认 587（STARTTLS）。</summary>
    public int Port { get; init; } = 587;

    /// <summary>是否使用 SSL/TLS 直连（默认 false，使用 STARTTLS）。</summary>
    public bool UseSsl { get; init; }

    /// <summary>发件邮箱（启用时必填）。</summary>
    public string? FromEmail { get; init; }

    /// <summary>发件人显示名。</summary>
    public string? FromName { get; init; }

    /// <summary>认证用户名（启用且服务器要求认证时必填）。</summary>
    public string? Username { get; init; }

    /// <summary>认证密码（敏感，日志中需掩码）。</summary>
    public string? Password { get; init; }

    /// <summary>连接 / 发送超时（秒），默认 15。</summary>
    public int TimeoutSeconds { get; init; } = 15;

    /// <summary>
    /// 快速校验：启用但必填项缺失时返回错误列表。
    /// </summary>
    /// <returns>错误消息列表（空列表表示校验通过）。</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!Enabled) return errors;

        if (string.IsNullOrWhiteSpace(Host)) errors.Add("Smtp:Host 不能为空。");
        if (string.IsNullOrWhiteSpace(FromEmail)) errors.Add("Smtp:FromEmail 不能为空。");
        if (string.IsNullOrWhiteSpace(Username)) errors.Add("Smtp:Username 不能为空。");
        if (string.IsNullOrWhiteSpace(Password)) errors.Add("Smtp:Password 不能为空。");

        return errors;
    }
}
