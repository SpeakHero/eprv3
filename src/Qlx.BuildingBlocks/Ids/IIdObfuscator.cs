/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：对外 ID 混淆契约，API 收发边界的真实 long 与混淆字符串互转
 */

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// 对外 ID 混淆器。进程内部（事件契约、查询接口、数据库）一律使用真实 long；
/// 对外 API 收发边界经本混淆器加解密，禁止把真实主键暴露给外部。
/// </summary>
public interface IIdObfuscator
{
    /// <summary>
    /// 将真实 ID 编码为混淆字符串。
    /// </summary>
    /// <param name="id">真实 ID，必须为非负数。</param>
    /// <returns>混淆字符串。</returns>
    string Encode(long id);

    /// <summary>
    /// 尝试将混淆字符串解码为真实 ID。
    /// </summary>
    /// <param name="obfuscated">混淆字符串。</param>
    /// <param name="id">解码成功时输出真实 ID。</param>
    /// <returns>解码是否成功；输入非法或不属于本混淆器（盐/字母表不符）时为 <see langword="false"/>。</returns>
    bool TryDecode(string? obfuscated, out long id);
}
