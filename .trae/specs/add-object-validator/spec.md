# Spec：可复用全局验证工具（ObjectValidator：结果式 + 服务层抛异常式，支持 IValidatableObject / IAsyncValidatableObject）

> 日期：2026-10-26　|　状态：待审批
> 范围：仅在 `Qlx.BuildingBlocks` 新增验证工具与接口 + `tests/Qlx.BuildingBlocks.Tests` 新增单测；纯增量，**无破坏性变更**，不强制迁移既有调用点。

## Why

MVC 模型验证只在 HTTP 管线内自动触发；服务层、后台任务、种子数据等场景需要手动验证实现了 `IValidatableObject` 的对象（如上一轮重构中 `ResetPortalUserPasswordRequest` 的密码策略校验）时，目前只能手写 `Validator.ValidateObject` 样板代码，且 BCL 不提供 `IValidatableObject` 的异步对应物——含 I/O 型校验（查库、调远程）的对象无法参与统一验证管线。需要一个全局统一的手动验证入口：既要能收集结果（`IEnumerable<ValidationResult>`），也要让服务层**一次调用即复用 DTO 全部验证规则**、失败直接抛项目统一的 `ValidationException`（422 FieldErrors），消除服务层重复的转换与校验样板。

## What Changes

- **ADDED** 新接口 `Qlx.BuildingBlocks.Validation.IAsyncValidatableObject`：`Task<IEnumerable<ValidationResult>> ValidateAsync(ValidationContext validationContext, CancellationToken cancellationToken = default)`（BCL 缺失的异步对应物，仓库中当前不存在，见已核实结论）。
- **ADDED** 新静态工具类 `Qlx.BuildingBlocks.Validation.ObjectValidator`：
  - 同步重载 `Validate(object? instance, IServiceProvider? serviceProvider = null, IDictionary<object, object?>? items = null, bool validateAllProperties = true)`
  - 异步重载 `ValidateAsync(object? instance, IServiceProvider? serviceProvider = null, IDictionary<object, object?>? items = null, CancellationToken cancellationToken = default, bool validateAllProperties = true)`
  - 上下文重载 `Validate(ValidationContext)` / `ValidateAsync(ValidationContext, CancellationToken)`：调用方可自建 `ValidationContext` 传入自定义验证服务（`ServiceProvider`）与元数据（`Items`）
  - 验证结果合并语义：`Validator.ValidateObject`（特性校验 + `IValidatableObject.Validate`，框架原生行为）∪ `IAsyncValidatableObject.ValidateAsync`（如实现），统一合并为 `IEnumerable<ValidationResult>` 返回
  - 边界与异常：`instance` 为 null → 返回空结果集（不抛 NRE）；同步重载遇 `IAsyncValidatableObject` 实例 → 抛 `NotSupportedException` 并在消息中指明改用异步重载（避免静默漏验）；`Validate`/`ValidateAsync` 内部抛出的异常原样传播（不吞并、不包装，与框架一致）；异步路径 `ConfigureAwait(false)`
  - **服务层抛异常式快捷方法（一次调用复用 DTO 全部验证）**：`ValidateOrThrow(...)` / `ValidateOrThrowAsync(...)`（含 `IServiceProvider` 扩展重载，服务层可一行调用）；失败时将全部 `ValidationResult` 映射为 `FieldError` 抛出项目统一的 `Qlx.Abstractions.Exceptions.ValidationException`——字段名取首个成员名并经 `JsonNamingPolicy.CamelCase.ConvertName` 转驼峰（与既有服务层字段命名约定一致，如 `newPassword`）；无成员名的对象级错误映射为空字段名（与 MVC `ValidationProblemDetails` 空键行为一致）；通过则正常返回不抛
- **ADDED** 单元测试 `tests/Qlx.BuildingBlocks.Tests/Validation/ObjectValidatorTests.cs`，覆盖普通对象（仅特性）、仅单一接口、双接口、成功/失败、上下文传递、异常传播、null 边界等场景。
- 不改动既有 DTO / 服务 / 控制器；既有 `IValidatableObject` 调用点（如 `ResetPortalUserPasswordRequest` 的 MVC 管线验证）保持原状，后续可按需采纳本工具。

