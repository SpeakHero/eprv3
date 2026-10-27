# 通用 AI 调用层 — 验收清单

> 关联规格：[spec.md](./spec.md) ｜ 任务：[tasks.md](./tasks.md)
> 勾选依据：代码核对 + `dotnet build`（0 警告）+ 单测断言 + 配置核对
> 执行说明：本层不新增端点，验收以单测与配置核对为主；真实厂商调用需外部密钥，不在本批验收范围内。

## 一、统一契约与分层

- [x] `src/Qlx.Abstractions/Ai/` 存在 `IAiChatClient`，唯一调用方法为 `Task<AiChatResponse> ChatAsync(AiChatRequest, CancellationToken)`（`CancellationToken` 必选、无默认值）
- [x] `IAiInvoker.InvokeAsync(AiChatRequest, CancellationToken)` 为业务唯一入口，业务无需引用任何厂商实现类型
- [x] `AiChatRequest`/`AiChatResponse`/`AiMessage`/`AiInvocationAttempt` 均为不可变 `record`，覆盖消息、模型、温度、maxTokens、响应格式、耗时、token 用量
- [x] `Qlx.Abstractions` 未引用 `Qlx.BuildingBlocks`；`Qlx.BuildingBlocks.Ai` 未引用任何 `Qlx.Modules.*`

## 二、工厂与厂商具体实现

- [x] `IAiChatClientProvider` 暴露 `Type`/`DefaultBaseUrl`/`Create(string name, AiProviderOptions)`；`IAiChatClientFactory` 按「厂商名 → Type → 实例」解析，业务层无 `new` 具体实现
- [x] 未知厂商名抛 `AI_PROVIDER_NOT_CONFIGURED`；未知 `Type` 抛 `AI_PROVIDER_TYPE_UNSUPPORTED`（单测覆盖，含大小写不敏感）
- [x] DI 中可解析 8 个 Provider 且 `Type` 唯一：`openai-compatible`/`zhipu`/`dashscope`/`volcengine`/`siliconflow`/`deepseek`/`qianfan`/`ollama`
- [x] 6 个商业 Provider 的默认 BaseUrl 与规格「厂商实现清单」一致；`openai-compatible` 无默认地址；Ollama 默认 `http://localhost:11434/`
- [x] 商业适配（`OpenAiCompatibleChatClient`）：`POST {BaseUrl}/chat/completions`、Bearer 鉴权、请求体含 `model`/`messages`/`max_tokens`/`response_format`，解析 `choices[0].message.content` 与 `usage`/`finish_reason`
- [x] 开源适配（`OllamaChatClient`）：`POST {BaseUrl}/api/chat`（`stream=false`），解析 `message.content`/`prompt_eval_count`/`eval_count`/`done_reason`
- [x] 实际模型取 `request.Model ?? 厂商 Model`；两者皆空 → `AI_PROVIDER_NOT_CONFIGURED` 且不发起请求
- [x] URL 拼接规范（BaseUrl 去尾斜杠 + 相对路径），配置 `BaseUrl` 可覆盖厂商默认
- [x] 适配器缺少 `BaseUrl`/`ApiKey` 时抛 `AiProviderException(AI_PROVIDER_NOT_CONFIGURED)`，不发起 HTTP 请求

## 三、调用策略与故障转移

- [x] `Qlx:Ai:Strategy` 支持 `Fixed` / `RoundRobin`（大小写不敏感）；非法值启动失败
- [x] 固定模式仅调用 `FixedProvider`（未填写时取启用厂商中 `Priority` 最小者）；失败后不再尝试其他厂商
- [x] 轮询模式按 `Priority` 升序（同优先级按厂商名 ordinal）依次调用，跳过 `Enabled=false`
- [x] 任一厂商成功并返回有效结果即立即返回，后续厂商未被调用（单测断言调用次序）
- [x] 有效结果判定 = HTTP 2xx + `Content` 非空白 +（`Json` 格式时）`JsonDocument.Parse` 成功
- [x] HTTP 401/403/429 视为该厂商失败并继续下一厂商；空内容/非法 JSON 记为 `INVALID_RESPONSE` 继续
- [x] 单厂商超时归类 `TIMEOUT`；网络异常归类 `NETWORK_ERROR`；未知异常归类 `UNEXPECTED_ERROR`
- [x] 外部 `CancellationToken` 取消时原样上抛 `OperationCanceledException`，不包装为业务异常
- [x] 单厂商内不自动重试（一次调用一次尝试）

## 四、标准化异常与链路信息

