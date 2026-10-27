/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：商品编码生成器实现——分类编码取自商品分类只读查询，流水经 ISerialSequenceStore 原子取号 + 冲突重试
 */

using System.Globalization;

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.BuildingBlocks.Numbering;
using Qlx.Modules.Stk.Repositories;

namespace Qlx.Modules.Stk.Services.Numbering;

/// <summary>
/// 商品编码生成器。规则固定为 <c>{分类编码}{seq:0000}</c>（如分类码 <c>01</c> → <c>010001</c>）；
/// 分类不存在按 422 校验异常返回，序号冲突最多重试 100 次（N-3-3）。
/// </summary>
/// <param name="categoryQuery">跨模块商品分类只读查询（取分类编码）。</param>
/// <param name="serialSequenceStore">序号存储（原子取号）。</param>
/// <param name="repo">Stk 租户仓储（编码唯一性校验）。</param>
internal sealed class StkItemCodeGenerator(
    IProductCategoryQueryService categoryQuery,
    ISerialSequenceStore serialSequenceStore,
    IStkTenantRepository repo) : IStkItemCodeGenerator
{
    /// <summary>冲突重试上限（与 N-3-3 一致）。</summary>
    private const int MaxRetries = 100;

    /// <summary>序号位数：4 位左补零。</summary>
    private const int SequenceDigits = 4;

    private readonly IProductCategoryQueryService _categoryQuery = categoryQuery;
    private readonly ISerialSequenceStore _serialSequenceStore = serialSequenceStore;
    private readonly IStkTenantRepository _repo = repo;

    /// <inheritdoc />
    public async Task<string> NextAsync(long categoryId, CancellationToken ct)
    {
        ProductCategoryQueryRow category = await _categoryQuery.FindAsync(categoryId, ct).ConfigureAwait(false)
            ?? throw new ValidationException("categoryId", "商品分类不存在");

        string sequenceKey = $"encoding:stk.item:{category.Code}";
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            long sequence = await _serialSequenceStore.GetNextAsync(sequenceKey, ct).ConfigureAwait(false);
            string candidate = category.Code
                + sequence.ToString("D" + SequenceDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (!await _repo.IsItemCodeExistsAsync(candidate, ct).ConfigureAwait(false))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"商品编码生成冲突重试已达上限（{MaxRetries} 次）：categoryId={categoryId}，categoryCode={category.Code}。");
    }
}
