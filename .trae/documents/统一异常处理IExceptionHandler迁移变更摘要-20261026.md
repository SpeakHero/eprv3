# 统一异常处理迁移至 IExceptionHandler 变更摘要（2026-10-26）

## 一、背景与结论

原自研约定式中间件 `ExceptionHandlerMiddleware`（含手写 try/catch 管线、OCE 静默过滤、响应包装）迁移为 ASP.NET Core 官方 `IExceptionHandler` 模式（.NET 8+ 引入，官方文档：error-handling / error-handling-api）。

**迁移原则：仅替换注册机制与管线承载，响应契约字节级不变。**

| 对比维度 | 旧：自研中间件 | 新：IExceptionHandler |
|---|---|---|
| 响应已开始时抛异常 | 会二次抛 `InvalidOperationException` | 框架自动检测 `Response.HasStarted` 并优雅中止 |
| DiagnosticSource | 无 | 内建 HandledException / UnhandledException 事件（OTel 可直接消费） |
| 处理器 DI 生命周期 | 约定式中间件=单例 | 按请求作用域解析，未来处理器可注入 scoped 服务（如 ITenantContext） |
| 可扩展性 | 单一 catch-all | 多处理器链式注册，首个返回 true 生效 |
| 响应格式 | `ApiResponse` 信封 | 不变（未采用 RFC 9457 ProblemDetails，避免前端破坏性变更） |

## 二、变更文件清单

### 新增

| 文件 | 说明 |
|---|---|
| [QlxApiExceptionHandler.cs](../../src/Qlx.BuildingBlocks/Api/QlxApiExceptionHandler.cs) | `IExceptionHandler` 实现（sealed partial，主构造注入 `IHostEnvironment` + `ILogger<T>`）。原映射逻辑等价搬入 `Map()`：NotFoundException→404、ConflictException→409、ValidationException→422（含 FieldErrors）、UnauthorizedException→401、ForbiddenException→403、BadHttpRequestException/InvalidDataException→400（隐藏框架细节）、BusinessException 泛化→422、未知→500（生产隐藏详情/开发含原始消息）。客户端断开（OCE 且 `RequestAborted`）静默返回 true 不写响应。LoggerMessage 两法保留（500=Error 全栈 / 业务=Warning） |
| [QlxExceptionHandlingExtensions.cs](../../src/Qlx.BuildingBlocks/Api/QlxExceptionHandlingExtensions.cs) | `AddQlxExceptionHandling()`（DI 注册）+ `UseQlxExceptionHandling()`（管线启用，内部即框架 `UseExceptionHandler()`） |
| [QlxApiExceptionHandlerTests.cs](../../tests/Qlx.BuildingBlocks.Tests/Api/QlxApiExceptionHandlerTests.cs) | 专项测试 12 项（此前中间件零直接测试） |

### 删除

| 文件 | 说明 |
|---|---|
| [~~ExceptionHandlerMiddleware.cs~~](../../src/Qlx.BuildingBlocks/Api/) | 逻辑全部搬入新文件，整体删除（含 `UseExceptionHandlerMiddleware` 扩展） |

### 修改

| 文件 | 说明 |
|---|---|
| [Program.cs](../../src/Qlx.Host/Program.cs) | 服务链追加 `.AddQlxExceptionHandling()`（L83）；管线处改为 `app.UseQlxExceptionHandling()`（L225）。**位置不变**：仍处 ForwardedHeaders 之后、安全响应头/CORS 之前——集中式授权方案依赖此最外层位置统一封装 401/403 |
| [ApiResponse.cs](../../src/Qlx.BuildingBlocks/Api/ApiResponse.cs) | 仅文档注释：文件头描述与 `<see cref>` 由旧中间件类型改为 `QlxApiExceptionHandler` |
| [OperationAuditFilterTests.cs](../../tests/Qlx.BuildingBlocks.Tests/Audit/OperationAuditFilterTests.cs) | 宿主适配：`ConfigureServices` 加 `services.AddQlxExceptionHandling()`，`Configure` 换用 `UseQlxExceptionHandling()` |

## 三、关键技术点（.NET 10 实测偏差）

**框架启动校验**：.NET 10 的 `ExceptionHandlerMiddlewareImpl` 在宿主启动期校验管线配置，仅认可 `ExceptionHandlingPath` / `ExceptionHandler` 选项 / `IProblemDetailsService` 三者之一；`IExceptionHandler` 改为按请求作用域惰性解析，**不参与启动校验**。仅注册处理器而启用 `UseExceptionHandler()` 会导致所有宿主启动失败（报 "An error occurred when configuring the exception handler middleware..."）。

