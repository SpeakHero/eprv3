# Checklist：可复用全局手动验证工具（ObjectValidator）

- [x] `IAsyncValidatableObject` 接口已定义于 `src/Qlx.BuildingBlocks/Validation/`，签名与语义符合 spec（ValidateAsync 返回失败集合，含 CancellationToken 默认参数）
- [x] `ObjectValidator` 提供同步与异步各两组重载（实例 + 服务/元数据参数；预构建 ValidationContext），返回统一 `IEnumerable<ValidationResult>`
- [x] 合并语义正确：`Validator.ValidateObject`（特性 + IValidatableObject）∪ `IAsyncValidatableObject.ValidateAsync`，成员名称与错误消息完整保留，与框架原生验证行为一致
- [x] 验证上下文传递正确：`ServiceProvider` 可被验证方法内 `GetService` 解析，`Items` / `DisplayName` 原样透传，`CancellationToken` 传入 `ValidateAsync`
- [x] 空对象边界：null 实例返回空结果集（同步/异步），不抛 `NullReferenceException`；null ValidationContext 抛 `ArgumentNullException`
- [x] 同步重载遇 `IAsyncValidatableObject` 实例抛 `NotSupportedException`，消息明确指引改用异步重载（无静默漏验）
- [x] 用户验证方法抛出的异常原样传播，工具不吞并不包装；异步路径使用 `ConfigureAwait(false)`
- [x] 服务层快捷方法可用：`ValidateOrThrow` / `ValidateOrThrowAsync`（含 `IServiceProvider` 扩方法）一次调用复用 DTO 全部验证，失败抛项目统一 `ValidationException`
- [x] `ValidationResult` → `FieldError` 映射正确：字段名取首成员名并转驼峰（`NewPassword` → `newPassword`），对象级错误字段名为空串，全部失败项一次性完整携带
- [x] 公开 API 具备完整中文 XML 注释（含重载语义差异与「双接口不得重复同一规则」编写约定），文件头符合项目规范
- [x] 单元测试覆盖：普通对象、仅单一接口、双接口、成功/失败、上下文/服务/元数据/取消令牌传递、异常传播、null 边界（同步/异步）、抛异常式快捷方法（失败/通过/驼峰映射）
- [x] `dotnet build src/Qlx.Host/Qlx.Host.csproj` 0 警告 0 错误
- [x] `ObjectValidatorTests` 全部通过；测试程序集全量运行无回归
