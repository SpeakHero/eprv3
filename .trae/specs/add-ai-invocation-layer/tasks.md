# Tasks：add-ai-invocation-layer

> 关联规格：[spec.md](./spec.md) ｜ 验收：[checklist.md](./checklist.md)
> 执行原则：每个阶段结束立即 `dotnet build`（`TreatWarningsAsErrors`，必须 0 错误 0 警告），通过后再进入下一阶段。

## 阶段 A：配置体系与环境变量覆盖

- [x] A1 修改 `src/Qlx.BuildingBlocks/Configuration/QlxConfigurationExtensions.cs`
  - 在四级 JSON 源全部加载后追加 `builder.AddEnvironmentVariables()`（环境变量优先级最高）
  - 更新 XML 注释说明覆盖规则（`Qlx__Ai__Providers__zhipu__ApiKey` 双下划线映射）与文件头版本/修改时间
  - 保留 `QLX_CONFIG_ROOT` 解析逻辑不变
- [x] A2 新增单测 `tests/Qlx.BuildingBlocks.Tests/Configuration/QlxConfigurationEnvironmentOverrideTests.cs`
  - 临时目录放 `common/ai.json`（`Strategy=Fixed`、`Providers.zhipu.Model=glm-4.7-flash`），设 `QLX_CONFIG_ROOT` 指向该目录
  - 设环境变量 `Qlx__Ai__Strategy=RoundRobin`、`Qlx__Ai__Providers__zhipu__ApiKey=test-key`
  - 断言配置读取为 `RoundRobin` 与 `test-key`；`finally` 恢复环境变量与 `QLX_CONFIG_ROOT`
- [x] A3 新增 `config/common/ai.json`
  - `Qlx:Ai`：`Enabled=false`、`Strategy=Fixed`、`FixedProvider=zhipu`、`TimeoutSeconds=30`、`LogRequestContent=false`、`MaxLogContentLength=200`
  - `Providers` 为**厂商名字典**：`zhipu` / `dashscope` / `volcengine` / `siliconflow` / `deepseek` / `qianfan` / `ollama`（Ollama 默认 `Enabled=false`，其余按调研给默认模型；`BaseUrl` 可省略以使用厂商默认）
  - 文件内注释：API Key 禁止明文入 JSON，经 `Qlx__Ai__Providers__<厂商名>__ApiKey` 注入（C4）
- [x] A4 `dotnet build` 通过

## 阶段 B：统一契约与标准化异常

- [x] B1 新增 `src/Qlx.Abstractions/Ai/` 契约
  - `AiMessageRole.cs`（`System=1` / `User=2` / `Assistant=3`）
  - `AiResponseFormat.cs`（`Text=0` / `Json=1`）
  - `AiMessage.cs`（`record (AiMessageRole Role, string Content)`）
  - `AiChatRequest.cs`（`Purpose` / `Messages`(required) / `Model` / `Temperature` / `MaxTokens` / `ResponseFormat`，init 属性 + 默认值）
  - `AiChatResponse.cs`（`Content` / `ProviderName` / `Model` / `DurationMilliseconds` / `PromptTokens` / `CompletionTokens` / `FinishReason`，支持 `with` 覆盖耗时）
  - `IAiChatClient.cs`（`ProviderName` + `Task<AiChatResponse> ChatAsync(AiChatRequest, CancellationToken)`）
  - `IAiInvoker.cs`（`Task<AiChatResponse> InvokeAsync(AiChatRequest, CancellationToken)`）
  - `AiAttemptOutcome.cs`（`Success=1` / `Failed=2`）
  - `AiInvocationAttempt.cs`（`Index`/`ProviderName`/`ProviderType`/`Model`/`DurationMilliseconds`/`Outcome`/`HttpStatusCode`/`ErrorCode`/`ErrorMessage`）
  - `AiProviderException.cs`（适配器失败信号：`ProviderName`/`HttpStatusCode`/`ErrorCode`/`Message`，可带 inner）
  - `AiErrorCodes.cs`（`AI_DISABLED` / `AI_PROVIDER_NOT_CONFIGURED` / `AI_PROVIDER_TYPE_UNSUPPORTED` / `AI_INVOCATION_FAILED` / `AI_REQUEST_INVALID`；尝试级 `TIMEOUT` / `NETWORK_ERROR` / `INVALID_RESPONSE` / `UNEXPECTED_ERROR`；`HttpStatus(int)` 生成 `HTTP_{code}`）
  - `AiProviderTypes.cs`（`Zhipu` / `DashScope` / `Volcengine` / `SiliconFlow` / `DeepSeek` / `Qianfan` / `OpenAiCompatible` / `Ollama` 常量）
  - 所有文件带项目标准文件头
