# 通用 AI 调用层（多厂商统一接入 / 固定与轮询策略 / 故障转移）Spec

> 关联规则：`.trae/rules/global-cache-layering-standard.md`（C-x/B-x/V-x/E-x）、`.trae/rules/global-csharp-standard.md`（6.5 业务异常 / 6.12 JSON 统一 / 6.13 CancellationToken / 7.6 配置边界、C2/C4）
> 关联调研：`docs/单位名称结构化拆解开源方案调研.md`（本层为该场景的通用底座，本次不实现具体业务解析逻辑）

## Why

项目需要接入多家 AI 厂商（主流商业云接口 + 本地开源模型），当前平台没有任何 AI 调用抽象。若业务代码直接绑定某家厂商的 HTTP 细节/密钥/模型名，会带来四个问题：切换或扩容厂商必须改业务代码；单厂商故障无兜底；线上排障缺少逐厂商的耗时与状态；密钥与调用策略散落各处。本 spec 建立一个与业务完全解耦的通用 AI 调用层，用配置驱动「固定 / 轮询」两种策略并实现自动故障转移，同时为调研过的每家厂商提供可直接启用的具体实现类。

## What Changes

- 新增统一契约（`src/Qlx.Abstractions/Ai/`）：`IAiChatClient`（厂商适配器唯一方法签名）、`IAiInvoker`（业务门面）、`AiChatRequest`/`AiChatResponse`/`AiMessage`/`AiInvocationAttempt`/`AiProviderException`、`AiErrorCodes`、`AiProviderTypes`。
- 新增标准化异常 `src/Qlx.Abstractions/Exceptions/AiInvocationException.cs`：继承 `BusinessException`，`Message` 内联完整调用链摘要，`Attempts`/`Details` 携带逐厂商明细。
- 新增实现（`src/Qlx.BuildingBlocks/Ai/`）：`QlxAiOptions` + `AiProviderOptions`（record + init + `ValidateOnStart` + `IValidateOptions` 启动校验）、`IAiChatClientProvider` + `IAiChatClientFactory`（按配置 `厂商名 → Type → 实例`）、`AiInvoker`（策略执行 + 故障转移 + 结构化日志）、`AddQlxAi` 装配扩展。
- 新增厂商具体实现类（`src/Qlx.BuildingBlocks/Ai/Providers/`）：
  - 协议内核 `OpenAiCompatibleChatClient`（`/chat/completions` + Bearer）。
  - 商业/云端具体实现：`ZhipuChatClientProvider`（智谱 GLM）、`DashScopeChatClientProvider`（阿里百炼）、`VolcengineArkChatClientProvider`（火山方舟）、`SiliconFlowChatClientProvider`（硅基流动）、`DeepSeekChatClientProvider`（DeepSeek）、`QianfanChatClientProvider`（百度千帆），另保留通用 `OpenAiCompatibleChatClientProvider`（任意兼容网关）。
  - 免费开源本地实现：`OllamaChatClientProvider`（`/api/chat`）。
- 新增 `config/common/ai.json`（配置节 `Qlx:Ai`：全局策略、启用开关、按**厂商名**组织的厂商字典；API Key 不落配置文件）。
- 修改 `src/Qlx.BuildingBlocks/Configuration/QlxConfigurationExtensions.cs`：在四级 JSON 源之后追加环境变量提供程序。现状 JSON 源最后加载会**反向覆盖**环境变量，导致 `Qlx__Ai__Strategy` 之类覆盖无效；本批修正后环境变量优先级最高（要求 9 的前提）。
- Host 装配：`src/Qlx.Host/Program.cs` 增加 `AddQlxAi(builder.Configuration)`。
- 新增单测（`tests/Qlx.BuildingBlocks.Tests/Ai/`）：工厂解析、各厂商默认地址/类型、固定模式、轮询回退、全失败异常链路、无效结果判定、401/403/429 分类、超时/取消、日志字段、环境变量覆盖、启动校验、新增厂商零改核心。
- 非破坏性：不新增 HTTP 端点、不改业务模块、不改数据库、不改前端、不新增 NuGet 包。

