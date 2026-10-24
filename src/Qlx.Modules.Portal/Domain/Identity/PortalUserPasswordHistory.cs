/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：门户用户密码历史（用于密码修改时禁止与最近 N 次相同）
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Portal.Domain.Identity;

/// <summary>
/// 门户用户密码历史记录。<see cref="PortalUser"/> 每次密码修改/重置时新增一条，
/// 用于下次修改时校验"不能与最近 N 次相同"。哈希由 <see cref="Microsoft.AspNetCore.Identity.IPasswordHasher{TUser}"/> 生成（含 PBKDF2 salt，
/// 所以同一明文每次哈希不同，校验时必须用 VerifyHashedPassword 逐个匹配）。
/// </summary>
[Table("por_user_password_histories")]
[Index(nameof(PortalUserId))]
[Index(nameof(PortalUserId), nameof(CreatedAt))]
public sealed class PortalUserPasswordHistory
{
    /// <summary>主键（雪花 ID）。</summary>
    public long Id { get; private set; }

    /// <summary>门户用户 Id。</summary>
    public long PortalUserId { get; private set; }

    /// <summary>密码哈希（PBKDF2，PasswordHasher 生成）。</summary>
    [MaxLength(512)]
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>记录创建时间（密码修改/重置发生时刻）。</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>EF Core 无参构造。</summary>
    public PortalUserPasswordHistory() { }

    /// <summary>构造密码历史记录。</summary>
    /// <param name="id">雪花 Id。</param>
    /// <param name="portalUserId">门户用户 Id。</param>
    /// <param name="passwordHash">PBKDF2 哈希。</param>
    public PortalUserPasswordHistory(long id, long portalUserId, string passwordHash)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(passwordHash);
        Id = id;
        PortalUserId = portalUserId;
        PasswordHash = passwordHash;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
