/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25
 * 描述：QlxAuthOptions.Validate 白盒测试——全部校验分支（Issuer/Audience/生命周期/锁定阈值/密码策略/证书节）
 *       与 ToString 敏感字段掩码
 */

using Qlx.Modules.Portal.Auth;

namespace Qlx.BuildingBlocks.Tests.Portal.Auth;

public sealed class QlxAuthOptionsValidationTests
{
    private static string[] Validate(QlxAuthOptions options)
        => [.. options.Validate(null!).Select(r => r.ErrorMessage!)];

    [Fact]
    public void DefaultOptionsAreValid()
    {
        Assert.Empty(Validate(new QlxAuthOptions()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-uri")]
    public void InvalidIssuerRejected(string? issuer)
    {
        Assert.Contains(Validate(new QlxAuthOptions { Issuer = issuer! }), m => m.Contains("Issuer"));
    }

    [Fact]
    public void EmptyAudienceRejected()
    {
        Assert.Contains(Validate(new QlxAuthOptions { Audience = " " }), m => m.Contains("Audience"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public void AccessTokenLifetimeOutOfRangeRejected(int minutes)
    {
        Assert.Contains(
            Validate(new QlxAuthOptions { AccessTokenLifetimeMinutes = minutes }),
            m => m.Contains("AccessTokenLifetimeMinutes"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1440)]
    public void AccessTokenLifetimeBoundaryAccepted(int minutes)
    {
        Assert.Empty(Validate(new QlxAuthOptions { AccessTokenLifetimeMinutes = minutes }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public void RefreshTokenLifetimeOutOfRangeRejected(int days)
    {
        Assert.Contains(
            Validate(new QlxAuthOptions { RefreshTokenLifetimeDays = days }),
            m => m.Contains("RefreshTokenLifetimeDays"));
    }

    [Fact]
    public void MaxPasswordFailAttemptsBelowOneRejected()
    {
        Assert.Contains(
            Validate(new QlxAuthOptions { MaxPasswordFailAttempts = 0 }),
            m => m.Contains("MaxPasswordFailAttempts"));
    }

    [Fact]
    public void PasswordFailWindowBelowOneRejected()
    {
        Assert.Contains(
            Validate(new QlxAuthOptions { PasswordFailWindowMinutes = 0 }),
            m => m.Contains("PasswordFailWindowMinutes"));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(129)]
    public void MinPasswordLengthOutOfRangeRejected(int length)
    {
        Assert.Contains(
            Validate(new QlxAuthOptions { MinPasswordLength = length }),
            m => m.Contains("MinPasswordLength"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(25)]
    public void PasswordNeverRepeatOutOfRangeRejected(int n)
    {
        Assert.Contains(
            Validate(new QlxAuthOptions { PasswordNeverRepeatLastN = n }),
            m => m.Contains("PasswordNeverRepeatLastN"));
    }

    [Fact]
    public void HistoryRetentionBelowRepeatWindowRejected()
    {
        Assert.Contains(
            Validate(new QlxAuthOptions { PasswordNeverRepeatLastN = 5, PasswordHistoryRetentionCount = 4 }),
            m => m.Contains("PasswordHistoryRetentionCount"));
    }

    [Fact]
    public void NegativeRenewalGraceRejected()
    {
        Assert.Contains(
            Validate(new QlxAuthOptions { TenantTokenRenewalGraceMinutes = -1 }),
            m => m.Contains("TenantTokenRenewalGraceMinutes"));
    }

    [Fact]
    public void CertificateSourceEmptySkipsCertificateValidation()
    {
        Assert.Empty(Validate(new QlxAuthOptions
        {
            Certificate = new QlxAuthOptions.ProductionCertificateOptions { Source = "" },
        }));
    }

    [Fact]
    public void CertificateFileSourceWithoutPathRejected()
    {
        Assert.Contains(
            Validate(new QlxAuthOptions
            {
                Certificate = new QlxAuthOptions.ProductionCertificateOptions
                {
                    Source = QlxAuthOptions.ProductionCertificateOptions.SourceFile,
                },
            }),
            m => m.Contains("Path"));
    }

    [Fact]
    public void CertificateStoreSourceWithoutThumbprintRejected()
    {
        Assert.Contains(
            Validate(new QlxAuthOptions
            {
                Certificate = new QlxAuthOptions.ProductionCertificateOptions
                {
                    Source = QlxAuthOptions.ProductionCertificateOptions.SourceStore,
                },
            }),
            m => m.Contains("Thumbprint"));
    }

    [Fact]
    public void CertificateStoreSourceWithThumbprintAccepted()
    {
        Assert.Empty(Validate(new QlxAuthOptions
        {
            Certificate = new QlxAuthOptions.ProductionCertificateOptions
            {
                Source = QlxAuthOptions.ProductionCertificateOptions.SourceStore,
                Thumbprint = "A1B2C3D4E5F6",
            },
        }));
    }

    [Fact]
    public void CertificateUnknownSourceRejected()
    {
        Assert.Contains(
            Validate(new QlxAuthOptions
            {
                Certificate = new QlxAuthOptions.ProductionCertificateOptions { Source = "Vault" },
            }),
            m => m.Contains("Certificate.Source"));
    }

    [Fact]
    public void CertificateFileSourceWithPathAccepted()
    {
        Assert.Empty(Validate(new QlxAuthOptions
        {
            Certificate = new QlxAuthOptions.ProductionCertificateOptions
            {
                Source = QlxAuthOptions.ProductionCertificateOptions.SourceFile,
                Path = "C:\\certs\\qlx.pfx",
            },
        }));
    }

    [Fact]
    public void ToStringMasksDevSeedAdminPassword()
    {
        var text = new QlxAuthOptions { DevSeedAdminPassword = "SuperSecret123" }.ToString();

        Assert.DoesNotContain("SuperSecret123", text, StringComparison.Ordinal);
        Assert.Contains("***", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToStringShowsNullPasswordAsNull()
    {
        var text = new QlxAuthOptions().ToString();

        Assert.Contains("DevSeedAdminPassword = null", text, StringComparison.Ordinal);
    }
}
