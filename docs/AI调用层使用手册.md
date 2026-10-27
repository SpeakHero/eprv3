# 通用 AI 调用层使用手册

> 位置：`docs/`（源码位于 `src/Qlx.BuildingBlocks/Ai`）
> 版本：1.0.0 beta
> 适用：所有需要调用大模型的业务模块（门户、租户、支撑模块等）
> 关联规格：[spec.md](../.trae/specs/add-ai-invocation-layer/spec.md)

本层对业务只暴露两个契约：`IAiInvoker`（唯一调用入口）与 `AiChatRequest` / `AiChatResponse`。
业务代码不感知厂商名、地址、模型与密钥，也不允许 `new` 任何厂商适配器。

---

## 1. 能力概览

| 能力 | 说明 |
| --- | --- |
| 统一契约 | `IAiChatClient` 定义单厂商调用；`IAiInvoker` 定义业务入口，方法签名统一为 `(AiChatRequest, CancellationToken)` |
| 工厂模式 | `IAiChatClientFactory` 按「厂商名 → 配置 `Type` → 适配器」动态实例化，业务零硬编码 |
| 多厂商 | 内置 6 家商业厂商（智谱 / 阿里百炼 / 火山方舟 / 硅基流动 / DeepSeek / 百度千帆）+ OpenAI 兼容网关 + 本地 Ollama |
| 调用策略 | `Fixed`（固定单厂商）/ `RoundRobin`（按优先级逐家尝试、故障自动转移） |
| 标准化异常 | 全部失败抛 `AiInvocationException`（`BusinessException` 子类，HTTP 层映射 422），携带完整调用链 |
| 结构化日志 | 每次厂商尝试与整体结束均有固定事件 Id（4101~4105），含耗时、状态码、错误码 |
| 配置覆盖 | 全部配置项支持环境变量覆盖（`Qlx__Ai__...`），不同部署环境无需改文件 |
| 可扩展 | 新增厂商 = 新增一个 Provider 实现 + 注册一行 + 配置项，核心调用逻辑零改动 |

---

## 2. 快速上手

### 2.1 启用调用层

AI 层默认**关闭**（`Qlx:Ai:Enabled=false`），避免无密钥环境误外呼。启用需两步：

1. 修改 [config/common/ai.json](../config/common/ai.json)：`"Enabled": true`；
2. 注入密钥环境变量（禁止写入 JSON）：

```powershell
# Windows PowerShell（开发机）
$env:Qlx__Ai__Providers__zhipu__ApiKey = "<你的密钥>"
```

```bash
# Linux 容器
docker run -e Qlx__Ai__Providers__zhipu__ApiKey=<你的密钥> ...
```

### 2.2 注入并调用

业务服务只需依赖 `IAiInvoker`（已由 `AddQlxAi` 注册为 Singleton，线程安全）：

```csharp
using Qlx.Abstractions.Ai;
using Qlx.Abstractions.Exceptions;

public sealed class CompanyNameParseService(IAiInvoker aiInvoker)
{
    public async Task<string> ParseAsync(string companyName, CancellationToken cancellationToken)
    {
        AiChatResponse response = await aiInvoker.InvokeAsync(
            new AiChatRequest
            {
                Purpose = "tenant-company-name-parse", // 仅用于日志排障
                Messages =
                [
                    new AiMessage(AiMessageRole.System, "你是企业名称结构化解析助手，只输出 JSON。"),
                    new AiMessage(AiMessageRole.User, companyName),
                ],
                ResponseFormat = AiResponseFormat.Json,
                Temperature = 0,
                MaxTokens = 512,
            },
            cancellationToken).ConfigureAwait(false);

        return response.Content;
    }
}
```

### 2.3 响应字段

| 字段 | 含义 |
| --- | --- |
| `Content` | 模型输出正文（`Json` 格式时已保证是合法 JSON） |
| `ProviderName` | 实际应答的厂商名（排查「这次是哪家返回的」） |
| `Model` | 实际使用的模型（`request.Model ?? 厂商配置 Model`） |
| `DurationMilliseconds` | 含故障转移在内的本次调用耗时 |
| `PromptTokens` / `CompletionTokens` | 用量（厂商未返回时为 `null`） |
| `FinishReason` | 结束原因（厂商透传，可能为 `null`） |