## Impact

- Affected specs：`.trae/rules/global-cache-layering-standard.md`（V-1 后端独立校验、E-2 单测守护）、`.trae/rules/global-csharp-standard.md`（6.5/6.12/6.13/7.6）、`docs/开发推进计划.md`（后续「单位名称解析」接入点）。
- Affected code：
  - `src/Qlx.Abstractions/Ai/`（新增契约）
  - `src/Qlx.Abstractions/Exceptions/AiInvocationException.cs`（新增异常）
  - `src/Qlx.BuildingBlocks/Ai/`（Options / 校验器 / 工厂 / Invoker / DI 扩展）
  - `src/Qlx.BuildingBlocks/Ai/Providers/`（协议内核 + 7 类厂商实现）
  - `src/Qlx.BuildingBlocks/Configuration/QlxConfigurationExtensions.cs`（追加环境变量源）
  - `src/Qlx.Host/Program.cs`（装配 `AddQlxAi`）
  - `config/common/ai.json`（新增配置节）
  - `tests/Qlx.BuildingBlocks.Tests/Ai/`、`tests/Qlx.BuildingBlocks.Tests/Configuration/`
- 数据库：无变更（调用日志走宿主 `ILogger` 管道，不新建表）。
- 前端：无变更。
- 构建约束：`Directory.Build.props` 已启用 `TreatWarningsAsErrors`（`AnalysisLevel=latest-recommended`），新增代码必须 0 警告。

## 厂商实现清单（Type → 默认 BaseUrl）

| 厂商 | `Type` | 默认 BaseUrl（配置 `BaseUrl` 可覆盖） | 模型来源 |
|------|--------|--------------------------------------|----------|
| 智谱 GLM | `zhipu` | `https://open.bigmodel.cn/api/paas/v4/` | 配置 `Model`（示例 `glm-4.7-flash`，官方免费档） |
| 阿里百炼 | `dashscope` | `https://dashscope.aliyuncs.com/compatible-mode/v1/` | 配置 `Model`（示例 `qwen-plus`） |
| 火山方舟 | `volcengine` | `https://ark.cn-beijing.volces.com/api/v3/` | 配置 `Model`（接入点 ID 或模型名，必填） |
| 硅基流动 | `siliconflow` | `https://api.siliconflow.cn/v1/` | 配置 `Model`（免费档以平台当期公告为准） |
| DeepSeek | `deepseek` | `https://api.deepseek.com/v1/` | 配置 `Model`（示例 `deepseek-chat`） |
| 百度千帆 | `qianfan` | `https://qianfan.baidubce.com/v2/` | 配置 `Model` |
| 通用兼容网关 | `openai-compatible` | 无（必须配置 `BaseUrl`） | 配置 `Model` |
| 本地开源 | `ollama` | `http://localhost:11434/` | 配置 `Model`（示例 `qwen2.5:7b`） |

> 说明：`zhipu`/`dashscope`/`siliconflow` 等云端「免费额度」模型与本地 Ollama 均属开源/可选开源权重模型；密钥与额度由各平台账号决定，本层只负责协议适配与策略调度。

## 关键设计决策（会签项）

