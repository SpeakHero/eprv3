/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 16:20:00 +08:00 中国标准时间 星期日
 * 描述：商品分类主数据服务契约——多级分类维护、层级编码、停用审批挂起与应用
 */

using Qlx.Modules.Bas.Dtos;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 商品分类主数据服务。读路径经统一缓存读穿；写路径事务提交后落审计并失效 Bas 租户缓存前缀。
/// </summary>
public interface IItemCategoryService
{
    /// <summary>分类扁平列表（不组装树，B-2-1）。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>分类扁平行列表。</returns>
    Task<IReadOnlyList<ItemCategoryRow>> ListAsync(CancellationToken ct);

    /// <summary>分类详情。</summary>
    /// <param name="id">分类 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>分类扁平行。</returns>
    Task<ItemCategoryRow> GetAsync(long id, CancellationToken ct);

    /// <summary>新建分类（编码按父编码 + 两位流水自动生成，层级受参数上限约束）。</summary>
    /// <param name="payload">新建入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的分类扁平行。</returns>
    Task<ItemCategoryRow> CreateAsync(CreateItemCategoryPayload payload, CancellationToken ct);

    /// <summary>更新分类（名称与默认计量单位组）。</summary>
    /// <param name="id">分类 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的分类扁平行。</returns>
    Task<ItemCategoryRow> UpdateAsync(long id, UpdateItemCategoryPayload payload, CancellationToken ct);

    /// <summary>停用/启用分类。</summary>
    /// <param name="id">分类 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更后的分类扁平行。</returns>
    Task<ItemCategoryRow> ToggleAsync(long id, CancellationToken ct);

    /// <summary>软删分类（存在下级分类时阻断）。</summary>
    /// <param name="id">分类 Id。</param>
    /// <param name="ct">取消令牌。</param>
    Task DeleteAsync(long id, CancellationToken ct);

    /// <summary>提交分类停用审批申请（挂起，审批通过后停用）。</summary>
    /// <param name="id">分类 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求行。</returns>
    Task<ApprovalRequestRow> SubmitDisableRequestAsync(long id, CancellationToken ct);

    /// <summary>审批通过后停用分类（由审批应用器在事务内调用，缓存失效由应用器统一执行）。</summary>
    /// <param name="id">分类 Id。</param>
    /// <param name="ct">取消令牌。</param>
    Task ApplyDisableAsync(long id, CancellationToken ct);
}