---

## 3. 配置说明

配置节：`Qlx:Ai`（[config/common/ai.json](../config/common/ai.json)），字段与 `QlxAiOptions` 一一对应。

### 3.1 全局配置

| 键 | 默认值 | 说明 |
| --- | --- | --- |
| `Enabled` | `false` | 总开关；`false` 时调用立即抛 `AI_DISABLED`，不发起任何 HTTP 请求 |
| `Strategy` | `Fixed` | `Fixed` / `RoundRobin`（大小写不敏感），非法值启动失败 |
| `FixedProvider` | `zhipu` | 固定模式目标厂商名；为空时取启用厂商中 `Priority` 最小者 |
| `TimeoutSeconds` | `30` | 全局单厂默认超时（秒，1~600）；厂商可单独覆盖 |
| `LogRequestContent` | `false` | 是否记录请求消息正文；默认只记参数摘要 |
| `MaxLogContentLength` | `200` | 开启正文日志时的单条截断长度（字符，1~4000） |
| `Providers` | 厂商名字典 | 键为厂商名，值为厂商配置（见 3.2） |

> `Providers` 必须是**厂商名字典**而不是数组。数组下标会随增删厂商漂移，导致
> `Qlx__Ai__Providers__0__ApiKey` 这类环境变量串到别的厂商上。

### 3.2 单个厂商配置（`Providers:{厂商名}`）

| 键 | 默认值 | 说明 |
| --- | --- | --- |
| `Type` | `openai-compatible` | 厂商类型，决定使用哪个协议适配器（大小写不敏感） |
| `BaseUrl` | 空 | 接口基地址；为空时使用该类型的默认地址（`openai-compatible` 无默认值，必须配置） |
| `Model` | 空 | 默认模型名；请求 `Model` 优先 |
| `ApiKey` | 空 | 密钥；**禁止明文写入配置文件**，经环境变量注入 |
| `Enabled` | `true` | 是否参与候选（固定模式目标也必须为 `true`） |
| `Priority` | `100` | 数值越小越优先；轮询模式按此升序尝试 |
| `TimeoutSeconds` | 空 | 厂商单独超时；空则用全局值 |

### 3.3 内置厂商与默认地址

| `Type` | 默认 `BaseUrl` | 是否必须 `ApiKey` | 备注 |
| --- | --- | --- | --- |
| `zhipu` | `https://open.bigmodel.cn/api/paas/v4/` | 是 | 智谱 GLM |
| `dashscope` | `https://dashscope.aliyuncs.com/compatible-mode/v1/` | 是 | 阿里百炼兼容模式 |
| `volcengine` | `https://ark.cn-beijing.volces.com/api/v3/` | 是 | 火山方舟；`Model` 按控制台模型 ID / 接入点 ID 填写 |
| `siliconflow` | `https://api.siliconflow.cn/v1/` | 是 | 硅基流动 |
| `deepseek` | `https://api.deepseek.com/v1/` | 是 | DeepSeek |
| `qianfan` | `https://qianfan.baidubce.com/v2/` | 是 | 百度千帆 |
| `openai-compatible` | 无（必须配 `BaseUrl`） | 是 | 任意 OpenAI 兼容网关 |
| `ollama` | `http://localhost:11434/` | 否 | 本地开源模型（`/api/chat`，非 `/chat/completions`） |

### 3.4 环境变量覆盖

配置加载优先级（低 → 高）：
默认源 → `config/common/*.json` → `config/portal|tenant/*.json` → `config/modules/**` → `config/environments/{环境}/**` → **环境变量**。

环境变量用双下划线表示层级：

| 需求 | 环境变量 |
| --- | --- |
| 切换为轮询 | `Qlx__Ai__Strategy=RoundRobin` |
| 改固定目标 | `Qlx__Ai__FixedProvider=dashscope` |
| 开正文日志 | `Qlx__Ai__LogRequestContent=true` |
| 全局超时 | `Qlx__Ai__TimeoutSeconds=60` |
| 厂商密钥 | `Qlx__Ai__Providers__zhipu__ApiKey=...` |
| 厂商模型 | `Qlx__Ai__Providers__volcengine__Model=...` |
| 停用某厂商 | `Qlx__Ai__Providers__deepseek__Enabled=false` |