| # | 决策 | 说明 |
|---|------|------|
| D1 | 分层归属 | 契约放 `Qlx.Abstractions.Ai`（业务模块只依赖契约）；实现放 `Qlx.BuildingBlocks.Ai`。不新建项目，不改任何模块工程。 |
| D2 | 双接口分离 | `IAiChatClient`（厂商适配器：只负责协议编解码与 HTTP 调用，签名 `Task<AiChatResponse> ChatAsync(AiChatRequest, CancellationToken)`）；`IAiInvoker`（业务门面：策略、回退、日志、异常）。策略逻辑不下沉到适配器。 |
| D3 | 两级工厂 + 厂商字典 | 配置 `Qlx:Ai:Providers` 为**以厂商名为键的对象字典**（非数组），密钥可用 `Qlx__Ai__Providers__zhipu__ApiKey` 稳定注入，不会因数组索引漂移串号。`IAiChatClientProvider` 暴露 `Type` 与 `Create(string name, AiProviderOptions)`；`IAiChatClientFactory` 按名取配置、按 `Type` 选实现。新增厂商 = 新增 Provider 类 + `AddQlxAi` 注册一行 + 配置项，核心类零改动。 |
| D4 | 策略取值 | 配置值 `Fixed` / `RoundRobin`（大小写不敏感）。`RoundRobin` 语义严格按需求 5：按 `Priority` 升序（同优先级按厂商名 ordinal）**逐个失败转移**，任一成功立即返回；不做多实例负载轮转。 |
| D5 | 有效结果判定 | HTTP 2xx 且 `Content` 非空白；`ResponseFormat=Json` 时还要求 `Content` 可被 `JsonDocument.Parse` 解析。任一不满足即视为该厂商失败并继续下一个。 |
| D6 | 异常契约 | `AiInvocationException : BusinessException`，错误码常量：`AI_DISABLED` / `AI_PROVIDER_NOT_CONFIGURED` / `AI_PROVIDER_TYPE_UNSUPPORTED` / `AI_INVOCATION_FAILED`。`Message` 内联链路摘要（如「AI 调用全部失败（策略 RoundRobin，尝试 2 个厂商）：zhipu(HTTP 429)；ollama(连接超时)」），`Attempts`/`Details` 提供结构化明细。401/403/429/5xx 均视为该厂商失败并继续；仅当全部候选失败才抛出。消息与响应片段截断（≤500 字符），不含鉴权头与密钥。 |
| D7 | 日志 | `ILogger<AiInvoker>` + `[LoggerMessage]` 源生成（对齐 `ExceptionHandlerMiddleware` 写法）。每次调用生成 `correlationId`；每条日志含 `correlationId` / `purpose` / `strategy` / `provider` / `type` / `model` / `attemptIndex` / `durationMs` / `outcome` / `httpStatus` / `errorCode`；成功附带 `contentLength`。请求参数摘要（模型、温度、maxTokens、响应格式、消息数）必记；**消息正文默认不记录**，仅在 `LogRequestContent=true` 时按 `MaxLogContentLength` 截断记录。API Key 任何情况下不得落日志。 |
| D8 | 配置与环境变量 | 配置节 `Qlx:Ai`；`AddQlxConfiguration` 末尾追加 `AddEnvironmentVariables()`，实现 `Qlx__Ai__Strategy` / `Qlx__Ai__Providers__zhipu__ApiKey` 覆盖（要求 9）。默认 `Enabled=false`（无密钥环境不误调用、不阻断启动）。`Enabled=true` 时由 `IValidateOptions<QlxAiOptions>`（可解析已注册 Provider）在启动期校验：每个启用厂商 `Type` 已注册、`BaseUrl` 可解析（配置或厂商默认）、`Model` 非空、超时>0、`Priority>=0`、`FixedProvider` 存在；无任何启用厂商直接启动失败。API Key 遵守 C4：只经环境变量/密钥引用注入，`config/*.json` 不存明文；`AiProviderOptions.ToString()` 掩码 Key。**配置变更需重启生效**（`IOptions` + 命名 HttpClient 启动期注册，本层不做热切换）。 |
| D9 | 超时与取消 | 每个启用厂商注册命名 `IHttpClientFactory` 客户端（名称 `qlx-ai:{厂商名}`），`Timeout` 取该厂商 `TimeoutSeconds`，缺省取全局 `TimeoutSeconds`；不在业务代码内 new `CancellationTokenSource`（6.13.4）。外部令牌取消时 `OperationCanceledException` 原样上抛，不包装为业务异常（6.13.5）。超时归类 `TIMEOUT`（仅当外部令牌未取消时）。单厂商不自动重试。 |
| D10 | 序列化与 URL | 请求/响应体统一使用 `QlxJsonDefaults.Default`，禁止 `new JsonSerializerOptions`（6.12）。该单例为 CamelCase，适配器内部 DTO 必须 `[JsonPropertyName]` 显式声明线上字段（`model`/`messages`/`max_tokens`/`response_format` 等），响应按原始字段名用 `JsonDocument` 读取。URL 拼接统一「BaseUrl 去尾斜杠 + `/` + 相对路径」，兼容各厂商默认地址与自建网关。 |
| D11 | 模型优先级与请求校验 | 实际模型 = `AiChatRequest.Model ?? 厂商配置 Model`；两者皆空 → `AI_PROVIDER_NOT_CONFIGURED`（不发起 HTTP）。`Messages` 为空属编程错误，抛 `ArgumentException`（非业务异常），避免把内部误用伪装成 422。 |
| D12 | 生命周期与错误映射 | `IAiInvoker` / `IAiChatClientFactory` 注册为无状态 Singleton，线程安全；适配器每次调用即时创建、不持有可变状态。AI 失败沿用既有 `BusinessException → 422` 中间件映射，本层不新增 HTTP 状态码映射（本层无端点）。 |

