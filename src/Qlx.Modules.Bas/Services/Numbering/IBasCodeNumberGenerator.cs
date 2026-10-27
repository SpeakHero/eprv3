/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 12:00:00 +08:00 中国标准时间 星期日
 * 描述：Bas 主数据统一发号器契约（禁止使用 IDocumentNumberGenerator 生成主数据编码）
 */

namespace Qlx.Modules.Bas.Services.Numbering;

/// <summary>
/// Bas 主数据编码发号器。按租户 + 业务类型的 <c>bas_code_rules</c> 分段模板生成编码，
/// 流水段经 <see cref="Qlx.BuildingBlocks.Numbering.ISerialSequenceStore"/> 原子取号。
/// </summary>
internal interface IBasCodeNumberGenerator
{
    /// <summary>
    /// 生成下一个业务编码。
    /// </summary>
    /// <param name="bizType">业务类型（org / supplier / customer / employee / labour / category）。</param>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="parentCode">上级编码（模板含 <c>{parent}</c> 段时使用）。</param>
    /// <param name="typeCode">类型代码（模板含 <c>{type}</c> 段时使用，如组织类型 GS/SUB/BM/XM）。</param>
    /// <param name="isConflict">唯一性校验委托：返回 <see langword="true"/> 表示编码已存在需换号重试。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>租户内唯一的业务编码。</returns>
    Task<string> NextAsync(
        string bizType,
        long tenantId,
        string? parentCode,
        string? typeCode,
        Func<string, CancellationToken, Task<bool>> isConflict,
        CancellationToken ct);
}