> 策略、厂商集合与超时在**启动期定稿**（`IOptions` 与命名 `HttpClient` 均在此刻构造），
> 变更后需重启进程才生效；仅密钥类变更同样建议重启以避免与连接池超时不一致。

---

## 4. 调用策略

### 4.1 固定模式（`Fixed`，默认）

- 只调用 `FixedProvider` 指定的厂商；未填写时取**启用厂商中 `Priority` 最小者**。
- 该厂商失败即抛 `AiInvocationException`，**不会**降级到其他厂商。
- 适用：对模型一致性要求高、或只在某一家有配额的场景。

### 4.2 轮询模式（`RoundRobin`）

- 候选 = 所有 `Enabled=true` 的厂商，按 `Priority` 升序排列，同优先级按厂商名 ordinal 排序。
- 逐家调用，**任一家返回有效结果立即返回**，后续厂商不再调用（故障自动转移）。
- 全部失败时抛 `AI_INVOCATION_FAILED`，`Message` 内联每家厂商与失败原因。
- 单厂商内**不自动重试**：一次调用 = 一次尝试。
- 适用：免费额度互补、多厂商容灾。示例配置（优先级 10 → 20 → 30）：

```json
"Strategy": "RoundRobin",
"Providers": {
  "zhipu":      { "Type": "zhipu",      "Model": "glm-4.7-flash", "Priority": 10 },
  "dashscope":  { "Type": "dashscope",  "Model": "qwen-plus",     "Priority": 20 },
  "siliconflow":{ "Type": "siliconflow","Model": "Qwen/Qwen2.5-7B-Instruct", "Priority": 30 }
}
```

### 4.3 有效结果判定

满足全部条件才算成功，否则记为 `INVALID_RESPONSE` 并继续下一家：

1. HTTP 2xx；
2. `Content` 非空白；
3. `ResponseFormat = Json` 时 `Content` 能被 `JsonDocument.Parse` 解析。

HTTP 401 / 403 / 429 视为该厂商失败（错误码 `HTTP_401` / `HTTP_403` / `HTTP_429`），轮询模式下自动尝试下一家。

---

## 5. 异常处理

### 5.1 错误码

| 错误码 | 抛出位置 | 含义 |
| --- | --- | --- |
| `AI_DISABLED` | `IAiInvoker` | 调用层未启用 |
| `AI_PROVIDER_NOT_CONFIGURED` | 工厂 / Invoker / 适配器 | 厂商未配置、目标厂商不存在或已停用、`BaseUrl` / `Model` / `ApiKey` 缺失（此时不发请求） |
| `AI_PROVIDER_TYPE_UNSUPPORTED` | 工厂 | 配置的 `Type` 未注册 |
| `AI_INVOCATION_FAILED` | `IAiInvoker` | 全部候选厂商失败 |
| `TIMEOUT` / `NETWORK_ERROR` / `UNEXPECTED_ERROR` | 尝试级 | 超时 / 网络异常 / 未归类异常 |
| `INVALID_RESPONSE` | 尝试级 | 空内容或非法 JSON |
| `HTTP_{状态码}` | 尝试级 | 厂商返回非 2xx |

### 5.2 读取调用链

```csharp
try
{
    response = await aiInvoker.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
}
catch (AiInvocationException ex)
{
    // ex.ErrorCode：AI_DISABLED / AI_PROVIDER_NOT_CONFIGURED / ... / AI_INVOCATION_FAILED
    // ex.Message：面向用户的可读摘要（内联每个厂商名与失败原因）
    foreach (AiInvocationAttempt attempt in ex.Attempts)
    {
        // Index / ProviderName / ProviderType / Model / DurationMilliseconds
        // Outcome / HttpStatusCode / ErrorCode / ErrorMessage（已截断 ≤500 字符）
    }

    if (ex.ErrorCode == AiErrorCodes.InvocationFailed)
    {
        // 全部厂商不可用：建议提示用户稍后重试或走人工兜底
    }
}
```

`AiInvocationException.Details` 同步携带 `new { Attempts }`，可被统一异常中间件序列化（业务异常 → HTTP 422）。

> 调用方传入的 `CancellationToken` 被取消时，**原样抛出** `OperationCanceledException`，不会被包装成业务异常；请照常让取消向上传播。