## 自查修正记录（本轮）

1. **环境变量键稳定性**：`Providers` 由数组改为「厂商名 → 配置」字典，密钥注入不再依赖数组下标（原设计 `Providers__0__ApiKey` 在增删厂商时会串号）。
2. **补齐厂商实现**：原设计仅有 1 个通用兼容适配器 + Ollama；本轮补齐调研过的智谱、阿里百炼、火山方舟、硅基流动、DeepSeek、百度千帆 6 个具体实现类（共享协议内核）。
3. **模型优先级缺口**：明确 `request.Model ?? provider.Model`，并在缺失时快速失败。
4. **启动校验增强**：新增 `IValidateOptions<QlxAiOptions>`（依赖已注册 Provider），验证 `Type` 已注册、`BaseUrl` 可解析（含厂商默认）、启用厂商数 > 0。
5. **热更新预期管理**：明确策略/厂商/超时变更需重启，避免误以为配置文件改动即时生效。
6. **敏感信息外泄面**：明确异常消息与日志的响应片段截断上限、密钥永不记录、`ToString()` 掩码。
7. **重试语义**：明确 429/5xx 仅作为失败转移，不在单厂商内自动重试。
8. **日志完整性**：成功日志补 `contentLength`，整体汇总补总耗时与最终厂商/失败码。
9. **请求校验定位**：空消息列表按编程错误抛 `ArgumentException`，与业务校验异常（422）区分。
10. **错误码映射**：明确沿用既有中间件 422 映射，本批不改全局异常映射。

## ADDED Requirements

### Requirement: 统一 AI 调用契约

系统 SHALL 提供与厂商无关的调用契约，所有适配器实现必须遵循相同方法签名与输入输出约定。

#### Scenario: 业务仅依赖门面
- **WHEN** 业务代码构造函数注入 `IAiInvoker` 并提交 `AiChatRequest`
- **THEN** 业务无需引用任何厂商类型、地址、模型名或密钥，即可得到 `AiChatResponse`（含 `Content`、`ProviderName`、`Model`、耗时与 token 用量）

#### Scenario: 适配器签名一致
- **WHEN** 新增一个 `IAiChatClient` 实现
- **THEN** 必须实现 `ProviderName` 属性与唯一的 `Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken cancellationToken)` 方法，禁止新增第二套调用入口

### Requirement: 工厂化动态创建

系统 SHALL 依据配置参数动态实例化对应厂商客户端，业务层不出现具体实现类的 `new` 或类型判断。

#### Scenario: 按 Type 解析适配器
- **WHEN** 配置 `Type` 为 `zhipu` / `dashscope` / `volcengine` / `siliconflow` / `deepseek` / `qianfan` / `openai-compatible` / `ollama` 之一
- **THEN** `IAiChatClientFactory` 从已注册 `IAiChatClientProvider` 集合中按 `Type` 匹配并创建实例

