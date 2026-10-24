/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 描述：门户库连接配置项（配置节 Qlx:Portal），供门户模块与四库启动校验共用
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.Configuration;

/// <summary>
/// 门户库连接配置项。对应配置节 <c>Qlx:Portal</c>（与其下 Auth / Provisioning 子节并存）。
/// 门户库承载控制面数据，固定连接串，禁止运行期裸读字符串键。
/// </summary>
public sealed record PortalConnectionOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Portal";

    /// <summary>门户库连接串键名（配置节内的相对键）。</summary>
    public const string PortalConnectionStringKey = "PortalConnectionString";

    /// <summary>门户库固定连接串；生产环境只存密钥引用（经 Data Protection 解密注入真实值）。</summary>
    [Required(AllowEmptyStrings = false)]
    public string PortalConnectionString { get; init; } = string.Empty;
}
