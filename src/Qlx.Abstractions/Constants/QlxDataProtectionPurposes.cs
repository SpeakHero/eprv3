/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 16:00:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-20 16:00:00 +08:00 中国标准时间 星期一
 * 描述：Data Protection Purpose 常量集中管理，全局唯一入口，禁止各模块硬编码 purpose 字符串
 */

namespace Qlx.Abstractions.Constants;

/// <summary>
/// 全局统一的 Data Protection Purpose 常量。
/// 所有模块调用 <see cref="Microsoft.AspNetCore.DataProtection.IDataProtectionProvider.CreateProtector"/> 时
/// 必须从此类引用，禁止在模块内硬编码 purpose 字符串。
/// <para>
/// 命名规则：<c>Qlx.Erpv3.{业务域}.{用途}</c>，避免不同 purpose 之间相互误用，
/// 同时为运维排查（Data Protection 密文失效定位）提供稳定锚点。
/// </para>
/// <para>
/// 新增 purpose 时在此类追加字段，禁止在模块内声明私有常量。
/// </para>
/// </summary>
public static class QlxDataProtectionPurposes
{
    /// <summary>门户租户数据库凭据（DbUser / DbPassword）加解密 purpose。</summary>
    public const string TenantDatabaseCredentials = "Qlx.Erpv3.TenantDatabaseCredentials";

    // 后续业务域按需追加：
    // public const string UserSensitiveProfile = "Qlx.Erpv3.User.SensitiveProfile";
    // public const string SmtpPassword          = "Qlx.Erpv3.Smtp.Password";
}
