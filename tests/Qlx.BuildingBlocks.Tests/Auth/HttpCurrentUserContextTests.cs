/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 08:00:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 08:00:00 中国标准时间 星期三
 * 描述：HttpCurrentUserContext 单元测试：claims 读取与匿名降级
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Qlx.BuildingBlocks.Auth;

namespace Qlx.BuildingBlocks.Tests.Auth;

/// <summary>
/// HTTP 当前用户上下文验证：认证请求读取 login_account / name / sub，
/// 非请求上下文（后台任务）与匿名请求安全降级。
/// </summary>
public sealed class HttpCurrentUserContextTests
{
    [Fact]
    public void ReadsClaimsFromHttpContext()
    {
        var identity = new ClaimsIdentity(authenticationType: "Bearer");
        identity.AddClaim(new Claim("sub", "abc123"));
        identity.AddClaim(new Claim("name", "张三"));
        identity.AddClaim(new Claim(HttpCurrentUserContext.LoginAccountClaim, "zhangsan"));
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };

        var context = new HttpCurrentUserContext(accessor);

        Assert.True(context.IsAuthenticated);
        Assert.Equal("abc123", context.UserId);
        Assert.Equal("zhangsan", context.UserName);
        Assert.Equal("张三", context.UserDisplayName);
    }

    [Fact]
    public void FallsBackToNameIdentifierClaimForUserId()
    {
        var identity = new ClaimsIdentity(authenticationType: "Bearer");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "uid-77"));
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };

        var context = new HttpCurrentUserContext(accessor);

        Assert.Equal("uid-77", context.UserId);
    }

    [Fact]
    public void DegradesToAnonymousWithoutHttpContext()
    {
        // 模拟后台任务 / 启动期：IHttpContextAccessor 无当前请求
        var context = new HttpCurrentUserContext(new HttpContextAccessor());

        Assert.False(context.IsAuthenticated);
        Assert.Null(context.UserId);
        Assert.Null(context.UserName);
        Assert.Null(context.UserDisplayName);
    }
}
