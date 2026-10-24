/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22
 * 描述：对外 Id 字符串统一解析入口（JSON 反序列化与 ModelBinder 共用）：
 *       优先按 Hashids 解码，失败时回退纯数字，保证历史裸数字入参依旧兼容。
 */

using System.Globalization;

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// 对外 Id 字符串解析器。收发边界（JSON Converter / ModelBinder）统一走此入口，禁止各自实现解析逻辑。
/// </summary>
internal static class ObfuscatedIdParser
{
    /// <summary>
    /// 解析对外 Id：先按 Hashids 解码，失败且为纯数字时回退为原始 <see cref="long"/>。
    /// </summary>
    /// <param name="raw">对外 Id 字符串（混淆串或纯数字）。</param>
    /// <param name="id">解析成功时的真实 Id。</param>
    /// <returns>解析是否成功。</returns>
    public static bool TryParse(string? raw, out long id)
    {
        id = 0L;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        // 运行期按需取混淆器：宿主启动顺序不受约束（先注册后解析均可）
        if (IIdObfuscatorRegistry.IsRegistered && IIdObfuscatorRegistry.Current.TryDecode(raw, out id))
        {
            return true;
        }

        // 兼容回退：历史/种子数据直接传裸数字
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
    }
}
