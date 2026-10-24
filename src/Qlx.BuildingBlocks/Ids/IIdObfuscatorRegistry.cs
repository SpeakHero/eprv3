/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：混淆器全局注册表——JsonConverter 等静态场景（无法走 DI）通过此处获取 IIdObfuscator。
 *      由 IdsServiceCollectionExtensions 在 AddSingleton<IIdObfuscator> 后立即调用 Register。
 */

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// ID 混淆器全局注册表。解决 JSON Converter 等纯静态上下文无法注入 DI 的问题。
/// </summary>
public static class IIdObfuscatorRegistry
{
    private static volatile IIdObfuscator? _current;

    /// <summary>当前进程唯一混淆器实例。未注册时为 <see langword="null"/>（JsonConverter 会回退裸数字）。</summary>
    public static IIdObfuscator Current => _current ?? throw new InvalidOperationException(
        "IIdObfuscatorRegistry 尚未注册。请在 Host 启动期调用 AddQlxIds 完成注册。");

    /// <summary>注册进程级唯一混淆器实例（AddSingleton 完成后立即调用）。</summary>
    public static void Register(IIdObfuscator obfuscator)
    {
        ArgumentNullException.ThrowIfNull(obfuscator);
        Interlocked.Exchange(ref _current, obfuscator);
    }

    /// <summary>是否已注册（JsonConverter 可用此判断决定是否走混淆分支）。</summary>
    public static bool IsRegistered => _current is not null;
}
