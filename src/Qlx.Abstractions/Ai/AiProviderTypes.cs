/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 厂商类型标识常量
 */

namespace Qlx.Abstractions.Ai;

/// <summary>
/// AI 厂商类型标识常量。对应配置 <c>Qlx:Ai:Providers:{厂商名}:Type</c>。
/// </summary>
public static class AiProviderTypes
{
    /// <summary>任意 OpenAI 兼容网关（无默认地址，必须配置 <c>BaseUrl</c>）。</summary>
    public const string OpenAiCompatible = "openai-compatible";

    /// <summary>智谱 GLM。</summary>
    public const string Zhipu = "zhipu";

    /// <summary>阿里百炼（DashScope 兼容模式）。</summary>
    public const string DashScope = "dashscope";

    /// <summary>火山方舟。</summary>
    public const string Volcengine = "volcengine";

    /// <summary>硅基流动。</summary>
    public const string SiliconFlow = "siliconflow";

    /// <summary>DeepSeek。</summary>
    public const string DeepSeek = "deepseek";

    /// <summary>百度千帆。</summary>
    public const string Qianfan = "qianfan";

    /// <summary>本地 Ollama。</summary>
    public const string Ollama = "ollama";
}