**处置**：`AddQlxExceptionHandling()` 按官方文档组合在 `AddExceptionHandler<T>()` 之外附带调用 `AddProblemDetails()`。因本处理器始终返回 true 全量接管，ProblemDetails 服务不会实际写响应，`ApiResponse` 契约不受影响。

**其他适配**：接口参数名须为 `httpContext`（CA1725）；探测控制器纯抛异常动作按 pragma 豁免 CA1822（MVC 不支持 static 动作方法）。

## 四、行为等价性验证

| 场景 | 旧行为 | 新行为 | 结果 |
|---|---|---|---|
| 各业务异常→状态码/信封 | 404/409/422(+errors)/401/403/400/422 | 同 | 一致 |
| 未知异常 500 生产隐藏详情 | "服务器内部错误" | 同 | 一致 |
| 未知异常 500 开发含详情 | "服务器内部错误：原始消息" | 同 | 一致 |
| 客户端断开（OCE + RequestAborted） | 静默不写响应 | 同 | 一致 |
| 内部取消（OCE 但非客户端断开） | 500 | 同 | 一致 |
| Content-Type | `application/json; charset=utf-8` | 同 | 一致 |
| 日志 | 500=Error 全栈 / 业务=Warning | 同 | 一致 |

## 五、新增测试明细（12 项）

集成（TestServer 真实管线，9 项）：
1. NotFoundExceptionMapsTo404WithEnvelope（含信封结构 + Content-Type）
2. ConflictExceptionMapsTo409WithCustomErrorCode
3. ValidationExceptionMapsTo422WithFieldErrors（字段级 errors 断言）
4. UnauthorizedExceptionMapsTo401
5. ForbiddenExceptionMapsTo403
6. GenericBusinessExceptionMapsTo422（BusinessException 泛化分支）
7. InvalidDataExceptionMapsTo400WithoutFrameworkDetail（不暴露框架细节）
8. UnknownExceptionMapsTo500AndHidesDetailInProduction
9. UnknownExceptionRevealsDetailInDevelopment

单元（DefaultHttpContext 直测处理器，3 项）：
10. TryHandleAsyncSwallowsAbortedRequestCancellation（断开静默，响应体为空）
11. InternalCancellationNotCausedByClientStillMapsTo500
12. TryHandleAsyncTakesOverUnknownExceptionFully（返回 true 全量接管）

## 六、验证结果

- `dotnet build` 全解决方案：0 警告 0 错误
- 全解决方案测试：**1402 通过 / 0 失败**（BuildingBlocks 1382 + Architecture 20）
- grep 确认源码无旧类型残留引用（仅剩 2 处为文档注释中对框架类型 `ExceptionHandlerMiddleware` 的合法引用）

## 七、遗留说明

- `docs/渗透测试问题整改记录-20261024.md` 与 `.trae/documents/集中式授权策略映射重构计划-20261024.md` 中的旧扩展名引用为历史记录，按惯例不回改
- 本次未采用 `ExceptionHandlerOptions.StatusCodeSelector`（类型映射内聚于处理器）；未迁移 RFC 9457 ProblemDetails 响应格式（如未来前端愿意同步重构，可作为独立演进项）

## 八、后续调整（2026-10-26）

**ContentType 去硬编码**（[QlxApiExceptionHandler.cs#L56](file:///e:/ERPv2/eprv3/src/Qlx.BuildingBlocks/Api/QlxApiExceptionHandler.cs#L56)）：

```csharp
// 调整前
httpContext.Response.ContentType = "application/json; charset=utf-8";
// 调整后
httpContext.Response.ContentType = $"{MediaTypeNames.Application.Json}; charset=utf-8";
```

- 说明：.NET 10 BCL 中**不存在**取值恰为 `"application/json; charset=utf-8"` 的公共系统常量——`MediaTypeNames.Application.Json` 仅为 `"application/json"`（无 charset）。故采用常量插值方案：媒体类型部分由 `System.Net.Mime.MediaTypeNames.Application.Json` 提供，charset 后缀保留字面量，编译期 const 折叠后与原字符串**字节级一致**
- 验证：`dotnet build` 0 警告 0 错误；12 项专项测试全绿（xunit v3 In-Process Runner 直跑），响应头日志确认仍为 `application/json; charset=utf-8`
- 附注：`dotnet test` 在当前环境（Microsoft.Testing.Platform 2.5.1 + SDK 11.0.100-rc.2）下触发 "Testing with VSTest target is no longer supported" 回归，与本次变更无关；可改用 xunit v3 runner 直跑或显式 `-p:TestingPlatformDotnetTestSupport=true` 绕过