## Impact

- 受影响 specs：无既有 spec 覆盖验证基础设施；本 spec 为新增能力（`build-phase3-*` 等系列不依赖此工具）。
- 受影响代码：
  - 新增 `src/Qlx.BuildingBlocks/Validation/IAsyncValidatableObject.cs`
  - 新增 `src/Qlx.BuildingBlocks/Validation/ObjectValidator.cs`
  - 新增 `tests/Qlx.BuildingBlocks.Tests/Validation/ObjectValidatorTests.cs`
  - 复用既有：`System.ComponentModel.DataAnnotations`（ValidationContext / ValidationResult / Validator）

## ADDED Requirements

### Requirement: 异步可验证对象接口
系统 SHALL 提供 `IAsyncValidatableObject` 接口，作为 BCL `IValidatableObject` 的异步对应物，方法签名为 `Task<IEnumerable<ValidationResult>> ValidateAsync(ValidationContext validationContext, CancellationToken cancellationToken = default)`，语义约定与 `IValidatableObject.Validate` 一致（返回失败集合，通过则返回空集合）。

#### Scenario: 对象实现异步接口
- **WHEN** 某对象实现 `IAsyncValidatableObject` 并在 `ValidateAsync` 中返回包含失败项的集合
- **THEN** 该对象可被 `ObjectValidator.ValidateAsync` 验证并收集到这些失败项

### Requirement: 同步验证重载
`ObjectValidator.Validate` SHALL 对非空实例执行框架原生验证（`Validator.ValidateObject(instance, context, validateAllProperties)`，覆盖特性校验与 `IValidatableObject.Validate`），返回 `IEnumerable<ValidationResult>`。

#### Scenario: 普通对象（仅数据注解特性）验证失败
- **WHEN** 对象仅含数据注解特性（如 `[Required]` / `[StringLength]`）且属性值不满足
- **THEN** 返回的 `ValidationResult` 包含对应 `MemberNames` 与错误消息，与框架原生验证行为一致

#### Scenario: 仅实现 IValidatableObject 的对象
- **WHEN** 对象实现 `IValidatableObject` 且其 `Validate` 返回失败项
- **THEN** 失败项被收集并返回，且传入的 `ValidationContext` 携带调用方指定的 `ServiceProvider` 与 `Items`（`GetService` 可解析自定义验证服务）

#### Scenario: 同步重载遇到异步可验证对象
- **WHEN** 实例实现 `IAsyncValidatableObject` 且调用同步 `Validate`
- **THEN** 抛出 `NotSupportedException`，异常消息明确指示该对象须用 `ValidateAsync` 验证（不允许静默漏验）

### Requirement: 异步验证重载
`ObjectValidator.ValidateAsync` SHALL 合并以下全部验证结果并返回 `IEnumerable<ValidationResult>`：① `Validator.ValidateObject`（特性 + `IValidatableObject.Validate`，如实现）；② `IAsyncValidatableObject.ValidateAsync`（如实现，透传 `CancellationToken`）。

#### Scenario: 同时实现两个接口的对象
- **WHEN** 对象同时实现 `IValidatableObject` 与 `IAsyncValidatableObject`，两方法各返回不同失败项
- **THEN** 两侧结果全部合并返回，成员名称与错误消息完整保留（编写约定：不得在两方法间重复同一规则，本工具只合并不去重）

#### Scenario: 异步验证成功
- **WHEN** 各验证环节均无失败项
- **THEN** 返回空集合，不产生错误

### Requirement: 验证上下文传递
工具 SHALL 支持两种上下文来源：① 调用方传入 `serviceProvider`（自定义验证服务）与 `items`（元数据），由工具构建 `ValidationContext`；② 调用方直接传入已构建的 `ValidationContext`（`DisplayName` 等自定义内容原样透传）。

