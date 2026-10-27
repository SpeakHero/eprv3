/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 23:50:00 +08:00 中国标准时间 星期日
 * 描述：Bas 数据字典 DTO——仅扁平原始字段与数值枚举（B-2），中文映射与展示排序由前端完成
 */

namespace Qlx.Modules.Bas.Dtos;

// ================ 字典类型 ================

/// <summary>
/// 字典类型扁平行（原始字段）。<c>OwnerLevel</c>/<c>Status</c> 为数值枚举。
/// </summary>
/// <param name="Id">字典类型 Id。</param>
/// <param name="TypeCode">类型编码（创建后不可修改）。</param>
/// <param name="TypeName">类型名称。</param>
/// <param name="OwnerLevel">归属层级（1=集团级 / 2=子公司级）。</param>
/// <param name="OrgId">所属组织 Id（子公司级有值，集团级为空）。</param>
/// <param name="Status">启停状态（1=启用 / 2=停用）。</param>
/// <param name="IsSystem">是否系统内置（内置类型不可删除，仅可启停）。</param>
/// <param name="UpdatedAt">更新时间。</param>
public sealed record DictTypeRow(
    long Id,
    string TypeCode,
    string TypeName,
    int OwnerLevel,
    long? OrgId,
    int Status,
    bool IsSystem,
    DateTimeOffset UpdatedAt);

/// <summary>字典类型新建入参（类型编码由用户指定，创建后不可修改）。</summary>
/// <param name="TypeCode">类型编码。</param>
/// <param name="TypeName">类型名称。</param>
/// <param name="OwnerLevel">归属层级数值（1=集团级 / 2=子公司级）。</param>
/// <param name="OrgId">所属组织 Id（子公司级必填）。</param>
public sealed record SaveDictTypePayload(
    string TypeCode,
    string TypeName,
    int OwnerLevel,
    long? OrgId);

/// <summary>字典类型更新入参（类型编码与归属层级不可修改）。</summary>
/// <param name="TypeName">新名称。</param>
public sealed record UpdateDictTypePayload(string TypeName);

// ================ 字典项 ================

/// <summary>
/// 字典项扁平行（原始字段）。<c>Status</c> 为数值枚举。
/// </summary>
/// <param name="Id">字典项 Id。</param>
/// <param name="TypeCode">所属类型编码。</param>
/// <param name="ItemCode">项编码（创建后不可修改）。</param>
/// <param name="ItemName">项名称。</param>
/// <param name="SortNo">类型内排序号（升序）。</param>
/// <param name="Status">启停状态（1=启用 / 2=停用）。</param>
/// <param name="IsSystem">是否系统内置（内置项不可删除，仅可启停）。</param>
/// <param name="UpdatedAt">更新时间。</param>
public sealed record DictItemRow(
    long Id,
    string TypeCode,
    string ItemCode,
    string ItemName,
    int SortNo,
    int Status,
    bool IsSystem,
    DateTimeOffset UpdatedAt);

/// <summary>字典项新建入参（项编码创建后不可修改）。</summary>
/// <param name="ItemCode">项编码。</param>
/// <param name="ItemName">项名称。</param>
/// <param name="SortNo">排序号。</param>
public sealed record SaveDictItemPayload(
    string ItemCode,
    string ItemName,
    int SortNo);

/// <summary>字典项更新入参（项编码不可修改）。</summary>
/// <param name="ItemName">新名称。</param>
/// <param name="SortNo">新排序号。</param>
public sealed record UpdateDictItemPayload(
    string ItemName,
    int SortNo);
