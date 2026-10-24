/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 01:00:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 01:00:00 +08:00 中国标准时间 星期三
 * 描述：全平台统一 JsonSerializerOptions 静态单例（禁止各组件自行 new）。
 *      保持纯净：不引用 BuildingBlocks 层（循环依赖），Converter 注入由 Host 启动阶段显式完成。
 */

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Qlx.Abstractions.Serialization;

/// <summary>
/// 全平台统一 JSON 序列化选项。所有组件一律引用此静态单例，禁止自行 new。
/// </summary>
/// <remarks>
/// <para>设计依据 saas-architecture-spec.md §13 API 设计约定：</para>
    /// <list type="bullet">
    /// <item>Web 默认行为（CamelCase + PropertyNameCaseInsensitive + 不区分大小写）</item>
    /// <item>MaxDepth = 10（兜底安全网，循环引用靠 Repository 层 AsNoTracking + Service 层 Mapper 投影保障）</item>
    /// <item>默认忽略 null 值（<see cref="Default"/>），审计场景使用 <see cref="PreserveNull"/> 保留 null</item>
/// <item>Hashids 混淆器 Converter 不在此注册（避免 Abstractions→BuildingBlocks 循环依赖），
///     由 Host Program.cs 的 <c>AddControllers().AddJsonOptions(...)</c> 显式注入；
///     BuildingBlocks 内部（缓存 / EventBus / 审计序列化）可通过 <see cref="WithConverters"/> 扩展注入</item>
/// </list>
/// </remarks>
public static class QlxJsonDefaults
{
    /// <summary>
    /// 默认序列化选项：CamelCase + 不区分大小写 + MaxDepth 5 + 忽略 null。
    /// 适用于 API 响应、缓存、事件总线等绝大多数场景。
    /// </summary>
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 10,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>
    /// 审计专用选项：保留 null（OldValues/NewValues 快照需完整反映变更，包括字段被置为 null）。
    /// 其余配置与 <see cref="Default"/> 一致。
    /// </summary>
    public static readonly JsonSerializerOptions PreserveNull = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        MaxDepth = 10,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>
    /// 克隆一份选项并追加 Converter（不修改 <see cref="Default"/> 单例本身）。
    /// BuildingBlocks 层内部需要序列化时调用，避免 Converter 污染全局单例。
    /// </summary>
    public static JsonSerializerOptions WithConverters(
        JsonSerializerOptions source,
        params JsonConverter[] converters)
    {
        var clone = new JsonSerializerOptions(source);
        foreach (var converter in converters)
        {
            clone.Converters.Add(converter);
        }

        return clone;
    }
}
