/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：外部身份提供方（R-9）测试：配置映射校验、注册表快照、空表启动、密钥解密与排序
 */

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Qlx.Abstractions.Constants;
using Qlx.Modules.Portal;
using Qlx.Modules.Portal.Domain.Tenancy;
using Qlx.Modules.Portal.Services;

namespace Qlx.BuildingBlocks.Tests.Portal;

/// <summary>
/// 外部身份提供方骨架测试：配置不合法必须被跳过（不影响启动），空表视为「未启用外部登录」。
/// </summary>
public sealed class ExternalIdentityProviderTests
{
    [Fact]
    public void DescriptorMapsScopesAndAuthority()
    {
        var entity = CreateEntity("wecom-main", "企业微信", ExternalIdentityProviderType.WeCom, "https://qy.example.com",
            "corp-1", scopes: "openid, profile;email openid");

        var descriptor = ExternalIdentityProviderDescriptor.TryCreate(entity, "s3cr3t", out var error);

        Assert.Null(error);
        Assert.NotNull(descriptor);
        Assert.Equal("wecom-main", descriptor.ProviderCode);
        Assert.Equal("https://qy.example.com/", descriptor.Authority.ToString());
        Assert.Equal("s3cr3t", descriptor.ClientSecret);
        Assert.Equal(["openid", "profile", "email"], descriptor.Scopes);
    }

    [Fact]
    public void DescriptorRejectsRelativeAuthority()
    {
        var entity = CreateEntity("bad", "非法配置", ExternalIdentityProviderType.Oidc, "login.example.com", "c", null);

        var descriptor = ExternalIdentityProviderDescriptor.TryCreate(entity, string.Empty, out var error);

        Assert.Null(descriptor);
        Assert.Contains("Authority", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryReplacesSnapshotAndFindsByCode()
    {
        var registry = new ExternalIdentityProviderRegistry();
        Assert.Empty(registry.Providers);
        Assert.Null(registry.Find("missing"));

        var entity = CreateEntity("ok", "标准 OIDC", ExternalIdentityProviderType.Oidc, "https://idp.example.com", "c", null);
        var descriptor = ExternalIdentityProviderDescriptor.TryCreate(entity, string.Empty, out _)!;

        Assert.Equal(1, registry.Replace([descriptor]));
        Assert.Same(descriptor, registry.Find("ok"));
    }

    [Fact]
    public async Task EmptyTableYieldsNoProvidersAndDoesNotThrow()
    {
        await using var scope = await SourceScope.CreateAsync();
        var providers = await scope.Source.GetEnabledAsync(TestContext.Current.CancellationToken);

        Assert.Empty(providers);
    }

    [Fact]
    public async Task SourceSkipsUndecryptableRowsAndKeepsValidOnes()
    {
        await using var scope = await SourceScope.CreateAsync();
        await scope.SeedAsync([
            WithCipher(CreateEntity(
                "broken", "密钥损坏", ExternalIdentityProviderType.DingTalk, "https://ding.example.com", "c", null),
                7001, "not-a-valid-cipher"),
            WithCipher(CreateEntity(
                "good", "标准 OIDC", ExternalIdentityProviderType.Oidc, "https://idp.example.com", "c", null),
                7002, scope.Protector.Protect("real-secret")),
        ]);

        var providers = await scope.Source.GetEnabledAsync(TestContext.Current.CancellationToken);

        var provider = Assert.Single(providers);
        Assert.Equal("good", provider.ProviderCode);
        Assert.Equal("real-secret", provider.ClientSecret);
    }

    [Fact]
    public async Task SourceOrdersBySortOrderAndIgnoresDisabledRows()
    {
        await using var scope = await SourceScope.CreateAsync();
        var second = CreateEntity("second", "第二", ExternalIdentityProviderType.Oidc, "https://b.example.com", "c", null);
        second.AssignId(8001);
        second.SetSortOrder(20);
        var first = CreateEntity("first", "第一", ExternalIdentityProviderType.Oidc, "https://a.example.com", "c", null);
        first.AssignId(8002);
        first.SetSortOrder(10);
        var disabled = CreateEntity("off", "停用", ExternalIdentityProviderType.Oidc, "https://c.example.com", "c", null);
        disabled.AssignId(8003);
        disabled.Disable();

        await scope.SeedAsync([second, first, disabled]);

        var providers = await scope.Source.GetEnabledAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["first", "second"], providers.Select(static p => p.ProviderCode));
    }

    private static ExternalIdentityProvider CreateEntity(
        string code,
        string displayName,
        ExternalIdentityProviderType type,
        string authority,
        string clientId,
        string? scopes)
        => new(code, displayName, type, authority, clientId, string.Empty, scopes);

    private static ExternalIdentityProvider WithCipher(ExternalIdentityProvider entity, long id, string cipher)
    {
        entity.AssignId(id);
        entity.SetClientSecretCipher(cipher);
        return entity;
    }

    /// <summary>Sqlite 门户库测试作用域 + 真实 Data Protection 临时保护器。</summary>
    private sealed class SourceScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SourceScope(SqliteConnection connection, IDbContextFactory<PortalDbContext> factory, IDataProtectionProvider provider)
        {
            _connection = connection;
            Factory = factory;
            Protector = provider.CreateProtector(QlxDataProtectionPurposes.ExternalIdentityProviderCredentials);
            Source = new PortalExternalIdentityProviderSource(
                factory, provider, NullLogger<PortalExternalIdentityProviderSource>.Instance);
        }

        public IDbContextFactory<PortalDbContext> Factory { get; }

        public IDataProtector Protector { get; }

        public PortalExternalIdentityProviderSource Source { get; }

        public static async Task<SourceScope> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var factory = new SqlitePortalDbContextFactory(connection);
            await using (var db = factory.CreateDbContext())
            {
                await db.Database.EnsureCreatedAsync();
            }

            var protector = new EphemeralDataProtectionProvider();
            return new SourceScope(connection, factory, protector);
        }

        public async Task SeedAsync(IEnumerable<ExternalIdentityProvider> entities)
        {
            await using var db = Factory.CreateDbContext();
            db.ExternalIdentityProviders.AddRange(entities);
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    /// <summary>基于同一 Sqlite 连接的门户库上下文工厂。</summary>
    private sealed class SqlitePortalDbContextFactory(SqliteConnection connection)
        : IDbContextFactory<PortalDbContext>
    {
        public PortalDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<PortalDbContext>()
                .UseSqlite(connection)
                .Options);

        public Task<PortalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }
}