#### Scenario: 自定义服务在验证方法内可用
- **WHEN** 调用 `ValidateAsync(instance, serviceProvider: 桩容器)` 且对象在 `ValidateAsync` 内调用 `validationContext.GetService<T>()`
- **THEN** 解析得到桩容器注册的服务实例

### Requirement: 空对象边界处理
工具 SHALL 对 `instance == null` 直接返回空验证结果（同步返回空集合，异步返回已完成的空集合任务），不抛 `NullReferenceException`；上下文重载对 `validationContext == null` 抛 `ArgumentNullException`（守卫明确）。

#### Scenario: 传入 null 实例
- **WHEN** 调用 `Validate(null)` 或 `ValidateAsync(null)`
- **THEN** 返回空结果集，流程不中断

### Requirement: 异常与可用性规范
工具 SHALL 遵循 .NET 最佳实践：公开 API 带完整中文 XML 注释（含语义约定与异常说明）；用户验证方法内抛出的异常原样向上传播；异步路径使用 `ConfigureAwait(false)`；不吞并、不重复包装异常；文件头遵循项目 `global-csharp-standard.md` 约定。

#### Scenario: 用户验证方法抛出异常
- **WHEN** `IValidatableObject.Validate` 或 `IAsyncValidatableObject.ValidateAsync` 内部抛出异常
- **THEN** 异常原样传播给调用方，工具不捕获、不包装

### Requirement: 服务层快捷校验（抛异常式，一次调用复用 DTO 验证）
工具 SHALL 提供 `ValidateOrThrow` / `ValidateOrThrowAsync` 快捷方法：内部复用结果式验证的合并语义，任一失败即收集**全部**失败项并一次性抛出 `Qlx.Abstractions.Exceptions.ValidationException`（`FieldErrors` 完整覆盖所有失败项），校验通过则静默返回；SHALL 同时提供 `IServiceProvider` 扩方法形式（`services.ValidateOrThrow(instance)` / `await services.ValidateOrThrowAsync(instance, ct)`），供持有 DI 容器的服务层一行调用。异常仅在失败时抛出，不改变成功路径的返回行为。

#### Scenario: 服务层一次调用复用 DTO 验证并失败抛异常
- **WHEN** 服务层调用 `ValidateOrThrow(request)`（或 `services.ValidateOrThrow(request)`），且 `request` 的特性校验与 `IValidatableObject.Validate` 均存在失败项
- **THEN** 抛出 `ValidationException`，`FieldErrors` 包含全部失败项；字段名为成员名驼峰形式（如 `NewPassword` → `newPassword`），对象级错误字段名为空串

#### Scenario: 校验通过不抛异常
- **WHEN** 调用 `ValidateOrThrow` / `ValidateOrThrowAsync` 且所有验证环节通过
- **THEN** 正常返回（不抛异常），不产生副作用

#### Scenario: 异步快捷方法透传取消令牌
- **WHEN** 调用 `services.ValidateOrThrowAsync(request, cancellationToken)` 且对象实现 `IAsyncValidatableObject`
- **THEN** `ValidateAsync` 收到同一 `CancellationToken`；失败时同样抛出 `ValidationException`

### Requirement: 单元测试覆盖
测试 SHALL 覆盖：null 实例（同步/异步）、普通对象特性失败与通过、仅 `IValidatableObject`、仅 `IAsyncValidatableObject`（含同步重载抛 `NotSupportedException`）、双接口合并、`ServiceProvider`/`Items`/预构建 `ValidationContext` 传递、`CancellationToken` 透传、验证方法异常传播、`ValidateOrThrow`/`ValidateOrThrowAsync` 失败抛 `ValidationException`（含驼峰字段名与对象级空字段名映射）与通过不抛。测试放置于 `tests/Qlx.BuildingBlocks.Tests/Validation/ObjectValidatorTests.cs`，遵循既有 xunit v3 测试风格。

#### Scenario: 全量测试通过
- **WHEN** 构建解决方案并运行测试程序集
- **THEN** 0 警告 0 错误，新增用例全部通过且既有测试无回归
