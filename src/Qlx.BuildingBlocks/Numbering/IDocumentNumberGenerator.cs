/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 描述：单据编号生成器契约，输出「业务前缀+组织码+期间+流水」格式编号
 */

namespace Qlx.BuildingBlocks.Numbering;

/// <summary>
/// 单据编号生成器契约。按「业务前缀-组织码-期间-流水」规则生成编号，
/// 流水号按「租户 + 前缀 + 组织码 + 期间」维度独立递增，跨期间自动重置；
/// 调用方必须处于已解析租户的请求上下文（共享库下多租户序号互不干扰）。
/// </summary>
public interface IDocumentNumberGenerator
{
    /// <summary>
    /// 生成下一个单据编号。
    /// </summary>
    /// <param name="prefix">业务前缀（如 CG、XS），对应各单据类型约定前缀。</param>
    /// <param name="orgCode">组织编码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>格式化后的单据编号。</returns>
    /// <exception cref="ArgumentException">前缀或组织码为空时抛出。</exception>
    Task<string> NextAsync(string prefix, string orgCode, CancellationToken cancellationToken);
}
