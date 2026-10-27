/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存台账精度统一助手——数量/单价 4 位、金额 2 位，一律 AwayFromZero 舍入（spec §6-4）
 */

namespace Qlx.Modules.Stk.Domain.Inventory;

/// <summary>
/// 库存台账精度助手。数量与单价保留 4 位、金额保留 2 位，
/// 舍入方式统一 <see cref="MidpointRounding.AwayFromZero"/>，避免银行家舍入导致金额漂移。
/// </summary>
internal static class StockMath
{
    /// <summary>数量小数位。</summary>
    public const int QtyDigits = 4;

    /// <summary>单价小数位。</summary>
    public const int CostDigits = 4;

    /// <summary>金额小数位。</summary>
    public const int AmountDigits = 2;

    /// <summary>按指定小数位四舍五入（远离零）。</summary>
    /// <param name="value">原始值。</param>
    /// <param name="digits">保留小数位。</param>
    /// <returns>舍入后的值。</returns>
    public static decimal Round(decimal value, int digits)
        => decimal.Round(value, digits, MidpointRounding.AwayFromZero);
}