---

## 6. 日志与排障

### 6.1 事件表（`ILogger<AiInvoker>`，源生成）

| EventId | 级别 | 时机 | 关键字段 |
| --- | --- | --- | --- |
| 4101 | Information | 调用开始 | `CorrelationId`、`Purpose`、`Strategy`、`CandidateCount`、`Model`、`Temperature`、`MaxTokens`、`ResponseFormat`、`MessageCount`、`ContentPreview` |
| 4102 | Information | 单厂商成功 | `Provider`、`ProviderType`、`Model`、`AttemptIndex`、`DurationMilliseconds`、`HttpStatusCode`、`ContentLength` |
| 4103 | Warning | 单厂商失败 | `Provider`、`ProviderType`、`AttemptIndex`、`DurationMilliseconds`、`Outcome`、`HttpStatusCode`、`ErrorCode`、`ErrorMessage` |
| 4104 | Information | 整体成功 | `Strategy`、`AttemptCount`、`TotalDurationMilliseconds`、`Provider` |
| 4105 | Error | 整体失败 | `Strategy`、`AttemptCount`、`TotalDurationMilliseconds`、`ErrorCodes` |

同一次调用用 `CorrelationId`（12 位）串联全部日志。

### 6.2 常用排查路径

```text
1. 按 CorrelationId 找到 4101，确认 Strategy 与候选数（CandidateCount）；
2. 看 4103 的 ErrorCode / HttpStatusCode：
   - HTTP_401 / HTTP_403 → 密钥无效、未开通模型或账号无权限
   - HTTP_429          → 配额耗尽 / 限流，轮询模式会自动换下一家
   - TIMEOUT           → 调大 TimeoutSeconds 或调整优先级
   - NETWORK_ERROR     → DNS / 代理 / 出网策略
3. 全部失败看 4105 的 ErrorCodes 与异常的 Attempts 明细。
```

想看到请求正文，把 `LogRequestContent` 设为 `true`（生产慎用，正文按 `MaxLogContentLength` 截断，密钥永不记录）。

---

## 7. 新增 AI 厂商

### 7.1 OpenAI 兼容厂商（绝大多数情况）

协议与 `/chat/completions` 一致时，只写一个薄封装类：

```csharp
namespace Qlx.BuildingBlocks.Ai.Providers;

/// <summary>某新厂商适配器工厂（复用 OpenAI 兼容协议内核）。</summary>
public sealed class NewVendorChatClientProvider(IHttpClientFactory httpClientFactory)
    : OpenAiCompatibleProviderBase(httpClientFactory)
{
    /// <inheritdoc />
    public override string Type => "new-vendor"; // 需与配置 Type 一致

    /// <inheritdoc />
    public override string? DefaultBaseUrl => "https://api.new-vendor.com/v1/";
}
```

然后在 [AiServiceCollectionExtensions.cs](../src/Qlx.BuildingBlocks/Ai/AiServiceCollectionExtensions.cs) 的 Provider 注册区加一行：

```csharp
_ = services.AddSingleton<IAiChatClientProvider, NewVendorChatClientProvider>();
```

最后在 [config/common/ai.json](../config/common/ai.json) 增加配置：

```json
"new-vendor": { "Type": "new-vendor", "Model": "<默认模型>", "Enabled": true, "Priority": 80 }
```

`AiInvoker`、`AiChatClientFactory`、`QlxAiOptions` **均无需改动**；密钥经
`Qlx__Ai__Providers__new-vendor__ApiKey` 注入。

### 7.2 非兼容协议（如 Ollama）

需要自定义请求/响应编解码时：

1. 实现 `IAiChatClient`（参考 [Providers/OllamaChatClient.cs](../src/Qlx.BuildingBlocks/Ai/Providers/OllamaChatClient.cs)）：自行拼 URL、序列化请求、解析响应，失败抛 `AiProviderException`；
2. 实现 `IAiChatClientProvider`（参考 [Providers/OllamaChatClientProvider.cs](../src/Qlx.BuildingBlocks/Ai/Providers/OllamaChatClientProvider.cs)）：声明 `Type` / `DefaultBaseUrl`，`Create` 返回自己的适配器；
3. 注册一行 + 配置项，同上。

