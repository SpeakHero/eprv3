/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24 23:05:00 +08:00 中国标准时间 星期六
 * 描述：登录安全缓存键统一构建：账号/IP 等外部输入经 SHA256 哈希后入键，禁止原始输入直接拼接缓存键
 */

using System.Security.Cryptography;
using System.Text;
using Qlx.BuildingBlocks.Caching;

namespace Qlx.Modules.Portal.Auth;

/// <summary>
/// 登录安全相关缓存键统一构建（密码失败计数 / 账号锁定 / IP 失败计数 / IP 封禁标记）。
/// 账号与 IP 等外部输入一律经 SHA256 哈希截断后入键：
/// 防止缓存键注入、避免原始账号/IP 在缓存层明文残留（缓存规范 C-1-2 统一键构建）。
/// </summary>
public static class AuthSecurityCacheKeys
{
    /// <summary>门户级缓存模块前缀（物理键：{InstanceName}_portal:auth:{category}:{hash}）。</summary>
    private const string ModulePrefix = "auth";

    /// <summary>密码失败计数键（按账号哈希）。</summary>
    /// <param name="loginAccount">登录账号（原始值，方法内哈希）。</param>
    public static string PasswordFail(string loginAccount) => Build("pwfail", loginAccount);

    /// <summary>账号锁定标记键（按账号哈希）。</summary>
    /// <param name="loginAccount">登录账号（原始值，方法内哈希）。</param>
    public static string Lockout(string loginAccount) => Build("lockout", loginAccount);

    /// <summary>IP 失败计数键（按来源 IP 哈希）。</summary>
    /// <param name="ipAddress">来源 IP（原始值，方法内哈希）。</param>
    public static string IpFail(string ipAddress) => Build("ipfail", ipAddress);

    /// <summary>IP 封禁标记键（按来源 IP 哈希）。</summary>
    /// <param name="ipAddress">来源 IP（原始值，方法内哈希）。</param>
    public static string IpBlock(string ipAddress) => Build("ipblock", ipAddress);

    private static string Build(string category, string rawValue)
        => QlxCacheKey.BuildPortalKey(ModulePrefix, $"{category}:{Hash(rawValue)}");

    /// <summary>SHA256 哈希取前 128 位（小写十六进制；输入先 Trim + 小写归一化，防大小写绕过计数）。</summary>
    private static string Hash(string rawValue)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawValue.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes[..16]).ToLowerInvariant();
    }
}