- [x] B2 新增 `src/Qlx.Abstractions/Exceptions/AiInvocationException.cs`
  - `sealed class AiInvocationException : BusinessException`
  - 构造函数 `(string errorCode, string message, IReadOnlyList<AiInvocationAttempt> attempts, Exception? innerException = null)`
  - `Details` 携带 `new { Attempts = attempts }`；`Attempts` 只读属性
- [x] B3 `dotnet build` 通过

## 阶段 C：Options、校验、工厂与厂商实现

- [x] C1 新增 `src/Qlx.BuildingBlocks/Ai/QlxAiOptions.cs`
  - `SectionName = "Qlx:Ai"`；`Enabled`(false) / `Strategy`("Fixed") / `FixedProvider` / `TimeoutSeconds`(30) / `LogRequestContent`(false) / `MaxLogContentLength`(200)
  - `Providers`：`Dictionary<string, AiProviderOptions>`（默认空字典，查找时按 `OrdinalIgnoreCase`）
  - 内部常量类 `Strategies{ Fixed, RoundRobin }`；`record` + `init` + 默认值 + DataAnnotations
- [x] C2 新增 `src/Qlx.BuildingBlocks/Ai/AiProviderOptions.cs`
  - `Type`(默认 `openai-compatible`) / `BaseUrl`(可空，厂商默认兜底) / `Model` / `ApiKey` / `Enabled`(true) / `Priority`(100) / `TimeoutSeconds`(null→全局)
  - 重写 `ToString()` 掩码 `ApiKey`（只输出是否已配置）
- [x] C3 新增 `src/Qlx.BuildingBlocks/Ai/IAiChatClientProvider.cs` + `IAiChatClientFactory.cs`
  - `IAiChatClientProvider`：`string Type { get; }`、`string? DefaultBaseUrl { get; }`、`IAiChatClient Create(string providerName, AiProviderOptions provider)`
  - `IAiChatClientFactory`：`IAiChatClient Create(string providerName)`
- [x] C4 新增 `src/Qlx.BuildingBlocks/Ai/Providers/OpenAiCompatibleChatClient.cs`
  - `POST {BaseUrl}/chat/completions`（URL 拼接规范化）；`Authorization: Bearer {ApiKey}`；请求 DTO 用 `[JsonPropertyName]`
  - `role` 枚举转小写字符串；`Json` 格式传 `response_format.type=json_object`；`request.Model ?? provider.Model`
  - 非 2xx 抛 `AiProviderException`（状态码 + 响应体截断 ≤500 字符）；2xx 解析 `choices[0].message.content`、`usage`、`finish_reason`
  - 缺少 `BaseUrl`（含默认兜底）/`Model`/`ApiKey` 时抛 `AiProviderException(AI_PROVIDER_NOT_CONFIGURED)`，不发起请求
  - `Stopwatch` 计时写入 `AiChatResponse.DurationMilliseconds`（Invoker 侧可再覆盖）
- [x] C5 新增 `src/Qlx.BuildingBlocks/Ai/Providers/OpenAiCompatibleChatClientProvider.cs`（`Type=openai-compatible`，`DefaultBaseUrl=null`）
- [x] C6 新增 6 个商业厂商 Provider（薄封装，产出 `OpenAiCompatibleChatClient`，各带默认 BaseUrl）
  - `ZhipuChatClientProvider.cs`（`zhipu`，`https://open.bigmodel.cn/api/paas/v4/`）
  - `DashScopeChatClientProvider.cs`（`dashscope`，`https://dashscope.aliyuncs.com/compatible-mode/v1/`）
  - `VolcengineArkChatClientProvider.cs`（`volcengine`，`https://ark.cn-beijing.volces.com/api/v3/`）
  - `SiliconFlowChatClientProvider.cs`（`siliconflow`，`https://api.siliconflow.cn/v1/`）
  - `DeepSeekChatClientProvider.cs`（`deepseek`，`https://api.deepseek.com/v1/`）
  - `QianfanChatClientProvider.cs`（`qianfan`，`https://qianfan.baidubce.com/v2/`）
- [x] C7 新增 `src/Qlx.BuildingBlocks/Ai/Providers/OllamaChatClient.cs` + `OllamaChatClientProvider.cs`
  - `POST {BaseUrl}/api/chat`，`stream=false`，`Json` 时 `format=json`，`options.temperature`/`options.num_predict`
  - 解析 `message.content`、`prompt_eval_count`/`eval_count`、`done_reason`；`DefaultBaseUrl = http://localhost:11434/`