- [x] `AiInvocationException : BusinessException`，错误码取自 `AiErrorCodes`（`AI_DISABLED`/`AI_PROVIDER_NOT_CONFIGURED`/`AI_PROVIDER_TYPE_UNSUPPORTED`/`AI_INVOCATION_FAILED`）
- [x] 全部候选失败时抛 `AI_INVOCATION_FAILED`，`Message` 内联每个厂商名称与失败原因（链路摘要）
- [x] `Attempts` 逐条含 `Index`/`ProviderName`/`ProviderType`/`Model`/`DurationMilliseconds`/`Outcome`/`HttpStatusCode`/`ErrorCode`/`ErrorMessage`，`Details` 同步携带
- [x] `Enabled=false` 时调用立即抛 `AI_DISABLED`，且不产生任何 HTTP 请求
- [x] 异常消息与 `Attempts.ErrorMessage` 中的厂商响应片段截断 ≤500 字符，不含 `Authorization` 头或 API Key

## 五、调用日志

- [x] 使用 `ILogger<AiInvoker>` + `[LoggerMessage]` 源生成（`partial` 类），无拼接字符串裸调用
- [x] 每次厂商尝试一条日志，含 `correlationId`/`purpose`/`strategy`/`provider`/`type`/`model`/`attemptIndex`/`durationMs`/`outcome`/`httpStatus`/`errorCode`/`contentLength`
- [x] 整体结束一条汇总日志，含策略、尝试次数、总耗时、最终厂商或失败错误码
- [x] 请求参数摘要（模型/温度/maxTokens/响应格式/消息数）必记；`LogRequestContent=false` 时日志不含消息正文
- [x] `LogRequestContent=true` 时正文按 `MaxLogContentLength` 截断
- [x] 任何日志分支均不含 `ApiKey`；`AiProviderOptions.ToString()` 对 `ApiKey` 掩码

## 六、配置、启动校验与环境变量覆盖

- [x] `config/common/ai.json` 使用 `Qlx:Ai` 节，`Providers` 为厂商名字典，字段与 `QlxAiOptions`/`AiProviderOptions` 一一对应
- [x] 配置默认 `Enabled=false`，缺失密钥环境不会发起外呼、不阻断启动
- [x] `config/common/ai.json` 不含任何明文 API Key；注释说明经 `Qlx__Ai__Providers__<厂商名>__ApiKey` 注入（C4）
- [x] `AddQlxConfiguration` 在四级 JSON 之后追加 `AddEnvironmentVariables()`；单测证明 `Qlx__Ai__Strategy` 与 `Qlx__Ai__Providers__zhipu__ApiKey` 覆盖配置文件
- [x] `QlxAiOptionsValidator`（`IValidateOptions`，依赖已注册 Provider）覆盖：启用时至少 1 个启用厂商、`Type` 已注册、`BaseUrl` 可解析（含厂商默认）、`Model` 非空、`Priority>=0`、超时>0、`Strategy` 合法、`Fixed` 指向存在且启用的厂商
- [x] `Enabled=false` 时厂商级错误不阻断启动（仅校验结构项）
- [x] 配置变更（策略/厂商/超时）需重启生效，规格与注释均已说明

## 七、解耦与可扩展性

- [x] 新增厂商路径只需：新增 `IAiChatClientProvider` 实现 + `AddQlxAi` 注册一行 + 配置项；`AiInvoker`/`AiChatClientFactory`/`QlxAiOptions` 零修改
- [x] 单测用 fake `Type` 验证「仅新增实现类 + 配置」即可参与调用
- [x] 不新增 NuGet 包（HTTP 走 `IHttpClientFactory`，序列化走 `QlxJsonDefaults.Default`）

## 八、规范与构建

- [x] 所有新增 `.cs` 文件含标准文件头（公司/作者/版本/创建时间/修改时间/描述），修改文件已更新修改时间
- [x] Options 为 `record` + `init` 属性 + 默认值，经 `BindConfiguration` + `ValidateDataAnnotations` + `ValidateOnStart`
- [x] 未出现 `new JsonSerializerOptions(...)`（统一 `QlxJsonDefaults.Default`）
- [x] 未出现 `throw new Exception()`；异常均为自定义类型；`ArgumentException` 仅用于空消息列表这一编程错误
- [x] 未新增内部 `CancellationTokenSource`；公开接口无 `CancellationToken = default`
- [x] 适配器内部 DTO 全部 `[JsonPropertyName]` 显式声明线上字段（CamelCase 单例不会把 `max_tokens` 写错）
- [x] `dotnet build` 0 错误 0 警告（`TreatWarningsAsErrors`）
- [x] `dotnet test tests/Qlx.BuildingBlocks.Tests/Qlx.BuildingBlocks.Tests.csproj` 全绿，既有用例无回归
- [x] `dotnet test` 全量（含 `Qlx.ArchitectureTests`）通过
