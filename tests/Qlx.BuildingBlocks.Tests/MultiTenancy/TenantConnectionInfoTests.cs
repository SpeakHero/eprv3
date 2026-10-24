/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 23:59:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 23:59:00 +08:00 中国标准时间 星期三
 * 描述：租户连接信息值对象测试：LocalDB/主机端口、空凭据集成认证、共享连接超时与池参数
 */

using Microsoft.Data.SqlClient;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.BuildingBlocks.Tests.MultiTenancy;

/// <summary>
/// <see cref="TenantConnectionInfo"/> 连接串构造测试（B1-02）。
/// </summary>
public sealed class TenantConnectionInfoTests
{
    [Fact]
    public void LocalDbHostBuildsDefaultInstanceWithoutPort()
    {
        var info = new TenantConnectionInfo(
            1, "(localdb)", 1433, "qlx_tenant_shared", string.Empty, string.Empty);

        var builder = info.ToBuilder();

        Assert.Equal(@"(localdb)\MSSQLLocalDB", builder.DataSource);
        Assert.Equal("qlx_tenant_shared", builder.InitialCatalog);
        Assert.True(builder.IntegratedSecurity);
        Assert.Empty(builder.UserID);
    }

    [Fact]
    public void LocalDbHostWithInstanceKeepsInstance()
    {
        var info = new TenantConnectionInfo(
            1, @"(localdb)\MSSQLLocalDB", 1433, "qlx_tenant_shared", string.Empty, string.Empty);

        Assert.Equal(@"(localdb)\MSSQLLocalDB", info.ToBuilder().DataSource);
    }

    [Fact]
    public void TcpHostAppendsPort()
    {
        var info = new TenantConnectionInfo(
            1, "10.0.0.8", 14330, "qlx_tenant_a", "db_user", "secret");

        var builder = info.ToBuilder();

        Assert.Equal("10.0.0.8,14330", builder.DataSource);
        Assert.Equal("db_user", builder.UserID);
        Assert.Equal("secret", builder.Password);
        Assert.False(builder.IntegratedSecurity);
    }

    [Fact]
    public void EmptyCredentialsFallBackToIntegratedSecurity()
    {
        var info = new TenantConnectionInfo(
            1, "sql-host", 1433, "qlx_tenant_a", "   ", "ignored");

        var builder = info.ToBuilder();

        Assert.True(builder.IntegratedSecurity);
        Assert.Empty(builder.UserID);
        Assert.Empty(builder.Password);
    }

    [Fact]
    public void InitialCatalogOverrideSupportsMasterConnection()
    {
        var info = new TenantConnectionInfo(
            1, "(localdb)", 1433, "qlx_tenant_a", string.Empty, string.Empty);

        Assert.Equal("master", info.ToBuilder("master").InitialCatalog);
    }

    [Fact]
    public void SharedOptionsCarryTimeoutPoolAndSharedFlag()
    {
        var options = new SharedDatabaseOptions
        {
            DbHost = "(localdb)",
            DbName = "qlx_tenant_shared",
            ConnectTimeoutSeconds = 12,
            MinPoolSize = 5,
            MaxPoolSize = 240,
        };

        var info = options.ToConnectionInfo(42);
        var builder = info.ToBuilder();

        Assert.True(info.IsShared);
        Assert.Equal(42, info.TenantId);
        Assert.Equal(12, builder.ConnectTimeout);
        Assert.Equal(5, builder.MinPoolSize);
        Assert.Equal(240, builder.MaxPoolSize);
        Assert.True(builder.MultipleActiveResultSets);
        Assert.Equal("qlx_tenant_shared", builder.InitialCatalog);
    }

    [Fact]
    public void IsolatedConnectionIsNotMarkedShared()
    {
        var info = new TenantConnectionInfo(
            2, "sql-host", 1433, "qlx_tenant_b", "db_user", "secret");

        Assert.False(info.IsShared);
    }
}
