/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-21 00:25:00 +08:00 中国标准时间 星期三
 * 描述：ID 混淆器（IIdObfuscator 适配），内部委托给纯算法 IHashidsService
 */

using Microsoft.Extensions.Options;

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// 业务主键混淆器（IIdObfuscator 适配层）。
/// 内部委托 <see cref="IHashidsService"/> 纯算法实现；
/// 业务代码仅依赖此类型（或接口），与算法实现解耦。
/// </summary>
public sealed class HashidsObfuscator : IIdObfuscator
{
    private readonly IHashidsService _hashids;

    /// <summary>
    /// 使用配置创建混淆器（工厂方式，避免 DI 循环依赖）。
    /// </summary>
    /// <param name="options">混淆配置。</param>
    public HashidsObfuscator(IOptions<IdObfuscationOptions> options)
        : this(options.Value.Salt, options.Value.MinLength, options.Value.Alphabet)
    {
    }

    /// <summary>
    /// 直接指定参数创建混淆器。
    /// 本构造函数仅同程序集（BuildingBlocks）内部使用；外部业务代码只能通过 DI 注入 IIdObfuscator 或 IHashidsService。
    /// </summary>
    /// <param name="salt">混淆盐。</param>
    /// <param name="minLength">混淆字符串最小长度。</param>
    /// <param name="alphabet">自定义字母表；<see langword="null"/> 时使用默认字母表。</param>
    internal HashidsObfuscator(string salt, int minLength = 0, string? alphabet = null)
    {
        _hashids = new HashidsService(salt, minLength, alphabet);
    }

    /// <summary>
    /// 直接注入已创建的 <see cref="IHashidsService"/> 实例（单元测试或多盐场景）。
    /// </summary>
    /// <param name="hashids">Hashids 算法服务。</param>
    public HashidsObfuscator(IHashidsService hashids)
    {
        _hashids = hashids;
    }

    /// <inheritdoc />
    public string Encode(long id) => _hashids.Encode(id);

    /// <inheritdoc />
    public bool TryDecode(string? obfuscated, out long id) => _hashids.TryDecode(obfuscated, out id);
}
