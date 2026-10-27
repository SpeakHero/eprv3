/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：商品编码生成器契约——「分类编码 + 4 位流水」（偏差 A-12：不走 IEncodingGenerator）
 */

namespace Qlx.Modules.Stk.Services.Numbering;

/// <summary>商品编码生成器。</summary>
internal interface IStkItemCodeGenerator
{
    /// <summary>按分类编码生成下一个商品编码（<c>{分类编码}{序号:0000}</c>）。</summary>
    /// <param name="categoryId">商品分类 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>租户内唯一的商品编码。</returns>
    Task<string> NextAsync(long categoryId, CancellationToken ct);
}