#### Scenario: 配置非法
- **WHEN** `Type` 未注册或厂商名不在 `Providers` 字典中
- **THEN** 抛出 `AiInvocationException`，错误码分别为 `AI_PROVIDER_TYPE_UNSUPPORTED` / `AI_PROVIDER_NOT_CONFIGURED`，消息指出具体 `Type` 或厂商名

### Requirement: 商业与开源两类适配实现（含具体厂商类）

系统 SHALL 提供两类适配，并为调研过的厂商提供可直接启用的具体实现类。

#### Scenario: 商业兼容接口
- **WHEN** 厂商配置引用 `zhipu` / `dashscope` / `volcengine` / `siliconflow` / `deepseek` / `qianfan` / `openai-compatible`
- **THEN** 适配器向 `{BaseUrl}/chat/completions` 发起 `POST`，携带 `Authorization: Bearer {ApiKey}`，请求体含 `model`、`messages`、可选 `temperature`/`max_tokens`/`response_format`；解析 `choices[0].message.content` 与 `usage` 用量

#### Scenario: 厂商默认地址
- **WHEN** 厂商配置省略 `BaseUrl`
- **THEN** 使用该 `Type` 对应的默认地址（见「厂商实现清单」）；`openai-compatible` 无默认地址，省略即启动校验失败

#### Scenario: 地址覆盖
- **WHEN** 厂商配置显式填写 `BaseUrl`（私有网关/国际站点）
- **THEN** 以配置值为准，覆盖厂商默认地址

#### Scenario: 本地开源接口
- **WHEN** 厂商 `Type=ollama`
- **THEN** 适配器向 `{BaseUrl}/api/chat` 发起 `POST`（`stream=false`，`Json` 格式时传 `format=json`），解析 `message.content` 与 `prompt_eval_count`/`eval_count`

### Requirement: 全局调用策略（固定 / 轮询）

系统 SHALL 通过 `Qlx:Ai:Strategy` 全局切换调用模式，无需改动代码。

#### Scenario: 固定模式
- **WHEN** `Strategy=Fixed` 且 `FixedProvider=zhipu`
- **THEN** 只调用 `zhipu`；若该厂商失败，不尝试其他厂商，直接抛出标准化异常

#### Scenario: 固定模式未指定厂商
- **WHEN** `Strategy=Fixed` 且 `FixedProvider` 为空
- **THEN** 取 `Enabled=true` 中 `Priority` 最小的厂商作为唯一目标

#### Scenario: 轮询模式
- **WHEN** `Strategy=RoundRobin` 且 `Providers` 中有多个 `Enabled=true`
- **THEN** 按 `Priority` 升序（同优先级按厂商名）依次调用，跳过 `Enabled=false` 的厂商

### Requirement: 故障自动转移

系统 SHALL 在轮询模式下逐个尝试，任一厂商返回有效结果即终止后续轮询并返回。

#### Scenario: 中途成功
- **WHEN** 第 1 个厂商超时、第 2 个厂商返回 2xx 且内容非空
- **THEN** 立即返回第 2 个厂商结果，`AiInvocationAttempt` 链中包含 2 条记录（1 失败 1 成功），不再调用第 3 个

#### Scenario: 无权限/限流继续尝试
- **WHEN** 某厂商返回 HTTP 401/403/429
- **THEN** 记录失败（`errorCode=HTTP_401/HTTP_403/HTTP_429`）并继续下一个厂商，不立即抛出，也不在单厂商内重试

#### Scenario: 无效结果视为失败
- **WHEN** 厂商返回 2xx 但 `Content` 为空或（要求 JSON 时）不是合法 JSON
- **THEN** 记录 `INVALID_RESPONSE` 失败并继续下一个厂商

### Requirement: 标准化业务异常与完整链路

系统 SHALL 在最后一个候选厂商仍失败时抛出继承 `BusinessException` 的标准化异常，携带完整调用链错误信息。

