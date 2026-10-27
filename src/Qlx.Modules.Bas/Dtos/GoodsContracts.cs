/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 16:10:00 +08:00 中国标准时间 星期日
 * 描述：Bas 商品分类与计量单位 DTO——仅扁平原始字段与数值枚举（B-2），树组装与中文映射由前端完成；
 *       新建/更新入参不含 Code（编码由后端生成，N-1），业务 Id 由全局 Hashids 元数据自动混淆
 */

namespace Qlx.Modules.Bas.Dtos;

// ================ 商品分类 ================

/// <summary>
/// 商品分类扁平行（原始字段）。<c>Status</c> 为数值枚举，分类树由前端按 <c>ParentId</c>/<c>LevelNo</c> 组装。
/// </summary>
/// <param name="Id">分类 Id。</param>
/// <param name="ParentId">父级分类 Id（null 为顶级）。</param>
/// <param name="Code">分类编码（层级连续编码，不可修改）。</param>
/// <param name="Name">分类名称。</param>
/// <param name="LevelNo">层级号（顶级为 1）。</param>
/// <param name="UnitGroupId">默认计量单位组 Id。</param>
/// <param name="Status">启停状态（1=启用 / 2=停用）。</param>
/// <param name="UpdatedAt">更新时间。</param>
public sealed record ItemCategoryRow(
    long Id,
    long? ParentId,
    string Code,
    string Name,
    int LevelNo,
    long? UnitGroupId,
    int Status,
    DateTimeOffset UpdatedAt);

/// <summary>新建商品分类入参（无编码字段，编码由后端统一发号器生成）。</summary>
/// <param name="Name">分类名称。</param>
/// <param name="ParentId">父级分类 Id（null 为顶级）。</param>
/// <param name="UnitGroupId">默认计量单位组 Id。</param>
public sealed record CreateItemCategoryPayload(
    string Name,
    long? ParentId,
    long? UnitGroupId);

/// <summary>更新商品分类入参（编码与父级层级不可变更）。</summary>
/// <param name="Name">分类名称。</param>
/// <param name="UnitGroupId">默认计量单位组 Id。</param>
public sealed record UpdateItemCategoryPayload(
    string Name,
    long? UnitGroupId);

/// <summary>分类停用审批挂起载荷（审批通过后停用该分类）。</summary>
/// <param name="CategoryId">分类 Id。</param>
public sealed record CategoryDisablePayload(long CategoryId);

// ================ 计量单位组 ================

/// <summary>计量单位组扁平行。</summary>
/// <param name="Id">单位组 Id。</param>
/// <param name="Code">单位组编码（后端生成，不可修改）。</param>
/// <param name="Name">单位组名称。</param>
/// <param name="Status">启停状态（1=启用 / 2=停用）。</param>
/// <param name="UpdatedAt">更新时间。</param>
public sealed record UnitGroupRow(
    long Id,
    string Code,
    string Name,
    int Status,
    DateTimeOffset UpdatedAt);

/// <summary>计量单位组新建/更新入参（无编码字段）。</summary>
/// <param name="Id">单位组 Id（null 表示新建）。</param>
/// <param name="Name">单位组名称。</param>
public sealed record SaveUnitGroupPayload(
    long? Id,
    string Name);

// ================ 计量单位 ================

/// <summary>计量单位扁平行。</summary>
/// <param name="Id">单位 Id。</param>
/// <param name="UnitGroupId">所属计量单位组 Id。</param>
/// <param name="Code">单位编码（后端生成，不可修改）。</param>
/// <param name="Name">单位名称。</param>
/// <param name="Precision">数量精度（小数位 0~8）。</param>
/// <param name="Status">启停状态（1=启用 / 2=停用）。</param>
/// <param name="UpdatedAt">更新时间。</param>
public sealed record UnitRow(
    long Id,
    long UnitGroupId,
    string Code,
    string Name,
    int Precision,
    int Status,
    DateTimeOffset UpdatedAt);

/// <summary>计量单位新建/更新入参（无编码字段）。</summary>
/// <param name="Id">单位 Id（null 表示新建）。</param>
/// <param name="Name">单位名称。</param>
/// <param name="UnitGroupId">所属计量单位组 Id。</param>
/// <param name="Precision">数量精度（0~8）。</param>
public sealed record SaveUnitPayload(
    long? Id,
    string Name,
    long UnitGroupId,
    int Precision);

// ================ 单位换算 ================

/// <summary>单位换算关系扁平行。</summary>
/// <param name="Id">换算关系 Id。</param>
/// <param name="UnitGroupId">所属计量单位组 Id。</param>
/// <param name="FromUnitId">源单位 Id。</param>
/// <param name="ToUnitId">目标单位 Id。</param>
/// <param name="Rate">换算率（1 源单位 = Rate 目标单位）。</param>
/// <param name="Status">启停状态（1=启用 / 2=停用）。</param>
/// <param name="UpdatedAt">更新时间。</param>
public sealed record UnitConversionRow(
    long Id,
    long UnitGroupId,
    long FromUnitId,
    long ToUnitId,
    decimal Rate,
    int Status,
    DateTimeOffset UpdatedAt);

/// <summary>单位换算关系新建/更新入参。</summary>
/// <param name="Id">换算关系 Id（null 表示新建）。</param>
/// <param name="UnitGroupId">所属计量单位组 Id。</param>
/// <param name="FromUnitId">源单位 Id。</param>
/// <param name="ToUnitId">目标单位 Id。</param>
/// <param name="Rate">换算率（必须大于 0）。</param>
public sealed record SaveUnitConversionPayload(
    long? Id,
    long UnitGroupId,
    long FromUnitId,
    long ToUnitId,
    decimal Rate);
