/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 厂商命名 HttpClient 名称构建器
 */

namespace Qlx.BuildingBlocks.Ai;

/// <summary>
/// AI 厂商命名 <see cref="System.Net.Http.IHttpClientFactory"/> 名称构建器。
/// 装配阶段按同一规则注册，适配器按同一规则取用，避免名称散装拼写。
/// </summary>
public static class AiHttpClientNames
{
    /// <summary>
    /// 构建指定厂商的命名 HttpClient 名称（<c>qlx-ai:{厂商名}</c>）。
    /// </summary>
    /// <param name="providerName">厂商名。</param>
    /// <returns>命名 HttpClient 名称。</returns>
    public static string For(string providerName) => $"qlx-ai:{providerName}";
}
