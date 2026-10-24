/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 22:30:00 +08:00 中国标准时间 星期四
 * 描述：实体级表名前缀特性，单模块多前缀场景（如 Support 模块 fil_/job_/msg_）由实体自带前缀
 */

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 实体级租户表名前缀特性。标注在实体类上时，<see cref="TenantDbContext.OnModelCreating"/>
/// 以特性前缀 + 实体名 snake_case 生成表名；未标注实体回落 DbContext 模块前缀
/// （<see cref="TenantDbContext.GetModuleTablePrefix"/>）。
/// 用于一个模块 DbContext 承载多张前缀表（如 Support 模块 fil_/job_/msg_/apr_/prt_）。
/// </summary>
/// <remarks>
/// 传空串（<c>""</c>）表示显式跳过任何前缀拼接：表名即 DbSet 属性名 snake_case
/// （适用于 DbSet 名已自带完整前缀语义的场景，如 <c>JobDefinitions</c> → <c>job_definitions</c>）。
/// </remarks>
/// <param name="prefix">表名前缀（须以 <c>_</c> 结尾，如 <c>"fil_"</c>；空串表示跳过前缀）。</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TenantTablePrefixAttribute(string prefix) : Attribute
{
    /// <summary>表名前缀（空串表示跳过前缀拼接）。</summary>
    public string Prefix { get; } = ValidatePrefix(prefix);

    private static string ValidatePrefix(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // 空串合法：显式声明不叠加前缀（表名 = DbSet 名 snake_case，自带前缀语义）
        if (value.Length == 0)
        {
            return value;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!value.EndsWith('_'))
        {
            throw new ArgumentException($"表名前缀必须以 '_' 结尾（实际值：{value}）", nameof(value));
        }

        if (value.Contains(' ', StringComparison.Ordinal) || value.Contains('.', StringComparison.Ordinal))
        {
            throw new ArgumentException($"表名前缀禁止包含空格或点号（实际值：{value}）", nameof(value));
        }

        return value;
    }
}
