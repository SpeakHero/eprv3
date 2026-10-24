/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 21:10:00 +08:00 中国标准时间 星期四
 * 描述：当前登录用户信息服务（/api/portal/me 的业务逻辑：Claims 解析、身份/租户上下文分支、DTO 组装）
 */

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Domain.Identity;

namespace Qlx.Modules.Portal.Services;

/// <summary>当前登录用户信息查询契约。</summary>
public interface ICurrentUserService
{
    /// <summary>按当前 Token 主体解析用户信息；主体无效或用户已停用时返回 null（API 层映射为 401）。</summary>
    /// <param name="principal">当前请求的 Claims 主体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<CurrentUserResponse?> GetCurrentUserAsync(
        ClaimsPrincipal principal, CancellationToken cancellationToken);
}

/// <summary>
/// 当前登录用户信息服务。区分身份级 Token（基础身份数据）与租户级 Token（额外返回租户上下文、模块与权限清单），
/// 用户状态实时校验，停用用户即使 Token 未过期也返回 null。
/// </summary>
public sealed class CurrentUserService(PortalDbContext db, IIdObfuscator obfuscator) : ICurrentUserService
{
    /// <inheritdoc />
    public async Task<CurrentUserResponse?> GetCurrentUserAsync(
        ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var subject = principal.FindFirstValue(OpenIddictConstants.Claims.Subject);
        if (subject is null || !obfuscator.TryDecode(subject, out var userId))
        {
            return null;
        }

        var user = await db.PortalUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null || user.Status != UserStatus.Enabled)
        {
            return null;
        }

        var tokenKind = principal.FindFirstValue(QlxAuthConstants.Claims.TokenKind);
        var tenantIdRaw = principal.FindFirstValue(QlxAuthConstants.Claims.TenantId);

        if (tokenKind == QlxAuthConstants.TokenKinds.Tenant
            && tenantIdRaw is not null
            && obfuscator.TryDecode(tenantIdRaw, out _)
            && principal.FindFirstValue(QlxAuthConstants.Claims.MemberId) is { } memberIdRaw
            && obfuscator.TryDecode(memberIdRaw, out var memberId))
        {
            // 租户上下文：返回租户内信息（显示名 / 角色 / 模块 / 权限清单）
            var member = await db.TenantMembers
                .AsNoTracking()
                .SingleOrDefaultAsync(m => m.Id == memberId, cancellationToken)
                .ConfigureAwait(false);

            var modules = principal.Claims
                .Where(c => c.Type == QlxAuthConstants.Claims.Modules)
                .Select(c => c.Value)
                .ToArray();

            return new CurrentUserResponse
            {
                UserId = subject,
                LoginAccount = user.LoginAccount,
                DisplayName = member?.DisplayNameInTenant ?? user.DisplayName,
                TenantId = tenantIdRaw,
                TenantCode = principal.FindFirstValue(QlxAuthConstants.Claims.TenantCode),
                MemberRole = principal.FindFirstValue(QlxAuthConstants.Claims.MemberRole),
                Modules = modules,
                Permissions = [], // 角色权限体系接入后填充
                IsPortalAdmin = user.IsPortalAdmin,
            };
        }

        // 身份级：基础信息 + 可进入租户列表由登录响应提供
        return new CurrentUserResponse
        {
            UserId = subject,
            LoginAccount = user.LoginAccount,
            DisplayName = user.DisplayName,
            Modules = [],
            Permissions = [],
            IsPortalAdmin = user.IsPortalAdmin,
        };
    }
}
