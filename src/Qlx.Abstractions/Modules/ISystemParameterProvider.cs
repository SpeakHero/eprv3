/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 08:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 08:30:00 中国标准时间 星期三
 * 描述：系统参数读取契约：业务运行参数（阈值 X）统一从数据库参数表获取
 */

namespace Qlx.Abstractions.Modules;

/// <summary>
/// 系统参数读取契约（规格铁律：金额阈值、锁定阈值、预警天数等业务运行参数一律参数化，
/// 由系统管理在线维护；实现侧须 Cache-Aside 缓存，读多写少）。
/// <para>参数缺失时调用方提供 fallback（通常等于 config 默认值），保证参数表尚未初始化时功能可用。</para>
/// </summary>
public interface ISystemParameterProvider
{
    /// <summary>
    /// 读取字符串参数值；参数不存在返回 fallback。
    /// </summary>
    /// <param name="paramKey">参数键。</param>
    /// <param name="fallback">参数缺失时的回退值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<string> GetStringAsync(string paramKey, string fallback, CancellationToken cancellationToken);

    /// <summary>
    /// 读取整数参数值；参数不存在或值不可解析为整数时返回 fallback。
    /// </summary>
    /// <param name="paramKey">参数键。</param>
    /// <param name="fallback">参数缺失 / 不可解析时的回退值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<int> GetIntAsync(string paramKey, int fallback, CancellationToken cancellationToken);
}
