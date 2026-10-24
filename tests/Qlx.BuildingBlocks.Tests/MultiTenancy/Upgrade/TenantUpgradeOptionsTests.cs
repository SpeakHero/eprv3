/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 17:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 17:30:00 +08:00 中国标准时间 星期四
 * 描述：租户升级与隔离巡检配置项默认值 / 数据注解校验测试
 */

using System.ComponentModel.DataAnnotations;
using Qlx.BuildingBlocks.MultiTenancy.Upgrade;

namespace Qlx.BuildingBlocks.Tests.MultiTenancy.Upgrade;

/// <summary>
/// <see cref="TenantUpgradeOptions"/> / <see cref="TenantIsolationAuditOptions"/> 默认值与
/// <see cref="RangeAttribute"/> 边界校验测试。
/// </summary>
public sealed class TenantUpgradeOptionsTests
{
    [Fact]
    public void UpgradeOptionsHasSafeDefaults()
    {
        var options = new TenantUpgradeOptions();

        Assert.True(options.Enabled);
        Assert.Equal(15, options.ScanIntervalSeconds);
        Assert.Equal(1000, options.BatchSize);
        Assert.Equal(100, options.VerifySampleSize);
        Assert.Equal(30, options.RetentionDays);
        Assert.Equal(5000, options.CleanBatchSize);
        Assert.Equal(24, options.CleanIntervalHours);
        Assert.Equal("Qlx:Tenancy:Upgrade", TenantUpgradeOptions.SectionName);
    }

    [Fact]
    public void UpgradeOptionsDefaultsPassDataAnnotationValidation()
    {
        var options = new TenantUpgradeOptions();
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();

        Assert.True(Validator.TryValidateObject(options, context, results, validateAllProperties: true));
    }

    [Fact]
    public void BatchSizeBelowRangeIsRejected()
    {
        var options = new TenantUpgradeOptions { BatchSize = 99 };

        Assert.False(TryValidate(options));
    }

    [Fact]
    public void BatchSizeAboveRangeIsRejected()
    {
        var options = new TenantUpgradeOptions { BatchSize = 100001 };

        Assert.False(TryValidate(options));
    }

    [Fact]
    public void ZeroSampleSizeMeansRowCountOnlyAndIsAllowed()
    {
        var options = new TenantUpgradeOptions { VerifySampleSize = 0 };

        Assert.True(TryValidate(options));
    }

    [Fact]
    public void RetentionDaysOutOfRangeIsRejected()
    {
        Assert.False(TryValidate(new TenantUpgradeOptions { RetentionDays = 0 }));
        Assert.False(TryValidate(new TenantUpgradeOptions { RetentionDays = 366 }));
    }

    [Fact]
    public void IsolationAuditOptionsHasSafeDefaults()
    {
        var options = new TenantIsolationAuditOptions();

        Assert.True(options.Enabled);
        Assert.Equal(24, options.IntervalHours);
        Assert.Equal("Qlx:Tenancy:IsolationAudit", TenantIsolationAuditOptions.SectionName);
    }

    [Fact]
    public void IsolationAuditIntervalOutOfRangeIsRejected()
    {
        Assert.False(TryValidate(new TenantIsolationAuditOptions { IntervalHours = 0 }));
        Assert.False(TryValidate(new TenantIsolationAuditOptions { IntervalHours = 169 }));
    }

    private static bool TryValidate(object instance)
    {
        var context = new ValidationContext(instance);
        return Validator.TryValidateObject(instance, context, [], validateAllProperties: true);
    }
}