约定要点：适配器只做「单厂商协议编解码 + HTTP」，**不重试、不降级、不做策略判断**；策略与日志统一由 `AiInvoker` 承担。

---

## 8. 安全与约束

- **密钥不入库文件**：`config/*.json` 只允许留注释，密钥一律经环境变量或密钥引用注入（规则 C4）。
- `AiProviderOptions.ToString()` 对 `ApiKey` 掩码为「已配置 / 未配置」；任何日志分支都不输出密钥。
- 厂商响应片段在异常与尝试明细中截断 ≤500 字符。
- 适配器内部请求 DTO 全部显式 `[JsonPropertyName]`（如 `max_tokens`），序列化统一走 `QlxJsonDefaults.Default`，禁止 `new JsonSerializerOptions`。
- 公开方法 `CancellationToken` 必选且无默认值；禁止内部 `new CancellationTokenSource()`。
- 本层不新增端点、不落库、不缓存结果；结果是否缓存由业务模块按缓存规范（C-x）自行决定。

---

## 9. 常见问题

| 现象 | 原因与处理 |
| --- | --- |
| 启动报「Qlx:Ai 已启用，但没有任何 Enabled=true 的厂商」 | `Enabled=true` 但所有厂商都被停用；至少启用一家 |
| 启动报「固定模式目标厂商未配置或已停用」 | `FixedProvider` 拼写错误或对应 `Enabled=false` |
| 启动报「BaseUrl 无法解析」 | 使用了 `openai-compatible` 却未配 `BaseUrl`（该类型无默认地址） |
| 启动报「厂商的 Model 未配置」 | 启用状态下每个厂商都必须有 `Model`（或请求里显式传 `Model`） |
| 调用抛 `AI_DISABLED` | `Qlx:Ai:Enabled` 仍为 `false` |
| 配置改了但行为没变 | 策略 / 厂商 / 超时为启动期定稿，需重启 |
| 日志里没有消息正文 | 默认 `LogRequestContent=false`；需要时置 `true` |
| 轮询模式下总是最后一家成功 | 前序厂商密钥无效或配额耗尽，看 4103 的 `ErrorCode` |
| Ollama 调用失败 | 确认本地 `ollama serve` 已启动、`Model` 已 `ollama pull`，且 `BaseUrl` 可达 |

---

## 10. 文件索引

| 文件 | 作用 |
| --- | --- |
| [QlxAiOptions.cs](../src/Qlx.BuildingBlocks/Ai/QlxAiOptions.cs) / [AiProviderOptions.cs](../src/Qlx.BuildingBlocks/Ai/AiProviderOptions.cs) | 配置模型（全局 / 单厂商） |
| [QlxAiOptionsValidator.cs](../src/Qlx.BuildingBlocks/Ai/QlxAiOptionsValidator.cs) | 启动期配置校验 |
| [IAiChatClientProvider.cs](../src/Qlx.BuildingBlocks/Ai/IAiChatClientProvider.cs) / [IAiChatClientFactory.cs](../src/Qlx.BuildingBlocks/Ai/IAiChatClientFactory.cs) | 厂商工厂契约 |
| [AiChatClientFactory.cs](../src/Qlx.BuildingBlocks/Ai/AiChatClientFactory.cs) | 厂商名 → 类型 → 实例 |
| [AiInvoker.cs](../src/Qlx.BuildingBlocks/Ai/AiInvoker.cs) | 策略、故障转移、日志、异常 |
| [AiServiceCollectionExtensions.cs](../src/Qlx.BuildingBlocks/Ai/AiServiceCollectionExtensions.cs) | `AddQlxAi` 装配 |
| [Providers/](../src/Qlx.BuildingBlocks/Ai/Providers) | 8 个厂商适配器与协议内核 |
| [src/Qlx.Abstractions/Ai](../src/Qlx.Abstractions/Ai) | 对外契约与错误码 |
| [AiInvocationException.cs](../src/Qlx.Abstractions/Exceptions/AiInvocationException.cs) | 标准化业务异常 |
| [config/common/ai.json](../config/common/ai.json) | 默认配置 |
| [tests/Qlx.BuildingBlocks.Tests/Ai](../tests/Qlx.BuildingBlocks.Tests/Ai) | 单测（工厂 / 装配 / 适配器 / 校验 / 策略与日志） |
