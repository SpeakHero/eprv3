/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：通用业务编码生成器契约——根据业务名称自动生成唯一编码
 */

namespace Qlx.BuildingBlocks.Numbering;

/// <summary>
/// 通用业务编码生成器契约。
/// <para>
/// 根据业务名称（中文）提取拼音作为编码基础，冲突时自动追加递增序号（如 001、002）保证全局唯一。
/// 与具体业务解耦：通过 scope 区分业务域（role / tenant / position 等），
/// 通过 uniquenessChecker 委托由业务方提供数据库存在性校验。
/// </para>
/// </summary>
public interface IEncodingGenerator
{
    /// <summary>
    /// 生成唯一业务编码。
    /// </summary>
    /// <param name="sourceName">业务名称（如角色名、租户名），作为拼音提取源。</param>
    /// <param name="scope">业务域标识（如 "role"、"tenant"、"position"），用于序号键隔离，禁止为空。</param>
    /// <param name="uniquenessChecker">
    /// 唯一性校验委托：传入候选编码，返回 true 表示该编码已存在（冲突），false 表示可用。
    /// 由业务方实现（通常查询数据库唯一索引）。
    /// </param>
    /// <param name="options">生成配置选项；为空时使用默认值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全局唯一的业务编码。</returns>
    /// <exception cref="ArgumentException">源名称为空或 scope 为空时抛出。</exception>
    /// <exception cref="InvalidOperationException">重试次数耗尽仍无法生成唯一编码时抛出。</exception>
    Task<string> GenerateAsync(
        string sourceName,
        string scope,
        Func<string, CancellationToken, Task<bool>> uniquenessChecker,
        EncodingGenerationOptions? options = null,
        CancellationToken cancellationToken = default);
}
