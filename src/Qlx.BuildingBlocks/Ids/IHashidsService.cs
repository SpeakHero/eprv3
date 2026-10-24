/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 00:20:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 00:20:00 +08:00 中国标准时间 星期三
 * 描述：Hashids 纯算法服务契约（与具体业务解耦，可独立复用做短链、分享码等）
 */

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// Hashids 纯算法服务契约。按 hashids 规范实现字母表经盐一致性洗牌、
/// 编码引入守卫符与分隔符；同一盐下编码结果稳定且可逆。
/// </summary>
/// <remarks>
/// 非加密手段，仅用于对外隐藏真实数值的量级与顺序。
/// 业务场景包括：ID 对外混淆（<see cref="IIdObfuscator"/> 适配）、
/// URL 短链、分享码、邀请码等。
/// </remarks>
public interface IHashidsService
{
    /// <summary>
    /// 将 long 数值编码为混淆字符串。
    /// </summary>
    /// <param name="number">非负整数。</param>
    /// <returns>混淆后的字符串；0 也会产生有效输出。</returns>
    string Encode(long number);

    /// <summary>
    /// 将混淆字符串解码回 long 数值。
    /// </summary>
    /// <param name="obfuscated">混淆字符串；允许 null / 空白。</param>
    /// <param name="number">解码后的数值；解码失败为 0。</param>
    /// <returns>解码成功返回 true；失败返回 false（含回编码校验）。</returns>
    bool TryDecode(string? obfuscated, out long number);
}
