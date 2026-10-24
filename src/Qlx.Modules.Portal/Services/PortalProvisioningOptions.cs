/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 18:45:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 22:20:00 +08:00 中国标准时间 星期三
 * 描述：新建租户的数据库预配参数（强类型 Options，禁止硬编码主机/库名前缀）
 */

using System.ComponentModel.DataAnnotations;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Tenancy;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 新建租户预配参数（配置节 <c>Qlx:Portal:Provisioning</c>）。
/// </summary>
public sealed record PortalProvisioningOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Portal:Provisioning";

    /// <summary>租户数据库主机（开发环境 LocalDB）。</summary>
    [Required(AllowEmptyStrings = false)]
    public string DbHost { get; init; } = PortalConstants.ProvisioningDefaults.DbHost;

    /// <summary>租户数据库端口。</summary>
    [Range(1, 65535)]
    public int DbPort { get; init; } = PortalConstants.ProvisioningDefaults.DbPort;

    /// <summary>租户数据库名前缀（最终库名 = 前缀 + 租户编码变形）。</summary>
    [Required(AllowEmptyStrings = false)]
    public string DbNamePrefix { get; init; } = PortalConstants.ProvisioningDefaults.DbNamePrefix;

    /// <summary>试用版默认订阅天数（其他等级无到期时间为 null）。</summary>
    [Range(0, 3650)]
    public int TrialDays { get; init; } = PortalConstants.ProvisioningDefaults.TrialDays;

    /// <summary>
    /// 新建请求未显式指定数据库模式时的默认模式。
    /// 独立库命名规则不受影响（仍为 前缀 + 租户编码）。
    /// </summary>
    public TenantDatabaseMode DefaultDatabaseMode { get; init; } = TenantDatabaseMode.Shared;

    /// <summary>新租户默认授权模块编码（必备模块由模块定义的 IsMandatory 自动补充）。</summary>
    public IReadOnlyList<string> DefaultModuleCodes { get; init; } = QlxModuleCodes.Mandatory;
}
