# Tasks：可复用全局手动验证工具（ObjectValidator）

- [x] Task 1: 定义 `IAsyncValidatableObject` 接口
  - [x] SubTask 1.1: 新建 `src/Qlx.BuildingBlocks/Validation/IAsyncValidatableObject.cs`：`Task<IEnumerable<ValidationResult>> ValidateAsync(ValidationContext validationContext, CancellationToken cancellationToken = default)`，中文 XML 注释说明语义（返回失败集合、通过返回空、与 `IValidatableObject.Validate` 对应）
  - [x] SubTask 1.2: 文件头遵循 `global-csharp-standard.md` 约定（公司/作者/版本/时间/描述）
- [x] Task 2: 实现 `ObjectValidator` 静态工具类
  - [x] SubTask 2.1: 新建 `src/Qlx.BuildingBlocks/Validation/ObjectValidator.cs`，提供 4 个公开重载：`Validate(object?, IServiceProvider?, IDictionary<object, object?>?, bool)`、`Validate(ValidationContext)`、`ValidateAsync(object?, IServiceProvider?, IDictionary<object, object?>?, CancellationToken, bool)`、`ValidateAsync(ValidationContext, CancellationToken)`
  - [x] SubTask 2.2: 实现合并语义：`Validator.ValidateObject`（特性 + IValidatableObject）∪ `IAsyncValidatableObject.ValidateAsync`；结果物化为列表返回（对外类型 `IEnumerable<ValidationResult>`）
  - [x] SubTask 2.3: 边界与异常：null 实例返回空结果；同步重载遇 `IAsyncValidatableObject` 抛 `NotSupportedException`（消息指明改用异步）；`Validate`/`ValidateAsync` 内部异常原样传播；异步路径 `ConfigureAwait(false)`
  - [x] SubTask 2.4: 完整中文 XML 注释（含各重载语义差异、编写约定「双接口不得重复同一规则」）
  - [x] SubTask 2.5: 服务层抛异常式快捷方法：`ValidateOrThrow`（同步）/ `ValidateOrThrowAsync`（异步，含 CancellationToken）+ `IServiceProvider` 扩方法形式；复用结果式合并语义，失败时将全部 `ValidationResult` 映射为 `FieldError`（字段名经 `JsonNamingPolicy.CamelCase.ConvertName` 转驼峰，无成员名的对象级错误映射为空字段名）并一次性抛 `Qlx.Abstractions.Exceptions.ValidationException`，通过则静默返回
- [x] Task 3: 编写单元测试 `tests/Qlx.BuildingBlocks.Tests/Validation/ObjectValidatorTests.cs`
  - [x] SubTask 3.1: null 实例边界（同步/异步均返回空，不抛异常）
  - [x] SubTask 3.2: 普通对象（仅特性）：`[Required]`/`[StringLength]` 违规 → 失败项含 MemberNames 与消息；合规 → 空
  - [x] SubTask 3.3: 仅 `IValidatableObject`：`Validate` 结果被收集；`ServiceProvider`/`Items` 经 context 正确传入（`GetService` 解析桩服务断言）
  - [x] SubTask 3.4: 仅 `IAsyncValidatableObject`：异步重载调用 `ValidateAsync` 并收集结果（断言 CancellationToken 透传）；同步重载抛 `NotSupportedException`
  - [x] SubTask 3.5: 双接口对象：两侧结果合并、成员名与消息完整
  - [x] SubTask 3.6: 预构建 `ValidationContext` 重载（DisplayName 等透传）；`Validate` 抛异常 → 原样传播断言
  - [x] SubTask 3.7: `ValidateOrThrow` / `ValidateOrThrowAsync`（含 `IServiceProvider` 扩方法）：失败 → 抛 `ValidationException` 且 `FieldErrors` 完整、字段名驼峰（`NewPassword` → `newPassword`）、对象级错误字段名为空串；通过 → 不抛；异步透传 CancellationToken
- [x] Task 4: 构建与全量验证
  - [x] SubTask 4.1: `dotnet build src/Qlx.Host/Qlx.Host.csproj` 0 警告 0 错误
  - [x] SubTask 4.2: 构建 `tests/Qlx.BuildingBlocks.Tests` 并以 MTP 控制台运行 `ObjectValidatorTests` 全部通过；再跑全量测试程序集确认无回归

# Task Dependencies

- Task 2 依赖 Task 1（工具引用接口）
- Task 3 依赖 Task 2（被测对象）
- Task 4 依赖 Task 3（验证覆盖完整性）
- 全链路为单一模块内串行交付，无可并行项
