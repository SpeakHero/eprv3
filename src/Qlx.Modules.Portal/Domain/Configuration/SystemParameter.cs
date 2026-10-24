/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 08:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 08:30:00 +08:00 中国标准时间 星期三
 * 描述：门户系统参数实体：业务运行参数（阈值 X）在线维护载体，键唯一
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Portal.Domain.Configuration;

/// <summary>
/// 门户级系统参数（por_parameters）。承载业务运行参数（规格铁律"阈值 X 参数化，
/// 系统管理在线维护"的门户侧落地）：登录安全阈值、会话策略等。
/// <para>静态部署配置走 config/*.json，业务运行参数走本表——边界禁止混淆。</para>
/// </summary>
[Table("por_parameters")]
[Index(nameof(ParamKey), IsUnique = true)]
public sealed class SystemParameter : AuditableEntity
{
    /// <summary>参数键（点分小写约定，如 auth.max_password_fail_attempts），全表唯一。</summary>
    [MaxLength(128)]
    public string ParamKey { get; private set; }

    /// <summary>参数值（字符串承载，数值/布尔由读取方按类型解析）。</summary>
    [MaxLength(1024)]
    public string ParamValue { get; private set; }

    /// <summary>参数说明（供系统管理界面展示）。</summary>
    [MaxLength(512)]
    public string? Description { get; private set; }

    private SystemParameter()
    {
        ParamKey = string.Empty;
        ParamValue = string.Empty;
    }

    /// <summary>
    /// 创建系统参数。
    /// </summary>
    /// <param name="paramKey">参数键（点分小写，不可空）。</param>
    /// <param name="paramValue">参数值。</param>
    /// <param name="description">参数说明（可选）。</param>
    public SystemParameter(string paramKey, string paramValue, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(paramValue);

        ParamKey = paramKey;
        ParamValue = paramValue;
        Description = description;

        // Portal 侧刻意不挂审计拦截器（1A 约定），审计字段由实体自行填充
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>更新参数值（系统管理在线维护）。</summary>
    public void UpdateValue(string paramValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramValue);
        ParamValue = paramValue;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>更新参数值与说明（管理端点调用，同时维护更新人审计字段）。</summary>
    public void Update(string paramValue, string? description, string? updatedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramValue);
        ParamValue = paramValue;
        Description = description;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>补充创建人审计字段（Portal 侧无审计拦截器，由服务层显式调用）。</summary>
    public void MarkCreated(string? createdBy)
    {
        CreatedBy = createdBy;
        UpdatedBy = createdBy;
    }
}