#### Scenario: 全部失败
- **WHEN** 所有候选厂商均失败（含 401/403/429、5xx、超时、网络错误、无效响应、密钥缺失）
- **THEN** 抛出 `AiInvocationException`，`ErrorCode=AI_INVOCATION_FAILED`，`Message` 包含每个厂商名称与失败原因，`Attempts` 逐条包含 `Index`/`ProviderName`/`ProviderType`/`Model`/`DurationMilliseconds`/`HttpStatusCode`/`ErrorCode`/`ErrorMessage`

#### Scenario: 未启用
- **WHEN** `Qlx:Ai:Enabled=false` 时业务调用 `IAiInvoker`
- **THEN** 立即抛出 `AiInvocationException`，`ErrorCode=AI_DISABLED`，消息提示启用配置项，且不产生任何 HTTP 请求

#### Scenario: 敏感信息保护
- **WHEN** 厂商返回错误响应体
- **THEN** 异常消息与 `Attempts.ErrorMessage` 中的响应片段截断至 ≤500 字符，且不含 `Authorization` 头或 API Key

### Requirement: 完整调用日志

系统 SHALL 为每次调用记录逐厂商日志，覆盖耗时、请求参数摘要与响应状态，便于问题排查。

#### Scenario: 成功与失败日志
- **WHEN** 一次调用经过 N 个厂商
- **THEN** 每次尝试产生一条日志（含 `correlationId`、厂商、类型、模型、耗时毫秒、HTTP 状态、结果、错误码，成功时附 `contentLength`），整体结束产生一条汇总日志（含策略、尝试次数、总耗时、最终厂商或失败错误码）

#### Scenario: 正文与密钥保护
- **WHEN** `LogRequestContent=false`（默认）
- **THEN** 日志只含请求参数摘要（模型/温度/maxTokens/响应格式/消息数），不含消息正文；即使开启也只按 `MaxLogContentLength` 截断记录；API Key 永不记录

### Requirement: 与业务解耦与可扩展

系统 SHALL 保证新增厂商只新增实现类并更新配置，核心调用逻辑零改动。

#### Scenario: 新增厂商
- **WHEN** 新增一个实现 `IAiChatClientProvider` 的类并注册到 DI，同时在 `Qlx:Ai:Providers` 增加一项
- **THEN** `AiInvoker`、`AiChatClientFactory`、`QlxAiOptions` 代码均无需修改；配置中引用该 `Type` 即可参与固定/轮询调用

#### Scenario: 架构边界
- **WHEN** 运行架构/单元测试
- **THEN** `Qlx.Abstractions` 不引用 `Qlx.BuildingBlocks`；`Qlx.BuildingBlocks.Ai` 不引用任何业务模块

### Requirement: 环境变量覆盖

系统 SHALL 允许所有 AI 配置项经环境变量覆盖，且优先级高于 `config/*.json`。

#### Scenario: 切换策略
- **WHEN** 进程环境变量设置 `Qlx__Ai__Strategy=RoundRobin`（配置文件为 `Fixed`）
- **THEN** 运行时策略为 `RoundRobin`

#### Scenario: 注入密钥
- **WHEN** 设置 `Qlx__Ai__Providers__zhipu__ApiKey=<key>`（`config/common/ai.json` 中不含该字段）
- **THEN** 该厂商以注入密钥调用，配置文件中不出现明文密钥

## 范围边界（本批明确不做）

- 不新增 HTTP 端点/Controller、前端页面或权限点：本层只对业务服务提供 `IAiInvoker`，接入单位名称解析等场景由后续批次落地。
- 不新建数据库表持久化调用日志，不接缓存（无「读不到就查库」路径）；日志经宿主 `ILogger` 管道输出，由部署侧日志采集汇聚。
- 不做多实例负载轮转、熔断、按厂商限流、流式（SSE）响应；不新增 Polly / 官方 SDK 等 NuGet 包。
- 不自动重试单个厂商（一次调用即一次尝试）；是否重试由上层业务决定。
- 不做配置热切换（策略/厂商/超时变更重启生效）；不改全局异常 → HTTP 状态码映射。
