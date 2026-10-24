/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 23:40:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 23:40:00 +08:00 中国标准时间 星期三
 * 描述：租户数据库模式领域行为测试（默认 Shared、仅允许单向升级、共享行凭据为空）
 */

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.BuildingBlocks.Tests.Portal;

/// <summary>
/// 租户数据库模式领域测试：<see cref="Tenant.ChangeDatabaseMode"/> 的状态机约束与
/// <see cref="TenantDatabase"/> 共享/独立两种构造形态。
/// </summary>
public sealed class TenantDatabaseModeTests
{
    private static Tenant CreateTenant(string code = "mode-ten") => new(code, "模式演示租户");

    [Fact]
    public void NewTenantDefaultsToSharedMode()
    {
        var tenant = CreateTenant();

        Assert.Equal(TenantDatabaseMode.Shared, tenant.DatabaseMode);
    }

    [Fact]
    public void ChangeDatabaseModeUpgradesSharedToIsolated()
    {
        var tenant = CreateTenant();

        tenant.ChangeDatabaseMode(TenantDatabaseMode.Isolated);

        Assert.Equal(TenantDatabaseMode.Isolated, tenant.DatabaseMode);
    }

    [Fact]
    public void ChangeDatabaseModeRejectsSameMode()
    {
        var tenant = CreateTenant();

        var ex = Assert.Throws<ConflictException>(
            () => tenant.ChangeDatabaseMode(TenantDatabaseMode.Shared));

        Assert.Equal("TENANT_DATABASE_MODE_UNCHANGED", ex.ErrorCode);
        Assert.Equal(TenantDatabaseMode.Shared, tenant.DatabaseMode);
    }

    [Fact]
    public void ChangeDatabaseModeRejectsDowngrade()
    {
        var tenant = CreateTenant();
        tenant.ChangeDatabaseMode(TenantDatabaseMode.Isolated);

        var ex = Assert.Throws<ConflictException>(
            () => tenant.ChangeDatabaseMode(TenantDatabaseMode.Shared));

        Assert.Equal("TENANT_DATABASE_MODE_INVALID", ex.ErrorCode);
        Assert.Equal(TenantDatabaseMode.Isolated, tenant.DatabaseMode);
    }

    [Fact]
    public void SharedDatabaseRowKeepsCredentialsEmptyAndDefaultPool()
    {
        var tenant = CreateTenant();
        var database = new TenantDatabase(
            tenant, TenantDatabaseMode.Shared, "(localdb)", 1433, "qlx_tenant_shared");

        Assert.Equal(TenantDatabaseMode.Shared, database.DatabaseMode);
        Assert.Equal("default", database.PoolCode);
        Assert.Equal("qlx_tenant_shared", database.DbName);
        Assert.Empty(database.EncryptedDbUser);
        Assert.Empty(database.EncryptedDbPassword);
        Assert.Null(database.EncryptionSalt);
    }

    [Fact]
    public void IsolatedConstructorKeepsLegacyIsolatedSemantics()
    {
        var tenant = CreateTenant();
        var database = new TenantDatabase(tenant, "(localdb)", 1433, "qlx_tenant_mode_ten");

        Assert.Equal(TenantDatabaseMode.Isolated, database.DatabaseMode);
        Assert.Equal("default", database.PoolCode);
    }

    [Fact]
    public void ExplicitPoolCodeOverridesDefault()
    {
        var tenant = CreateTenant();
        var database = new TenantDatabase(
            tenant, TenantDatabaseMode.Shared, "(localdb)", 1433, "qlx_tenant_shared", "pool-b");

        Assert.Equal("pool-b", database.PoolCode);
    }
}
