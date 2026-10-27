/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：跨模块客户只读查询契约——销售/信用校验等下游模块实时读取 Bas 客户扁平数据与信用额度时使用。
 */

namespace Qlx.Abstractions.Queries;

/// <summary>
/// 客户扁平只读视图（bas_customers 投影）。
/// <para>仅下发原始字段：<c>CreditGrade</c> / <c>Status</c> 为数值枚举，中文映射在前端公共层维护（B-2-5）；
/// 金额字段为原始 decimal 数值，不做显示格式化（B-2-2）。</para>
/// <para><c>Id</c> 为进程内雪花 long，进出 API 时由全局 Hashids JSON 元数据自动混淆。</para>
/// </summary>
/// <param name="Id">客户 Id（bas_customers.id）。</param>
/// <param name="Code">客户编码。</param>
/// <param name="Name">客户名称。</param>
/// <param name="CreditGrade">信用等级数值枚举（A/B/C/D）。</param>
/// <param name="CreditLimit">信用额度（原始金额）。</param>
/// <param name="CreditUsed">已占用信用额度（原始金额）。</param>
/// <param name="Status">客户状态数值枚举（Pending/Active/Frozen/Closed）。</param>
public sealed record CustomerQueryRow(
    long Id,
    string Code,
    string Name,
    int CreditGrade,
    decimal CreditLimit,
    decimal CreditUsed,
    int Status);

/// <summary>
/// 跨模块客户只读查询契约。Bas 模块负责实现并强制当前租户过滤；
/// 消费方禁止自行跨库 Join，一律经本接口。
/// </summary>
public interface ICustomerQueryService : IModuleQuery
{
    /// <summary>
    /// 按客户 Id 查询单条客户。
    /// </summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>客户行；不存在返回 <see langword="null"/>。</returns>
    Task<CustomerQueryRow?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>
    /// 查询客户信用额度与已占用额度（下单额度校验专用，仅返回最小金额事实）。
    /// </summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>信用额度与已占用额度；客户不存在返回 <see langword="null"/>。</returns>
    Task<(decimal Limit, decimal Used)?> GetCreditAsync(long id, CancellationToken cancellationToken);
}
