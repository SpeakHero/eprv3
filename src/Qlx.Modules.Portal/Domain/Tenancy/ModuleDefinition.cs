/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 描述：系统模块定义实体，门户侧登记平台所有可授权模块的元数据
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Portal.Domain.Tenancy;

/// <summary>
/// 模块分类。
/// </summary>
public enum ModuleCategory
{
    /// <summary>核心业务模块（采购/销售/商品等）。</summary>
    Business = 0,

    /// <summary>平台支撑模块（审批/信息/任务/文件）。</summary>
    Support = 1,

    /// <summary>门户管理模块（租户/许可/身份）。</summary>
    Portal = 2,
}

/// <summary>
/// 系统模块定义（表前缀 por_）。门户侧登记平台所有可授权模块的元数据，
/// 与租户侧 TenantModule 形成主从关系。模块编码必须与各模块 IModule.Manifest.ModuleCode 一致。
/// 继承 <see cref="Qlx.Abstractions.Domain.AuditableEntity"/>：雪花 Id 主键 + 审计字段由拦截器自动填充。
/// 原业务唯一键 ModuleCode 改为唯一索引。
/// </summary>
[Table("por_module_definitions")]
[Index(nameof(ModuleCode), IsUnique = true)]
public sealed class ModuleDefinition : Qlx.Abstractions.Domain.AuditableEntity
{
    /// <summary>模块编码（业务唯一键，对应 IModule.Manifest.ModuleCode）。</summary>
    [MaxLength(64)]
    public string ModuleCode { get; private init; } = string.Empty;

    /// <summary>模块显示名称（中文）。</summary>
    [MaxLength(128)]
    public string DisplayName { get; private init; } = string.Empty;

    /// <summary>模块分类。</summary>
    public ModuleCategory Category { get; private init; } = ModuleCategory.Business;

    /// <summary>模块当前版本（SemVer）。</summary>
    [MaxLength(32)]
    public string CurrentVersion { get; private init; } = string.Empty;

    /// <summary>模块表前缀（如 bas_ / pur_ / sal_）。</summary>
    [MaxLength(16)]
    public string TablePrefix { get; private init; } = string.Empty;

    /// <summary>模块程序集名称（约定 Qlx.Modules.{Name}）。</summary>
    [MaxLength(128)]
    public string AssemblyName { get; private init; } = string.Empty;

    /// <summary>是否支持按需授权（门户可对租户单独启停）。</summary>
    public bool Licensable { get; private init; } = true;

    /// <summary>是否为平台必备模块（所有租户默认启用，不可停用）。</summary>
    public bool IsMandatory { get; private init; }

    /// <summary>模块简介。</summary>
    [MaxLength(512)]
    public string? Description { get; private init; }
}
