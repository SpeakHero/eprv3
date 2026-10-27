/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 23:55:00 +08:00 中国标准时间 星期日
 * 描述：数据字典服务契约——两级可见性查询、写保护与启停管理
 */

using Qlx.Modules.Bas.Dtos;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 数据字典服务。读路径两级可见性（集团级 + 本子公司级），集团级仅租户根组织可维护；
/// 系统内置类型 / 字典项不可删除、仅可启停；被业务引用的字典项仅可停用。
/// </summary>
public interface IDictionaryService
{
    /// <summary>字典类型扁平列表（归属层级 / 状态筛选，按当前成员可见范围过滤）。</summary>
    Task<IReadOnlyList<DictTypeRow>> ListTypesAsync(int? ownerLevel, int? status, CancellationToken ct);

    /// <summary>字典类型详情。</summary>
    Task<DictTypeRow> GetTypeAsync(long id, CancellationToken ct);

    /// <summary>新建字典类型。</summary>
    Task<DictTypeRow> CreateTypeAsync(SaveDictTypePayload payload, CancellationToken ct);

    /// <summary>重命名字典类型（编码不可修改）。</summary>
    Task<DictTypeRow> UpdateTypeAsync(long id, UpdateDictTypePayload payload, CancellationToken ct);

    /// <summary>启停字典类型。</summary>
    Task<DictTypeRow> ToggleTypeAsync(long id, CancellationToken ct);

    /// <summary>指定类型下的字典项扁平列表（状态筛选，按排序号升序）。</summary>
    Task<IReadOnlyList<DictItemRow>> ListItemsAsync(string typeCode, int? status, CancellationToken ct);

    /// <summary>新增字典项。</summary>
    Task<DictItemRow> CreateItemAsync(string typeCode, SaveDictItemPayload payload, CancellationToken ct);

    /// <summary>更新字典项名称与排序号（项编码不可修改）。</summary>
    Task<DictItemRow> UpdateItemAsync(long id, UpdateDictItemPayload payload, CancellationToken ct);

    /// <summary>启停字典项。</summary>
    Task<DictItemRow> ToggleItemAsync(long id, CancellationToken ct);

    /// <summary>删除字典项（系统内置 / 被业务引用时拒绝，仅可停用）。</summary>
    Task DeleteItemAsync(long id, CancellationToken ct);
}