- [x] C8 新增 `src/Qlx.BuildingBlocks/Ai/AiChatClientFactory.cs`
  - 按厂商名从 `IOptions<QlxAiOptions>.Value.Providers`（大小写不敏感）取配置，未命中抛 `AI_PROVIDER_NOT_CONFIGURED`
  - 按 `Type` 从 `IEnumerable<IAiChatClientProvider>` 匹配（大小写不敏感），未命中抛 `AI_PROVIDER_TYPE_UNSUPPORTED`
- [x] C9 新增 `src/Qlx.BuildingBlocks/Ai/QlxAiOptionsValidator.cs`（实现 `IValidateOptions<QlxAiOptions>`，构造注入 `IEnumerable<IAiChatClientProvider>`）
  - 全局：`Enabled=false` 时仅校验 `Providers` 键非空、`MaxLogContentLength>0`、`TimeoutSeconds>0`
  - 启用时：至少 1 个启用厂商；每个启用厂商 `Type` 已注册、`BaseUrl`（配置或 `DefaultBaseUrl`）可解析为绝对 URI、`Model` 非空、`Priority>=0`、`TimeoutSeconds`（厂商或全局）>0
  - `Strategy=Fixed` 时 `FixedProvider`（若填写）必须存在并启用；`Strategy` 仅允许 `Fixed` / `RoundRobin`
- [x] C10 `dotnet build` 通过

## 阶段 D：调用策略、故障转移、日志与装配

- [x] D1 新增 `src/Qlx.BuildingBlocks/Ai/AiInvoker.cs`
  - `Enabled=false` → `AiInvocationException(AI_DISABLED)`
  - 请求校验：`Messages` 为空 → `ArgumentException`（参数名 `request`）
  - 候选解析：`Fixed`（`FixedProvider` 或 Priority 最小启用厂商）；`RoundRobin`（启用厂商按 `Priority` 升序 + 厂商名 ordinal）
  - 逐厂商：`Stopwatch` 计时、`IAiChatClientFactory.Create(name)`、`ChatAsync(request, cancellationToken)`、`with { DurationMilliseconds }`
  - 有效结果判定：`Content` 非空白；`Json` 时 `JsonDocument.Parse` 成功（D5）
  - 任一成功 → 立即返回，不再调用后续厂商
  - 失败分类：`AiProviderException`（沿用其 `ErrorCode`/`HttpStatusCode`）、`TaskCanceledException`（外部令牌未取消 → `TIMEOUT`，已取消 → 原样上抛）、`HttpRequestException`（`NETWORK_ERROR`）、其余（`UNEXPECTED_ERROR`）
  - 全部失败 → 组装链路 `Message` 并抛 `AiInvocationException`；外部令牌取消时直接上抛 `OperationCanceledException`
- [x] D2 在 `AiInvoker` 中以 `[LoggerMessage]` 落地日志（类声明 `partial`）
  - 尝试日志：`correlationId`/`purpose`/`strategy`/`provider`/`type`/`model`/`attemptIndex`/`durationMs`/`outcome`/`httpStatus`/`errorCode`/`contentLength`
  - 成功 Info、失败 Warning、整体结束 Info 或 Error（尝试次数、总耗时、最终厂商/失败码）
  - 请求摘要：`model`/`temperature`/`maxTokens`/`responseFormat`/`messageCount`；`LogRequestContent=true` 时附正文并按 `MaxLogContentLength` 截断；任何分支不记录 `ApiKey`
- [x] D3 新增 `src/Qlx.BuildingBlocks/Ai/AiServiceCollectionExtensions.cs`
  - `AddOptions<QlxAiOptions>().BindConfiguration(SectionName).ValidateDataAnnotations().ValidateOnStart()`
  - 注册 `IValidateOptions<QlxAiOptions>` → `QlxAiOptionsValidator`
  - 为每个 `Enabled=true` 厂商注册命名 `HttpClient`：名称 `qlx-ai:{厂商名}`，`Timeout = TimeSpan.FromSeconds(provider.TimeoutSeconds ?? options.TimeoutSeconds)`
  - `AddSingleton<IAiChatClientProvider, ...>`：通用兼容 + 智谱 + 百炼 + 火山 + 硅基流动 + DeepSeek + 千帆 + Ollama（共 8 个）
  - `TryAddSingleton<IAiChatClientFactory, AiChatClientFactory>()`、`TryAddSingleton<IAiInvoker, AiInvoker>()`
- [x] D4 `dotnet build` 通过

## 阶段 E：Host 装配与单测

