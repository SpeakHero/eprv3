/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 描述：Mapperly 身份映射器单元测试（ID 混淆 + 角色字符串化）
 */

using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Domain.Identity;

namespace Qlx.BuildingBlocks.Tests.Ids;

public sealed class IdentityMappersTests
{
    [Fact]
    public void MapsRowToSummaryWithObfuscatedTenantId()
    {
        var obfuscator = new HashidsObfuscator(new HashidsService("test-salt"));
        var mapper = new IdentityMappers(obfuscator);
        var lastLogin = new DateTimeOffset(2026, 10, 21, 6, 0, 0, TimeSpan.Zero);
        var row = new TenantMemberRow(42, "qlx-dev", "开发租户", "91510100MA0000000X", TenantMemberRole.TenantAdmin, lastLogin);

        var item = mapper.ToTenantSummaryItem(row);

        Assert.Equal(obfuscator.Encode(42), item.TenantId);
        Assert.Equal("qlx-dev", item.TenantCode);
        Assert.Equal("开发租户", item.DisplayName);
        Assert.Equal("91510100MA0000000X", item.TaxNo);
        Assert.Equal("TenantAdmin", item.MemberRole);
        Assert.Equal(lastLogin, item.LastLoginAt);
    }

    [Fact]
    public void MapsOrdinaryMemberRoleAsString()
    {
        var obfuscator = new HashidsObfuscator(new HashidsService("test-salt"));
        var mapper = new IdentityMappers(obfuscator);
        var row = new TenantMemberRow(7, "t2", "租户二", null, TenantMemberRole.OrdinaryMember, null);

        var item = mapper.ToTenantSummaryItem(row);

        Assert.Equal("OrdinaryMember", item.MemberRole);
        Assert.Null(item.TaxNo);
        Assert.Null(item.LastLoginAt);
    }
}
