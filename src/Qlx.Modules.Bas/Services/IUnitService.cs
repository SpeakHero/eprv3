/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 16:20:00 +08:00 中国标准时间 星期日
 * 描述：计量单位主数据服务契约——单位组 / 单位 / 单位换算率维护
 */

using Qlx.Modules.Bas.Dtos;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 计量单位主数据服务。单位组、单位、换算关系读路径经统一缓存读穿；
/// 写路径事务提交后落审计并失效 Bas 租户缓存前缀（换算率变更显式留痕旧值/新值）。
/// </summary>
public interface IUnitService
{
    /// <summary>计量单位组扁平列表。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>单位组扁平行列表。</returns>
    Task<IReadOnlyList<UnitGroupRow>> ListUnitGroupsAsync(CancellationToken ct);

    /// <summary>新建或更新计量单位组。</summary>
    /// <param name="payload">保存入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>保存后的单位组扁平行。</returns>
    Task<UnitGroupRow> SaveUnitGroupAsync(SaveUnitGroupPayload payload, CancellationToken ct);

    /// <summary>停用/启用计量单位组。</summary>
    /// <param name="id">单位组 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更后的单位组扁平行。</returns>
    Task<UnitGroupRow> ToggleUnitGroupAsync(long id, CancellationToken ct);

    /// <summary>计量单位扁平列表。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>单位扁平行列表。</returns>
    Task<IReadOnlyList<UnitRow>> ListUnitsAsync(CancellationToken ct);

    /// <summary>新建或更新计量单位。</summary>
    /// <param name="payload">保存入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>保存后的单位扁平行。</returns>
    Task<UnitRow> SaveUnitAsync(SaveUnitPayload payload, CancellationToken ct);

    /// <summary>停用/启用计量单位。</summary>
    /// <param name="id">单位 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更后的单位扁平行。</returns>
    Task<UnitRow> ToggleUnitAsync(long id, CancellationToken ct);

    /// <summary>单位换算关系列表（可按单位组过滤，扁平原始字段）。</summary>
    /// <param name="unitGroupId">单位组 Id（null 表示全部）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>换算关系扁平行列表。</returns>
    Task<IReadOnlyList<UnitConversionRow>> ListConversionsAsync(long? unitGroupId, CancellationToken ct);

    /// <summary>新建或更新单位换算关系（换算率变更显式捕获审计）。</summary>
    /// <param name="payload">保存入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>保存后的换算关系扁平行。</returns>
    Task<UnitConversionRow> SaveConversionAsync(SaveUnitConversionPayload payload, CancellationToken ct);
}