- [x] E1 `src/Qlx.Host/Program.cs` 增加 `using Qlx.BuildingBlocks.Ai;` 与 `.AddQlxAi(builder.Configuration)`（置于 `.AddQlxCaching(...)` 之后），更新文件头修改时间
- [x] E2 新增 `tests/Qlx.BuildingBlocks.Tests/Ai/` 测试替身
  - `RecordingAiChatClient`（记录调用次序/请求，可编程返回成功/异常/空内容/非法 JSON）
  - `FakeAiChatClientProvider`（可配置 `Type`、`DefaultBaseUrl` 与客户端工厂）
  - `StubHttpMessageHandler` + `IHttpClientFactory` 替身（用于适配器级单测）
  - `CapturingLogger<T>`（收集日志状态字段，供日志断言）
- [x] E3 新增 `tests/Qlx.BuildingBlocks.Tests/Ai/AiChatClientFactoryTests.cs`
  - 按名+`Type` 正确实例化；未知厂商名 → `AI_PROVIDER_NOT_CONFIGURED`；未知 `Type` → `AI_PROVIDER_TYPE_UNSUPPORTED`；大小写不敏感
- [x] E4 新增 `tests/Qlx.BuildingBlocks.Tests/Ai/AiProviderWiringTests.cs`
  - 8 个 `IAiChatClientProvider` 均可由 DI 解析，`Type` 唯一
  - 6 个商业 Provider 的 `DefaultBaseUrl` 与规格清单一致；`openai-compatible` 为 `null`
  - Ollama `DefaultBaseUrl = http://localhost:11434/`
- [x] E5 新增 `tests/Qlx.BuildingBlocks.Tests/Ai/OpenAiCompatibleChatClientTests.cs`
  - 请求体字段：`model`/`messages`/`max_tokens`/`response_format`；`Authorization` 头正确；URL 拼接去重斜杠
  - `request.Model` 覆盖配置 `Model`；两者缺失 → `AI_PROVIDER_NOT_CONFIGURED` 且不发请求
  - 非 2xx → `AiProviderException`（状态码 + 截断响应体）；2xx 解析 content/usage/finish_reason
- [x] E6 新增 `tests/Qlx.BuildingBlocks.Tests/Ai/QlxAiOptionsValidatorTests.cs`
  - 启用 + 空厂商字典 → 失败；启用 + 未知 `Type` → 失败；启用 + `BaseUrl` 缺失且无默认 → 失败；`Model` 空 → 失败
  - `Strategy` 非法 → 失败；`Fixed` 指向不存在/已停用厂商 → 失败；`Enabled=false` 时上述厂商级错误不阻断
- [x] E7 新增 `tests/Qlx.BuildingBlocks.Tests/Ai/AiInvokerTests.cs`
  - 固定模式成功（只调用 1 次）；固定模式失败不尝试其他厂商并抛异常
  - 轮询模式：第 1 个失败、第 2 个成功 → 返回第 2 个结果，调用次序 `[p1, p2]`，`Attempts` 两条
  - 全部失败 → `AiInvocationException`，`Attempts` 数量/错误码/耗时完整，`Message` 含全部厂商名
  - 401/403/429 继续下一厂商；空内容/非法 JSON → `INVALID_RESPONSE` 继续
  - 超时 → `TIMEOUT`；外部令牌取消 → 抛 `OperationCanceledException`（不包装）
  - `Enabled=false` → `AI_DISABLED` 且无调用
  - 日志断言：尝试日志含厂商/耗时/状态/错误码/`contentLength`；`LogRequestContent=false` 时无正文；日志不含 ApiKey
  - 扩展性：新增 fake `Type` 的 provider + 配置即可参与调用（核心类零改动）
- [x] E8 `dotnet build` 0 错误 0 警告
- [x] E9 `dotnet test tests/Qlx.BuildingBlocks.Tests/Qlx.BuildingBlocks.Tests.csproj` 全绿（含既有用例无回归）

## 阶段 F：收口

- [x] F1 全量 `dotnet test`（`Qlx.BuildingBlocks.Tests` + `Qlx.ArchitectureTests`）通过
- [x] F2 核对 `config/common/ai.json`（厂商字典）与 `QlxAiOptions`/`AiProviderOptions` 字段一一对应，无明文密钥
- [x] F3 按 [checklist.md](./checklist.md) 逐条核对并勾选

## Task Dependencies

- A1 ~ A3 与 B1 ~ B2 相互独立，可并行；B1 ~ B2 必须先于 C/D
- C1 ~ C2 依赖 B1（契约类型）
- C3 ~ C9 依赖 C1 ~ C2
- D1 ~ D3 依赖 C3 ~ C9
- E1 依赖 D3；E2 ~ E7 依赖 D1 ~ D3
- E8 ~ E9 依赖 E1 ~ E7
- F1 ~ F3 依赖 E8 ~ E9
