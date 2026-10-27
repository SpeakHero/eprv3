/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 描述：分页请求基类 + 分页结果基类 + IQueryable 异步分页扩展
 */

using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;

namespace Qlx.BuildingBlocks.Api;

/// <summary>
/// 分页请求基类。Controller 入参只需继承此类即可得到统一分页语义。
/// </summary>
public class PagedRequest
{
    /// <summary>页码（从 1 开始，默认 1）。</summary>
    [Range(1, int.MaxValue, ErrorMessage = "页码必须 ≥ 1")]
    public int Page { get; set; } = 1;

    /// <summary>每页大小（默认 20，最大 200）。</summary>
    [Range(1, 200, ErrorMessage = "每页大小必须在 1 ~ 200 之间")]
    public int PageSize { get; set; } = 20;

    /// <summary>排序字段（可选，对应实体属性名）。</summary>
    public string? OrderBy { get; set; }

    /// <summary>排序方向：asc / desc，默认 asc。</summary>
    public string OrderDirection { get; set; } = "asc";

    /// <summary>计算跳过数量。</summary>
    public int Skip => Math.Max(Page - 1, 0) * Math.Clamp(PageSize, 1, 200);

    /// <summary>计算安全的页大小。</summary>
    public int SafePageSize => Math.Clamp(PageSize, 1, 200);
}

/// <summary>
/// 分页结果基类（只读记录）。
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    long TotalCount,
    int Page,
    int PageSize)
{
    /// <summary>总页数。</summary>
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);

    /// <summary>当前是否最后一页。</summary>
    public bool IsLastPage => Page >= TotalPages;
}

/// <summary>
/// <see cref="IQueryable{T}"/> 异步分页扩展。
/// </summary>
public static class QueryablePagingExtensions
{
    /// <summary>
    /// 异步执行分页查询。
    /// </summary>
    /// <param name="source">查询源。</param>
    /// <param name="request">分页参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<PagedResult<T>> ToPagedAsync<T>(
        this IQueryable<T> source,
        PagedRequest request,
        CancellationToken cancellationToken)
    {
        long total = await source.LongCountAsync(cancellationToken).ConfigureAwait(false);
        List<T> items = await source
            .Skip(request.Skip)
            .Take(request.SafePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<T>(items, total, request.Page, request.SafePageSize);
    }

    /// <summary>
    /// 按动态属性名排序（支持 asc/desc）。
    /// </summary>
    public static IQueryable<T> OrderByDynamic<T>(
        this IQueryable<T> source,
        string propertyName,
        bool descending)
    {
        ParameterExpression parameter = Expression.Parameter(typeof(T), "e");
        MemberExpression property = Expression.Property(parameter, propertyName);
        LambdaExpression lambda = Expression.Lambda(property, parameter);

        string methodName = descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy);
        MethodInfo method = typeof(Queryable).GetMethods()
            .First(m => m.Name == methodName && m.GetParameters().Length == 2)
            !.MakeGenericMethod(typeof(T), property.Type);

        return (IQueryable<T>)method.Invoke(null, [source, lambda])!;
    }
}
