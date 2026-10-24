---
alwaysApply: false
description: 编写c#代码时候，后端开发
---
# 规则名称：C# 全局编码规范

| 项目 | 内容 |
|-----|------|
| 适用范围 | 本项目所有 C# 代码文件 |
| 生效文件 | `**/*.cs` |
| 优先级 | 项目级强制 |
| 语言版本 | C# 14 / .NET 10 / EF Core 10（目标框架 `net10.0`） |
| 架构形态 | 自研模块化单体（`Qlx.Abstractions` + `Qlx.BuildingBlocks` + Host + 各模块） |
| 生效说明 | 所有 C# 代码生成、重构、修复操作必须严格遵守本规则；本文件为**唯一** C# 规范来源 |

## 修订记录

| 版本 | 日期 | 修订内容 |
|-----|------|---------|
| 1.0.0 | 2026-10-15 | 初版 |
| 2.0.0 | 2026-10-20 | 全量重构：章节重排；修复编号断层与禁止清单错位；清理 Orchard / `ERP.Infrastructure` 等废弃引用，对齐 `Qlx.*` 命名与已锁定架构；新增「五、类型设计规范（record / class / struct 选型）」专节与对应禁止条目 |
| 3.0.0 | 2026-10-20 | 架构铁律补齐：(1) 修正 §7.3 #6 + 禁止清单 #18 过时 Finbuckle 引用 → 自研 TenantDbContext 全局过滤；(2) 新增 §6.12 全局 JSON 序列化统一（QlxJsonDefaults.Default / PreserveNull，禁止自行 new）；(3) 新增 §7.6 配置体系边界（C2 强类型 Options / C3 四级目录 / C4 密钥只存引用 / C5 业务参数走数据库 / C6 模块隔离）；(4) 新增 §8.10 实体基类强制继承（Entity / AuditableEntity / FullAuditableEntity / TenantXxx 决策树）；(5) 新增 §8.11 DbContext 继承铁律（业务模块必须 TenantDbContext，Portal / Audit 豁免并注明原因）；(6) 新增 §8.12 表名/列名命名约定；(7) 新增 §8.13 AuditSaveChangesInterceptor 自动填充规则（雪花 Id / 审计字段 / 软删改写 / 审计失败不阻断）；(8) 新增 §8.14 ID 使用边界（进程内真实 long，跨边界 Hashids 混淆）；(9) 禁止清单从 38 条扩展至 51 条，新增实体基类、DbContext 继承、拦截器、序列化、配置、ID 边界等 13 条强制禁止 |
| 3.1.0 | 2026-10-20 | 新增 §7.7 全局常量集中管理（Data Protection Purpose、跨模块共享常量统一入口，禁止各模块硬编码）；禁止清单新增 #52 |
| 3.2.0 | 2026-10-21 | 新增 §6.13 CancellationToken 传播规范（公开 API 禁止 `= default` / 仅私有方法允许 / EF 异步必须透传 / 禁 `CancellationToken.None` 硬编码 / 禁内部新建 CTS / 禁吞 `OperationCanceledException`，含两个显式豁免点）；§6.10.3 同步对齐；禁止清单新增 #53~#58 |
| 3.3.0 | 2026-10-22 | 新增 §8.15 禁止 N+1 查询与按需查询（Include/ThenInclude 预加载 + 投影 + 批量聚合三条正解；禁止清单新增 #59~#60） |
| 3.4.0 | 2026-10-22 | 新增 §8.15.6 改写关联表 / 中间表反模式条目（先查→RemoveRange→循环 Add），按 Entity / AuditableEntity / FullAuditableEntity 三类基类给出三条正解；禁止清单新增 #61；BasTenantRepository 6 个改写方法已落地 ExecuteDeleteAsync + AddRangeAsync |
| 3.5.0 | 2026-10-22 | §8.14 新增「落地机制」：JSON 元数据约定（`QlxIdJsonMetadata`）+ 全局 ModelBinder（`HashidsLongModelBinderProvider`）双通道，统一注册入口 `AddQlxJsonSerialization` / `AddQlxIdModelBinding`；禁止全局按类型注册 `HashidsJsonConverter` 与逐属性标注；明确顶层数组等未覆盖边界 |

## 目录

- [零、文件头摘要（强制）](#零文件头摘要强制)
- [一、命名规范（强制）](#一命名规范强制)
- [二、注释规范（强制）](#二注释规范强制)
- [三、面向对象设计原则（强制）](#三面向对象设计原则强制)
- [四、DRY 原则（消除重复，强制）](#四dry-原则消除重复强制)
- [五、类型设计规范（record / class / struct 选型）（强制）](#五类型设计规范record--class--struct-选型强制)
- [六、C# 基础强制规则](#六c-基础强制规则)
  - [6.12 全局 JSON 序列化统一（强制）](#612-全局-json-序列化统一强制)
  - [6.13 CancellationToken 传播规范（强制）](#613-cancellationtoken-传播规范强制)
- [七、分层架构强制规则](#七分架构强制规则)
  - [7.6 配置体系边界（强制）](#76-配置体系边界强制)
  - [7.7 全局常量集中管理（强制）](#77-全局常量集中管理强制)
- [八、EF Core 专项强制规则](#八ef-core-专项强制规则)
  - [8.10 实体基类强制继承（铁律）](#810-实体基类强制继承铁律)
  - [8.11 DbContext 继承铁律](#811-dbcontext-继承铁律)
  - [8.12 表名 / 列名命名约定](#812-表名--列名命名约定)
  - [8.13 AuditSaveChangesInterceptor 规则](#813-auditsavechangesinterceptor-规则)
  - [8.14 ID 使用边界（雪花 long + Hashids 混淆）](#814-id-使用边界雪花-long--hashids-混淆)
  - [8.15 禁止 N+1 查询与按需查询（强制）](#815-禁止-n1-查询与按需查询强制)
- [九、强制禁止清单（违反即视为不合规）](#九强制禁止清单违反即视为不合规)

---

## 零、文件头摘要（强制）

**所有 .cs 文件开头必须包含以下摘要注释块**，格式如下：

```csharp
/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-15 10:00:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-15 10:00:00 +08:00 中国标准时间 星期一
 * 描述：[该文件的职责概述，一句话]
 */
```

**修改时间规则**：每次文件有实质变更（非格式化），修改时间字段必须更新为当前日期时间，版本号按需递增。

**例外**：由工具自动生成的代码（如 `*.Designer.cs`、`*.g.cs`、Migration 设计器文件）不适用本规则。

---

## 一、命名规范（强制）

### 1.1 通用命名规则

| 标识符类型 | 规则 | 示例 |
|-----------|------|------|
| 类 / 结构体 / record / 接口 | PascalCase | `PurchaseService`、`Money`、`IApprovalRouter` |
| 接口 | 以 `I` 开头 | `IApprovalCallback`、`INotificationService` |
| 枚举类型 | PascalCase | `ApprovalStatus`、`OrgType` |
| 枚举值 | PascalCase | `Approved`、`Department` |
| 方法 | PascalCase | `StartApprovalAsync()`、`GetLowStockProducts()` |
| 事件 | PascalCase | `PurchaseReceived`、`SalesDelivered` |
| 属性 | PascalCase | `CustomerName`、`CurrentStatus` |
| 静态字段 | s_camelCase | `s_defaultTimeout` |
| 实例字段 | _camelCase | `_approvalService` |
| 常量 | PascalCase | `MaxRetryCount` |
| 本地变量 / 参数 | camelCase | `purchaseOrder`、`tenantId` |
| 异步方法 | Async 后缀 | `CreatePurchaseOrderAsync()` |
| 布尔变量 | 肯定式命名 | `isApproved`、`hasPermission`（禁止 `notApproved`、`withoutPermission`） |
| Options 类型 | 以 `Options` 结尾 | `SnowflakeOptions`、`CacheOptions` |
| 事件契约 | 过去式 + `Event` 结尾 | `PurchaseReceivedEvent`、`ApprovalCompletedEvent` |

### 1.2 命名禁止清单

| 禁止 | 原因 |
|-----|------|
| 缩写不明（`Mgr`、`Std`、`Val`） | 降低可读性，除非团队内约定俗成 |
| 匈牙利命名法（`strName`、`intAge`） | C# 是强类型，IDE 已能识别类型 |
| 下划线开头的公开成员 | 违反 .NET 命名规范 |
| 单一字母变量（`a`、`b`、`x`） | 循环变量 `i`、`j` 除外 |
| 重复后缀（`PurchaseOrderInfo`、`CustomerData`） | 类名本身已表明语义，不需要 `Info` / `Data` 后缀 |
| DTO / record 使用 `Dto` / `Model` 后缀 | 语义由命名空间与位置表达（`Contracts` / `Events`），不靠后缀 |

### 1.3 业务命名约定

| 约定 | 示例 |
|-----|------|
| 单据状态枚举以 `Status` 结尾 | `PurchaseOrderStatus` |
| 业务事件以过去式命名 | `PurchaseCreated`、`ApprovalCompleted` |
| Service 接口以 `Service` 结尾 | `IApprovalService` |
| Factory 以 `Factory` 结尾 | `ApprovalRouterFactory` |
| 自定义异常以 `Exception` 结尾 | `ApprovalFlowException` |
| 模块清单以 `ModuleManifest` 表达 | `PurchaseModule.Manifest` |

---

## 二、注释规范（强制）

### 2.1 XML 文档注释

**所有 public 类型、方法、属性、接口（含 record 的位置参数与属性）必须添加 XML 文档注释**：

```csharp
/// <summary>
/// 启动审批流程
/// </summary>
/// <param name="documentType">单据类型（PurchaseRequisition / SalesOrder / ...）</param>
/// <param name="documentId">单据唯一标识</param>
/// <param name="applicantId">申请人 UserId</param>
/// <returns>审批流程实例 ID</returns>
/// <exception cref="ArgumentNullException">任一参数为空时抛出</exception>
public async Task<string> StartApprovalAsync(string documentType, string documentId, string applicantId)
```

**record 的文档注释要点**：位置参数用 `<param>` 描述，`init` 属性用 `<summary>` 描述；单个属性即可自解释时，允许在类型 `<summary>` 内以列表集中说明，不必逐个重复。

### 2.2 注释禁止清单

| 禁止 | 原因 |
|-----|------|
| 注释解释代码**做了什么** | 代码本身应该自解释，注释解释**为什么** |
| 保留废弃代码（`// var oldValue = ...`） | 用 Git 追溯历史，不要注释保留 |
| 注释掉的 `using` 或 `#region` | 直接删除，Git 有记录 |
| 重复代码含义的注释 | `i++ // i 加 1` |
| 用注释说明类型该用 record 还是 class | 选型规则见第五节，代码里不写选型辩解 |

### 2.3 必须写注释的场景

- 复杂业务规则（审批条件计算、库存预警阈值逻辑）
- 非直观的技术实现（反射、表达式树、并发控制）
- 临时方案 / Hack（注明原因和后续改进计划）
- 常量 / 枚举值的业务含义

### 2.4 单行注释风格

```csharp
// 业务规则：金额 > 10 万需集团总经理审批，此处用于 If/Else 条件判断
bool approvalRequired = totalAmount > 100000;
```

---

## 三、面向对象设计原则（强制）

### 3.1 接口抽象（依赖倒置原则）

- **高层模块依赖接口，不依赖具体实现**
- 业务模块 Service 必须先定义接口（`IPurchaseService`），再写实现（`PurchaseService`）
- 禁止在业务代码里直接 `new` 具体实现类，全部通过 DI 注入

```csharp
// ✅ 正确：Controller 依赖接口 + 主构造函数注入
public class PurchaseController(IPurchaseService purchaseService) : ControllerBase
{
    [HttpGet("orders/{id}")]
    public Task<IActionResult> GetOrderAsync(string id, CancellationToken cancellationToken)
        => purchaseService.GetOrderAsync(id, cancellationToken);
}

// ❌ 错误：Controller 直接依赖实现
public class PurchaseController : ControllerBase
{
    private readonly PurchaseService _purchaseService;   // 禁止
}
```

### 3.2 工厂模式（多态场景的动态选择）

- **有多种实现且运行时动态选择的场景，使用工厂**
- 典型场景：审批路由（不同单据类型走不同审批链）、单据编号生成（不同公司规则不同）

```csharp
// 工厂接口
public interface IApprovalRouterFactory
{
    IApprovalRouter Create(string documentType);
}

// 工厂实现（分发表为真常量，允许 switch 表达式）
public class ApprovalRouterFactory : IApprovalRouterFactory
{
    public IApprovalRouter Create(string documentType) => documentType switch
    {
        "PurchaseRequisition" => new PurchaseApprovalRouter(),
        "SalesOrder"          => new SalesApprovalRouter(),
        _                     => throw new BusinessException($"未知单据类型：{documentType}")
    };
}
```

**说明**：工厂内部的 `switch` 只做"类型分派到实现"这一件事，业务行为仍由多态承载（见 3.3），因此不违反"禁止 switch 硬编码多类型分发"。

### 3.3 多态（避免 switch/if 硬编码业务分支）

- **同一业务行为有多种实现时，用多态分发，不用 if/else 或 switch**
- 新增一种类型时，只加一个新类，不改现有代码（开闭原则）

```csharp
// ✅ 正确：多态
public interface IApprovalRouter
{
    Task<IReadOnlyList<ApprovalStep>> BuildApprovalChainAsync(string applicantDepartmentId, decimal amount, CancellationToken cancellationToken);
}

public class PurchaseApprovalRouter : IApprovalRouter { /* ... */ }
public class SalesApprovalRouter : IApprovalRouter { /* ... */ }

// ❌ 错误：switch 硬编码业务分支，每加一种单据都要改这里
public async Task<IReadOnlyList<ApprovalStep>> BuildChainAsync(string type, string departmentId, decimal amount)
{
    return type switch
    {
        "PurchaseRequisition" => /* 采购审批链 */,
        "SalesOrder"          => /* 销售审批链 */,
        // 每加一种单据都要改这里
    };
}
```

**判别联合替代开关**：当"多种形态的数据节点"本身需要区分处理（如条件表达式节点、规则节点）时，用 `abstract record` + 子 `record` 承载形态，由多态方法处理，禁止用"枚举 + switch"组合。

### 3.4 单一职责原则

- 一个类只做一件事，一个 Service 只负责一个业务领域
- Service 方法数建议不超过 20 个，超过考虑拆分
- 方法行数建议不超过 50 行，超过考虑提取子方法

### 3.5 模块解耦（事件驱动）

- 业务模块之间**禁止直接调用**，通过事件总线解耦
- 模块 A 发布事件 → 模块 B 订阅事件 → 自动联动
- 事件契约（record）定义在 `Qlx.Abstractions`，订阅关系在模块 Manifest 中声明

```csharp
// 采购管理：入库后发布事件（不直接调商品管理）
await _eventBus.PublishAsync(new PurchaseReceivedEvent { PurchaseOrderId = id, Quantities = items }, cancellationToken);

// 商品管理：实现 IEventHandler<T> 订阅，订阅关系在模块 Manifest 声明
public sealed class PurchaseReceivedHandler(IInventoryService inventoryService)
    : IEventHandler<PurchaseReceivedEvent>
{
    public Task HandleAsync(PurchaseReceivedEvent @event, CancellationToken cancellationToken)
        => inventoryService.UpdateInventoryAsync(@event.Quantities, cancellationToken);
}
```

订阅事件的方法签名、以及"事件必须先落本模块 Outbox 再投递"的可靠性要求，见架构规格 2.3.3 / 2.3.5。

---

## 四、DRY 原则（消除重复，强制）

### 4.1 识别重复代码

**以下情况必须抽离为公共工具或公共服务**：

| 重复场景 | 处理方式 |
|---------|---------|
| 3 个以上业务模块有相同的编码生成逻辑（如 `CG-2026-10-0001`） | 抽离为 `Qlx.BuildingBlocks` 的 `IDocumentNumberGenerator` |
| 3 个以上业务模块有相同的审批启动逻辑 | 抽离为公共 `IApprovalService.StartApprovalAsync` |
| 3 个以上业务模块有相同的分页查询模式 | 抽离为 `PagedQueryHelper` + `PagedResult<T>` / `PagedRequest` |
| 3 个以上业务模块有相同的实体状态校验（如"草稿状态才能提交审批"） | 抽离为 `EntityStateValidator` 公共校验器 |
| 多个 Controller 有相同的响应封装逻辑 | 抽离为 `ApiResponse<T>`（record）+ ActionFilter |
| 3 个以上模块需要缓存 | 注入 `Qlx.BuildingBlocks` 的 `ICacheService`，禁止各自封装 Redis |

### 4.2 公共能力放置位置

| 类型 | 放置位置 | 示例 |
|-----|---------|------|
| 跨所有模块的通用工具 / 基础设施 | 共享内核 `Qlx.BuildingBlocks` | `SnowflakeIdGenerator`、`IDocumentNumberGenerator`、`ICacheService`、`PagedQueryHelper`、`DateTimeHelper` |
| 跨模块契约：模块对外服务接口 + 事件契约 DTO | `Qlx.Abstractions` | `IIntegrationEvent`、`IModule`、`IGoodsQueryService`、`ApiResponse<T>` |
| 业务相关但跨模块共用（接口在 `Qlx.Abstractions`，实现在属主模块） | 属主业务模块 | `IApprovalService`、`INotificationService` |
| 配置分级加载 / Data Protection / 日志等宿主装配 | Host 装配层 | `AddLayeredConfiguration`、`AddQlxDataProtection` |

### 4.3 重复代码排查机制

- 新增业务代码前，**必须先检索** `Qlx.BuildingBlocks` / `Qlx.Abstractions` 是否已有可用能力
- 代码 Review 时重点检查重复逻辑
- 定期（每 Phase 完成后）用工具扫描重复代码（如 SonarQube、JetBrains Duplicates Finder）

### 4.4 禁止的重复实现

| 禁止 | 正确做法 |
|-----|---------|
| 每个业务模块自己写一套单据编号生成 | 注入公共 `IDocumentNumberGenerator` |
| 每个业务模块自己发通知（`_notifications.NotifyAsync(...)`） | 注入公共 `INotificationService.SendAsync(...)` |
| 每个业务模块自己查组织树祖先 | 注入公共 `IOrgService.GetAncestorsAsync(...)` |
| 每个业务模块自己做行级权限判断 | 统一用 `IAuthorizationService` + `RowLevelAuthHandler` |
| 每个业务模块自己封装 Redis / 缓存实现 | 注入公共 `ICacheService`，提供方由配置整体替换 |
| 每个业务模块自己生成主键（GUID / 自增） | 注入公共雪花 ID 生成器（`ISnowflakeIdGenerator`） |
| 每个业务模块自己加解密对外 ID | 由统一 ID 混淆组件在收发边界自动处理（见架构规格 2.3.8） |

---

## 五、类型设计规范（record / class / struct 选型）（强制）

**原则：以语义选型，不以手感选型。** `record` 表达"值语义 + 不可变数据"，`class` 表达"身份 + 可变状态 + 行为"，`struct` 表达"小尺寸值语义"。选型错误会同时污染比较语义与线程安全，因此本条为强制项。

### 5.1 选型决策树

```
这个类型主要承载什么？
├── 跨边界传输的不可变数据（DTO / 事件契约 / Options / 查询结果 / 响应包装）
│   └── record（引用类型；全部属性 init）
│        └── 字段 ≤3、总尺寸 ≤16 字节、无继承、创建极其频繁 → readonly record struct
├── 有身份且需要跟踪变更（EF 实体 / 聚合根）
│   └── class
├── 由 DI 容器创建并持有依赖（Service / Repository / Handler / Controller / JobHandler）
│   └── class（主构造函数注入）
├── 需要引用相等语义，或生命周期内可变累积状态
│   └── class
└── 小尺寸纯值语义（编码 / 金额 / 区间 / 坐标）
    └── record 或 readonly record struct
```

### 5.2 record 强制使用场景（本项目必须）

| 场景 | 所在位置 | 要求 |
|------|---------|------|
| 集成事件契约（模块间事件） | `Qlx.Abstractions/Events` | 必须 record；抽象基类用 `abstract record IntegrationEvent`；事件是历史事实，禁止可变 |
| 模块对外服务返回模型 / 跨模块查询 DTO | `Qlx.Abstractions` | 必须 record |
| API 请求体 / 响应体 DTO | 模块 Controller / Minimal API | 必须 record |
| 统一响应包装 `ApiResponse<T>`、分页 `PagedRequest` / `PagedResult<T>` | 共享契约 | 必须 record |
| 强类型 Options（`XxxOptions`） | 各模块 / `Qlx.BuildingBlocks` | 必须 record，且**用 `{ get; init; }` 属性写法**（见 5.4） |
| 模块清单 `ModuleManifest`、权限点描述、菜单/路由声明 | `Qlx.Abstractions` | 必须 record |
| 值对象（金额 / 数量 / 期间 / 编码 / 地址） | Domain 层 | 必须 record 或 `readonly record struct`；修改用 `with` |
| 领域事件 / Outbox 消息 / 缓存条目 / 幂等键 | BuildingBlocks / 各模块 | 必须 record |
| 判别联合数据形态（条件表达式节点、规则节点） | 模块内 | `abstract record` + 子 record，多态处理 |

### 5.3 record struct 使用场景与限制

```csharp
// ✅ 正确：小尺寸、无继承、值语义
public readonly record struct Percentage(decimal Value);
public readonly record struct DateRange(DateOnly Start, DateOnly End);

// ❌ 错误：可变值类型——在集合/装箱复制后状态丢失
public record struct Counter { public int Value; }
```

- 默认写 `readonly record struct`，**禁止可变字段**（见禁止清单第 36 条）
- 尺寸 > 16 字节或字段 > 3 → 改用 `record`（引用类型），避免拷贝开销
- `record struct` 不可用于需要引用相等、需要继承、需要 `null` 语义的场景

### 5.4 声明风格：位置参数 vs `init` 属性

| 写法 | 适用场景 | 说明 |
|------|---------|------|
| 位置参数 `public record Money(decimal Amount, string Currency = "CNY");` | 字段少（≤4）、字段全必填、简单 DTO / 值对象 | 自动生成构造、解构、`with`、值相等；可空参数必须显式标注（`string? Remark`） |
| `init` 属性 `public record CacheOptions { public int TtlSeconds { get; init; } = 300; }` | 字段多、需要默认值、需要 `required`、**配置 Options** | 生成无参构造，避免配置缺项失配 |

**强制**：

1. **配置 Options 一律用 `init` 属性写法**并为每项给出合理默认值，配合 `BindConfiguration` + `ValidateOnStart`；**禁止**用位置参数 record 承载 Options。
2. 同一 record 内**禁止混用**位置参数与额外 `init` 属性形成双构造路径；需要扩展时统一改为 `init` 属性写法。
3. record 中**禁止出现可变属性 `{ get; set; }`**（破坏值语义与不可变性，见禁止清单第 31 条）；构造后需要派生新值一律用 `with`。
4. 位置参数名按属性规则用 PascalCase；参数即属性，不写 `_camelCase`。

### 5.5 record 特性使用规范

- **`with` 表达式**：record 唯一允许的"修改"方式，且只用于生成新实例，禁止用于可变对象。

  ```csharp
  PurchaseOrder approved = draft with { Status = PurchaseOrderStatus.Approved };
  ```

- **解构**：仅在调用方确实按位置取值时使用，不为"顺手"暴露解构。
- **相等性**：record 按属性逐项比较（值相等）。因此禁止把 record 用于需要引用相等的场景（见 5.7）。
- **自动 `ToString()`**：会输出全部属性值。**密钥、令牌、连接串、密码引用等敏感值禁止作为 record 属性**（见禁止清单第 34 条）；Options 类型如需打印日志，重写 `ToString()` 并掩码敏感项。
- **模式匹配**：record 与属性模式天然契合，优先用属性模式而非逐属性判断。

  ```csharp
  if (@event is StockLowEvent { Quantity: < 10, WarehouseCode: not null })
      /* 低库存告警 */ ;
  ```

### 5.6 record 与序列化、映射、EF Core、配置的配合

| 技术 | 规则 |
|------|------|
| **System.Text.Json** | 位置参数 record 走构造函数反序列化、`init` 属性走属性赋值，二者均受支持；但 **STJ 不强制校验 `required` 成员与可空注解**，外部输入边界仍须显式校验（FluentValidation 等） |
| **Mapperly**（本项目唯一映射器） | record ↔ record、record ↔ 实体映射由 Mapperly 编译期源生成；目标为位置参数 record 时直接调用构造函数；**禁止**运行时反射式映射（AutoMapper 类库已禁用）；映射配置集中在模块内 `Mappings` 目录 |
| **EF Core** | **禁止**将 record 用作 EF 实体 / 聚合根（值相等与实体身份语义冲突，变更跟踪不可靠）；值对象可用 `ComplexProperty` 映射 `record`，但必须不可变 + 通过 `with` 生成新实例 |
| **配置绑定** | Options 用 `init` 属性 record（见 5.4 第 1 条）；密钥只存引用，真实值经 Data Protection 注入 |
| **ID 混淆边界** | 对外 DTO（record）中的业务 ID 字段为混淆字符串，框架在收发边界自动加解密；进程内事件契约与模块间查询接口一律真实 `long`（架构规格 2.3.8） |

### 5.7 禁止使用 record 的场景

1. EF Core 实体、聚合根（身份语义 + 可变跟踪）→ `class`
2. DI 服务实现类（Service / Repository / Handler / Controller / JobHandler）→ `class`（record 的值相等对服务无意义，`with` 复制会误造实例）
3. 需要引用相等（`ReferenceEquals` 语义、按引用做键）的对象 → `class`
4. 生命周期内可变累积状态的对象（状态机、累加器、Builder）→ `class`
5. 尺寸 > 16 字节或字段 > 3 的 `record struct` → 改用 `record`
6. 承载敏感值（密钥 / 令牌 / 连接串）→ 不放入 record

### 5.8 选型速查表

| 需求 | 选型 |
|------|------|
| 跨边界传输的数据载体（DTO / 事件契约 / 响应包装） | `record`（默认 `init` 属性或位置参数） |
| 配置 Options | `record` + `init` 属性 + 默认值 |
| 值对象（金额 / 区间 / 编码 / 地址） | `record` 或 `readonly record struct` |
| EF 实体 / 聚合根 | `class` |
| Service / Repository / Handler / Controller | `class`（主构造函数注入） |
| 尺寸 ≤16 字节的高频纯值语义 | `readonly record struct` |
| 需要可变状态 / 引用相等 | `class` |

---

## 六、C# 基础强制规则

### 6.1 可空引用类型

必须启用可空引用类型，所有可能为 `null` 的参数、返回值、属性必须显式标注可空类型。

### 6.2 文件范围命名空间

必须使用文件范围命名空间，禁止多层嵌套命名空间。

### 6.3 构造函数注入

必须使用构造函数注入依赖（优先主构造函数），禁止使用服务定位器模式；禁止业务代码 `new` 服务实现类。

### 6.4 XML 文档注释

所有 public 类型、方法、属性、接口必须添加 XML 文档注释（见第二节）。

### 6.5 业务异常

业务异常必须抛出自定义业务异常类（如 `BusinessException`、`ApprovalFlowException`），禁止直接 `throw new Exception()`。

### 6.6 字符串拼接

字符串拼接优先使用字符串插值，复杂循环拼接必须使用 `StringBuilder`。

### 6.7 异步方法

所有异步方法必须以 `Async` 结尾，返回 `Task` 或 `ValueTask`，禁止 `async void`。

### 6.8 nameof 运算符

所有引用参数名、属性名、类型名的字符串场景，必须使用 `nameof`，禁止硬编码字符串字面量。

### 6.9 现代 C# 语法（强制使用）

**原则：能用现代语法就别用旧写法。目标是更短、更清晰、更少重复。** 编译目标为 `net10.0`，下列语法全部可用。

#### 6.9.1 目标类型 new 表达式（target-typed new）

类型已从左侧声明推断出来时，右侧 `new` 不再重复写类型名。

```csharp
// ✅ 正确：目标类型 new
List<string> names = new();
Dictionary<string, int> map = new();
var order = new PurchaseOrder { Amount = 100 };   // 属性初始化器 + 目标类型推断

// ❌ 错误：重复书写类型名
List<string> names = new List<string>();
Dictionary<string, int> map = new Dictionary<string, int>();
```

#### 6.9.2 集合表达式（collection expressions）

统一用 `[...]` 初始化数组、`List`、`Span` 等集合，替换 `new[] { }` / `new List<T> { }`。

```csharp
// ✅ 正确：集合表达式
int[] numbers = [1, 2, 3];
List<string> codes = [];
ReadOnlySpan<char> span = ['a', 'b', 'c'];
int[] combined = [.. numbers, 4, 5];   // 展开运算符

// ❌ 错误：旧式集合初始化
int[] numbers = new int[] { 1, 2, 3 };
List<string> codes = new List<string>();
```

#### 6.9.3 主构造函数（primary constructor）

依赖注入的参数直接写在类名后的主构造函数里，省去字段声明 + 构造函数赋值样板代码。

```csharp
// ✅ 正确：主构造函数注入
public class PurchaseService(IRepository<PurchaseOrder> repository, IDocumentNumberGenerator numberGenerator)
    : IPurchaseService
{
    public Task CreateAsync(PurchaseOrder order, CancellationToken cancellationToken)
        => repository.CreateAsync(order, cancellationToken);
}

// ❌ 错误：传统字段 + 构造函数样板
public class PurchaseService : IPurchaseService
{
    private readonly IRepository<PurchaseOrder> _repository;
    public PurchaseService(IRepository<PurchaseOrder> repository)
        => _repository = repository;
}
```

**注意**：主构造函数参数必须被使用（供方法引用），否则编译器报警告。若参数需在构造函数体内做复杂校验 / 转换，改用普通构造函数（可读性优先）。

#### 6.9.4 nameof 运算符（强制）

引用参数名 / 属性名 / 类型名 / 成员名的字符串场景，一律用 `nameof`，禁止硬编码字面量。

```csharp
// ✅ 正确
throw new ArgumentNullException(nameof(codeType));
throw new ArgumentException("不能为空", nameof(codeType));
_logger.LogInformation("更新 {Property}", nameof(order.Status));

// ❌ 错误：硬编码字符串，重命名后会悄悄失效
throw new ArgumentNullException("codeType");
throw new ArgumentException("不能为空", "codeType");
```

#### 6.9.5 其他现代语法（按场景优先使用）

| 场景 | 推荐写法 | 替代旧写法 |
|-----|---------|-----------|
| 条件赋值 / 多分支 | `switch` 表达式 | 多行 `if/else` 或 `switch` 语句 |
| 类型判断 + 取值 | `is` 模式匹配（`is Order o`） | `as` + 空判断，或 `typeof(x) == typeof(T)` |
| 属性判断 | 属性模式（`if (o is { Status: Approved })`） | 多层嵌套 `if` |
| 局部常量字符串 | 原始字符串字面量 `"""..."""`（含引号 / 换行时） | 转义 `\"` 拼接 |
| 释放资源 | `using` 声明（作用域结束自动释放） | `using (...) { }` 块 |
| 命名空间 | 文件范围命名空间（见 6.2） | 嵌套命名空间 |
| 结构化 / 不可变数据 | `record` / `record struct`（选型见第五节） | 手写值对象样板 |
| 不可变属性 | `init` / `required` 修饰符 | 只读字段 + 构造赋值 |
| 引用非空判断 | `??=`、`?.`、`??` | 显式 `if` 判空 |

```csharp
// switch 表达式
var label = status switch
{
    OrderStatus.Draft    => "草稿",
    OrderStatus.Approved => "已审批",
    _                    => "未知"
};

// 属性模式
if (order is { Amount: > 100000, Status: OrderStatus.Draft })
    /* 大额草稿订单 */ ;

// 类型判断用 is 模式，禁止 typeof 比较
if (context is PurchaseDbContext purchaseDbContext)
    /* 采购买模块上下文 */ ;

// record 值对象（选型依据见第五节）
public record Money(decimal Amount, string Currency = "CNY");

// required + init
public class Supplier
{
    public required string Code { get; init; }
    public string? Remark { get; init; }
}
```

#### 6.9.6 禁止为"炫技"而用

现代语法是为了**降低样板、提升可读性**，不是炫技。以下情况保持传统写法更清晰：

- 主构造函数参数需在构造函数体内做复杂校验 / 转换时
- 集合表达式展开让逻辑难懂时
- 一行塞多个模式匹配导致难以阅读时

**可读性 > 语法新潮**，遇到取舍以团队可维护性为准。

### 6.10 分析器强制规范（IDE / CA 零提示）

**原则：`dotnet build` 与 IDE 分析器（含 `IDE` 前缀的 Roslyn 代码风格规则、`CA` 前缀的代码质量规则、`CS` 前缀的编译警告）必须全部清零。** 不得用 `#pragma warning disable` 或 `[SuppressMessage]` 抑制，除非有充分理由并在同一处注释说明原因。

以下为已踩过的坑，编写代码时直接按下表写，禁止再犯：

#### 6.10.1 IDE0305 — 集合初始化可简化

已知集合（`List` / 数组等）的 `.ToList()` 或 `new List<T>()` 场景，用集合表达式 `[.. x]` 替代。

```csharp
// ✅ 正确：集合表达式展开
List<string> codes = [.. saasUser.RoleNames];
List<IUser> result = [.. Users.Values.Where(u => u.IsEnabled).Cast<IUser>()];

// ❌ 错误：冗余 ToList / 旧式初始化（触发 IDE0305）
List<string> codes = saasUser.RoleNames.ToList();
List<IUser> result = Users.Values.Where(u => u.IsEnabled).Cast<IUser>().ToList();
```

#### 6.10.2 CS8619 — 可空性与目标类型不匹配

`Task.FromResult(value)` 中 `value` 为非空类型、而目标方法返回 `Task<string?>` 时，必须显式指定泛型参数，禁止依赖类型推断。

```csharp
// ✅ 正确：显式指定可空泛型参数
public Task<string?> GetNameAsync(IUser user)
    => Task.FromResult<string?>(user.UserName);   // 模型属性非空，接口要求可空

// ❌ 错误：类型推断为非空 Task<string>，与 Task<string?> 冲突（触发 CS8619）
public Task<string?> GetNameAsync(IUser user)
    => Task.FromResult(user.UserName);
```

**通用规则**：接口契约与实现模型的可空性不一致时，一律按**接口契约**显式标注泛型参数 / 返回类型，不要靠推断。

#### 6.10.3 CA2016 — CancellationToken 必须转发

调用任何带 `CancellationToken` 参数的方法时，必须把当前方法的 `cancellationToken` 透传下去（完整签名 / 传播 / 豁免规则见 **§6.13**）；确无令牌可用于传播时，仅允许 §6.13.2 豁免点场景显式传 `CancellationToken.None` 并注释原因。同时禁止用 `.Result` / `.Wait()` 同步阻塞异步方法——必须 `await`。

```csharp
// ✅ 正确：async/await，令牌全程透传（公开方法禁止 = default，见 §6.13）
public async Task<bool> RedeemAsync(IUser user, string code, CancellationToken cancellationToken)
{
    string? serialized = await GetTokenAsync(user, provider, name, cancellationToken);
    // ...
    await SetTokenAsync(user, provider, name, updated, cancellationToken);
    return true;
}

// ❌ 错误：.Result / .Wait() 阻塞 + 令牌丢失（触发 CA2016）
public Task<bool> RedeemAsync(IUser user, string code, CancellationToken cancellationToken)
{
    var serialized = GetTokenAsync(user, provider, name).Result;   // 丢令牌且同步阻塞
    SetTokenAsync(user, provider, name, updated).Wait();
    return Task.FromResult(true);
}
```

#### 6.10.4 CA1816 — Dispose 必须抑制终结器

实现 `IDisposable.Dispose()` 的方法体必须调用 `GC.SuppressFinalize(this)`（除非该类未定义终结器）。

```csharp
// ✅ 正确
public void Dispose() => GC.SuppressFinalize(this);

// ❌ 错误：空 Dispose 或漏调（触发 CA1816）
public void Dispose() { }
```

### 6.11 正则表达式强制规范（预编译）

**原则：所有正则表达式必须以预编译形态定义与使用，禁止动态构造未预编译的正则实例。**

#### 6.11.1 首选：`[GeneratedRegex]` 源生成（.NET 7+ 官方首推）

固定模式的正则必须声明为 `partial` 方法 + `[GeneratedRegex]`，编译期生成匹配代码，零运行时编译开销，天然满足预编译要求。

```csharp
// ✅ 正确：源生成正则（隐式预编译，等效且优于 Compiled）
public static partial class SensitiveDataMasker
{
    [GeneratedRegex(@"^(?<prefix>1[3-9]\d)\d{4}(?<suffix>\d{4})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, 200)]
    private static partial Regex PhonePattern();
}

// ❌ 错误：每次调用都动态构造，无预编译（触发性能隐患）
var match = new Regex(@"^1[3-9]\d{9}$").Match(phone);
```

#### 6.11.2 动态模式：静态字段 + `RegexOptions.Compiled` + 超时

模式确需运行时拼接（如配置驱动的脱敏规则）时，必须满足全部三条：

1. 实例缓存于 `static readonly` 字段（或 DI 单例），禁止在方法体内每次 `new`；
2. 必须启用 `RegexOptions.Compiled` 预编译选项，建议叠加 `RegexOptions.CultureInvariant`；
3. 必须设置匹配超时（构造参数 `matchTimeout` 或 `AppDomain` 全局超时），防止 ReDoS。

```csharp
// ✅ 正确：静态缓存 + Compiled + 超时
private static readonly Regex s_dynamicRule = new(
    pattern,
    RegexOptions.Compiled | RegexOptions.CultureInvariant,
    TimeSpan.FromMilliseconds(200));

// ❌ 错误：未预编译、无超时
private static readonly Regex s_rule = new(pattern);
```

#### 6.11.3 其他要求

- 处理用户输入 / 外部数据的正则，必须评审回溯风险（嵌套量词 `(a+)+` 类模式禁止）；
- 正则接收外部模式的扩展点（如 `SensitiveDataMasker.RegisterRule`）必须在入口校验 `RegexOptions.Compiled` 已启用，未启用直接拒绝；
- 禁止 `Regex.IsMatch(input, pattern)` 等每次调用都解析模式的静态便捷重载用于固定模式。

### 6.12 全局 JSON 序列化统一（强制）

**原则：全平台只允许一套 JsonSerializerOptions，禁止各组件自行 new。**

#### 6.12.1 唯一入口

所有 `JsonSerializer.Serialize` / `JsonSerializer.Deserialize` / `JsonDocument` 调用必须引用 `Qlx.Abstractions.Serialization.QlxJsonDefaults` 静态单例，禁止各处散落 `new JsonSerializerOptions(...)`。

| 静态属性 | 适用场景 | 关键配置 |
|---------|---------|---------|
| `QlxJsonDefaults.Default` | API 响应、缓存、事件总线、Outbox、通用 DTO | CamelCase + PropertyNameCaseInsensitive + MaxDepth=5 + 忽略 null |
| `QlxJsonDefaults.PreserveNull` | 审计快照（OldValues / NewValues 需完整反映变更，包括置为 null） | 同上但保留 null |

```csharp
// ✅ 正确
JsonSerializer.Serialize(response, QlxJsonDefaults.Default);
JsonSerializer.Deserialize<T>(json, QlxJsonDefaults.Default);

// ❌ 错误：自行 new，配置散落
private static readonly JsonSerializerOptions s_json = new() { WriteIndented = false };
```

#### 6.12.2 禁止事项

- 禁止各组件 / 模块声明私有 `s_jsonOptions` / `s_serializerOptions` 字段
- 禁止绕过 `QlxJsonDefaults` 使用裸 `JsonSerializerDefaults.Web`
- 如需新增专用 Options（极罕见），必须在 `QlxJsonDefaults` 上追加静态属性并说明场景，**禁止在组件内自建**

### 6.13 CancellationToken 传播规范（强制）

**原则：取消令牌必须从调用入口一路透传到最底层 I/O（EF / Redis / HTTP），任何一层都不能"好心"地让它变成可选或丢弃。**

#### 6.13.1 签名规则

| 层级 | 规则 |
|------|------|
| Controller Action、公开 Service 方法、公开仓储/租约/缓存等接口方法 | `CancellationToken cancellationToken` **必选参数**，**禁止 `= default`** |
| 私有 / internal 方法 | 允许 `= default`（仅限确无外部调用方传递的场景） |
| 参数位置 | 必选参数在前；`CancellationToken` 之后**禁止再出现必需参数**，可选参数（`= null`）允许跟在其后 |

```csharp
// ✅ 正确：公开接口方法，token 必选且位于必需参数之后
Task<T> GetOrCreateAsync<T>(
    string key,
    Func<CancellationToken, Task<T>> factory,
    CancellationToken cancellationToken,
    CacheEntryOptions? options = null);

// ❌ 错误：公开接口默认值（调用方忘传 = 永远无法取消）
Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken cancellationToken = default);
```

#### 6.13.2 透传规则

1. **EF Core 异步方法必须显式传入 token**：`ToListAsync(ct)` / `SaveChangesAsync(ct)` / `ExecuteUpdateAsync(..., ct)` / `CreateDbContextAsync(ct)` 等，禁止无参重载。
2. Controller → Service → Repository / BuildingBlocks 全链路透传 `HttpContext.RequestAborted`（Controller 内即 `cancellationToken`）。
3. `CancellationToken.None` **禁止硬编码**，仅允许以下豁免点（使用处必须注释【豁免点 N】+ 原因）：
   - **豁免点 1**：EF `OnConfiguring` 等框架同步回调（无环境令牌可传播，如 `TenantDbContext` 连接池闸门）；
   - **豁免点 2**：进程退出 / 内部生命周期终结等确无请求上下文的场景。
4. **禁止内部随意新建 `CancellationTokenSource`**。CTS 只允许出现在生命周期所有者手中（`IHostApplicationLifetime` 适配、BackgroundService 停止信号、租约续期循环等自有后台任务的取消源），普通业务方法一律透传外部令牌。
5. **禁止吞 `OperationCanceledException`**：`catch (OperationCanceledException)` 空处理仅限"等待内部可控循环按预期取消"的场景（如 `DisposeAsync` 中 `await _renewalTask`）；外部传入令牌触发的取消必须向上传播，禁止 catch 后返回默认值 / 空结果 / 继续执行。业务代码不需要也不应该捕获它——让宿主统一处理。

#### 6.13.3 排查命令（评审自查）

```text
grep -n "CancellationToken \w+ = default"   # src 下命中即违规（豁免点除外）
grep -n "CancellationToken.None"            # 无【豁免点】注释即违规
grep -n "new CancellationTokenSource"       # 非生命周期所有者即违规
grep -nE "\.(ToListAsync|SaveChangesAsync|AnyAsync|CountAsync|FirstOrDefaultAsync|SingleOrDefaultAsync|ExecuteUpdateAsync|ExecuteDeleteAsync)\(\)"  # 无参即丢 token
```

---

## 七、分层架构强制规则

### 7.1 API 层（Controller / Minimal API）

1. 仅负责参数校验、权限校验、结果统一封装（`ApiResponse<T>`），**禁止编写业务逻辑**
2. 参数校验错误必须使用 `nameof` 标记字段名
3. 所有接口必须异步，返回 `Task<IActionResult>`
4. 禁止直接依赖具体实现类，必须依赖接口
5. 对外 ID 一律混淆字符串（框架在收发边界自动加解密），禁止在 API 层手工 `Encode` / `Decode`

### 7.2 Service 层

1. 承载核心业务逻辑、事务控制、领域规则
2. 业务 Service 必须先定义接口，再写实现，通过 DI 注入
3. 事务边界内禁止跨 `await` 使用 `Span<T>`
4. Service 注册生命周期：多租户场景必须 `Scoped`

### 7.3 Repository 层 / 数据访问

1. 封装数据操作，**禁止泄露业务逻辑**
2. 仅注入**本模块自己的** DbContext（每模块独立 DbContext，禁止共享 DbContext、禁止引用他模块 DbContext）
3. 所有查询必须参数化，禁止字符串拼接 SQL
4. 大数据量查询必须分页，禁止全表返回实体集合
5. 只读查询使用 `AsNoTracking`，写操作使用追踪
6. 多租户过滤由 <c>TenantDbContext</c> 基类自动注入 TenantId 全局查询过滤 + IsDeleted 软删过滤，**禁止各实体 / 各 Service 手工写 <c>TenantId == currentTenantId</c>**

### 7.4 Domain 层

1. 仅包含实体、枚举、值对象，**无外部依赖**
2. 禁止在 Domain 层引入业务逻辑或基础设施依赖

### 7.5 模块边界（模块化单体铁律）

1. 业务 / 支撑模块**只允许引用** `Qlx.Abstractions` 与 `Qlx.BuildingBlocks`
2. **禁止业务模块之间直接项目引用**，禁止直接 `new` 他模块实现
3. **禁止跨模块 Join 他模块数据表**、禁止引用他模块 DbContext / 实体
4. 模块间通信仅三种：进程内事件总线 + Outbox、`Qlx.Abstractions` 同步查询接口、（禁止）跨库 Join
5. 宿主（Host）引用全部模块并负责装配，模块不得引用宿主

（对应架构规格 2.3.2，由 CI 引用图检查 + NetArchTest 守护。）

### 7.6 配置体系边界（强制）

**铁律**（对应架构规格 2.3.9 C3 / C4 / C5）：

| 铁律 | 内容 |
|------|------|
| **C2** 强类型 Options + 启动校验 | 配置缺失 / 非法时应用启动即失败，**禁止运行时裸读 `IConfiguration["..."]` 字符串键** |
| **C3** 四级配置目录 | `config/common` 全平台默认 / `config/portal` 门户专用 / `config/tenant` 租户域默认 / `config/modules/<模块>` 功能级；新增模块必须自带配置文件夹与 Options 类型 |
| **C4** 密钥只存引用 | 连接串、DB 凭据、API Key、证书密码等敏感值**禁止明文存 config/*.json**，只存引用（如 Redis 中 key 或 DPAPI 加密后的 blob 引用），运行时经 Data Protection 解密注入 |
| **C5** 静态配置 vs 业务参数 | `config/*.json` 只承载**静态部署配置**（连接串、间隔、开关、默认值）；**业务运行参数**（审批金额区间、信用阈值、预警天数、费用标准等"阈值X"）必须走数据库参数表、由系统管理在线维护——**禁止写进 config/*.json** |
| **C6** 模块配置隔离 | 模块只允许注入本模块 + common 的 Options 类型；**禁止跨模块读取配置节** |

```csharp
// ✅ 正确：强类型 Options + ValidateOnStart
builder.Services.Configure<CacheOptions>(configuration.GetSection("Cache"));
builder.Services.AddOptions<CacheOptions>().ValidateOnStart();

// ❌ 错误：运行时裸读字符串键
string connectionString = builder.Configuration["Cache:RedisConnectionString"];
```

### 7.7 全局常量集中管理（强制）

**原则**：跨模块共享的字符串常量（尤其是基础设施标识、Data Protection purpose、配置节名、已知 tenantId 等）**禁止散落在各模块内硬编码**，必须在 `Qlx.Abstractions.Constants` 统一入口集中声明。

#### 7.7.1 管理范围

| 常量类别 | 集中类名 | 命名规则 | 示例 |
|---------|---------|---------|------|
| Data Protection Purpose | `QlxDataProtectionPurposes` | `Qlx.Erpv3.{业务域}.{用途}` | `Qlx.Erpv3.TenantDatabaseCredentials` |
| 强类型 Options 配置节名 | 各 Options 类自带 `SectionName` 常量 | `Qlx.{模块}:{配置}` | `SnowflakeOptions.SectionName = "Qlx:Ids:Snowflake"` |
| 跨模块约定的固定值（如 Provisioning 租户 Id） | `QlxInfrastructureConstants` | 业务语义命名 | `ProvisioningTenantId = 1` |

#### 7.7.2 强制规则

```csharp
// ✅ 正确：引用全局常量
_protector = dataProtection.CreateProtector(QlxDataProtectionPurposes.TenantDatabaseCredentials);

// ❌ 错误：模块内硬编码 purpose（禁止清单 #52）
private static readonly string ProtectionPurpose = "Qlx.Erpv3.TenantDatabaseCredentials";
```

#### 7.7.3 例外

- 模块**内部**使用的私有常量（不跨模块、不含基础设施标识），允许在模块内用 `private const` 声明——但优先考虑是否可以抽象为跨模块共享。
- 局部 `const` 字符串用于参数校验、消息格式化等（非基础设施标识），不受本条约束。

---

## 八、EF Core 专项强制规则

### 8.1 迁移管理

必须使用迁移管理数据库结构，禁止手动修改数据库；**每模块独立 Migrations**；新租户建库 = 创建空库 + 按依赖序执行全部已许可模块迁移。

### 8.2 预加载

禁用延迟加载，必须使用 `Include` / `ThenInclude` 预加载关联数据。

### 8.3 批量操作

批量操作优先使用 `ExecuteUpdateAsync` / `ExecuteDeleteAsync`，禁止循环提交。

### 8.4 参数化

所有查询必须参数化，禁止字符串拼接 SQL。

### 8.5 分页

大数据量查询必须分页，禁止全表返回实体集合。

### 8.6 客户端求值

禁止在 LINQ 中调用未被 EF Core 支持的自定义方法，避免客户端求值。

### 8.7 事务边界

事务范围必须明确，禁止跨服务共享事务。

### 8.8 隔离级别

写操作必须指定事务隔离级别，默认 `ReadCommitted`。

### 8.9 跨模块数据访问

禁止在 EF Core 查询中 `Join` 他模块数据表，也禁止引用他模块 DbContext / 实体（模块化单体铁律，见 7.5）。

### 8.10 实体基类强制继承（铁律）

**所有业务实体必须**从 `Qlx.Abstractions.Domain` 基类中选择一个继承，禁止自定义 `Id` / `CreatedAt` / `UpdatedAt` / `IsDeleted` 等审计字段。基类选择决策树：

```
这个实体需要什么能力？
├── 雪花 Id（long）+ 审计字段 + 租户隔离 + 软删  → TenantFullAuditableEntity
├── 雪花 Id + 审计字段 + 租户隔离（不需软删）    → TenantAuditableEntity
├── 雪花 Id + 审计字段（门户级，不需租户隔离）    → AuditableEntity
└── 仅雪花 Id 主键，无审计（极罕见：权限点定义、只读字典） → Entity
```

**强制**：

1. 实体类型**禁止**自行声明 `Id { get; private init; }` / `CreatedAt { get; private init; }` / `UpdatedAt` / `TenantId` / `IsDeleted` 等基类已提供的属性。基类属性由 `AuditSaveChangesInterceptor` 自动填充。
2. 门户模块的非租户隔离实体（`Tenant`、`TenantDatabase`、`ModuleDefinition`、`TenantModule`）继承 `AuditableEntity`，**不要**实现 `ITenantEntity` 接口。
3. `PermissionDefinition` 仅需雪花 Id，继承裸 `Entity` 即可（权限点只读，无审计需求）。

### 8.11 DbContext 继承铁律

| DbContext 类型 | 必须继承 | 原因 |
|----------------|---------|------|
| 所有**业务模块** DbContext（采购、销售、商品、物流、人员、财务、票据、合同、项目、设备租赁） | **`TenantDbContext`** | 基类提供：动态租户连接串路由 + TenantId/IsDeleted 全局查询过滤 + 拦截器自动注入 + snake_case 表名/列名 + 模块表前缀自动拼接 |
| `PortalDbContext` | `DbContext`（固定连接串，非租户路由） | 门户元数据（租户注册、模块定义）跨所有租户，不属租户隔离数据 |
| `AuditDbContext` | `DbContext`（固定连接串，非租户路由） | 审计日志跨租户集中存储；**刻意不继承 TenantDbContext**——否则拦截器写审计快照时会循环递归写入审计库 |

#### TenantDbContext 使用约定

- 构造函数固定签名：`(ITenantDatabaseResolver resolver, long tenantId, DbContextOptions baseOptions)`，Host 注册时传入当前请求的 `tenantId`。
- 子类**禁止重写** `OnConfiguring` / `OnModelCreating` 核心逻辑；如需追加配置，调用 `base.OnModelCreating(modelBuilder)` 后再追加。
- 子类可覆写 `GetModuleTablePrefix()` 返回模块前缀（如 `"pur_"` / `"sal_"`），基类自动将表名拼接为 `{prefix}_{snake_case_entity_name}`。

#### 拦截器注入约定

- `TenantDbContext` / `PortalDbContext` 的 `OnConfiguring` **必须**从 DI 容器解析 `IInterceptor` 并调用 `optionsBuilder.AddInterceptors(...)`，确保 `AuditSaveChangesInterceptor` 自动填充雪花 Id + 审计字段 + 软删改写。
- `AuditDbContext` **禁止**注入审计拦截器（会在保存审计快照时触发循环递归）。

### 8.12 表名 / 列名命名约定

- **表名**：模块前缀（可选）+ snake_case 实体名，如 `por_tenants`、`por_module_definitions`、`pur_purchase_orders`。
- **列名**：全部 snake_case（`CreatedAt` → `created_at`，`TenantCode` → `tenant_code`）。
- 以上转换由 `TenantDbContext` 基类 `OnModelCreating` 自动执行，PortalDbContext 由 Fluent API 显式配置。

### 8.13 AuditSaveChangesInterceptor 规则

**拦截器自动执行以下操作，业务代码禁止手动重复**：

| 场景 | 自动操作 | 业务代码禁止 |
|------|---------|-------------|
| 新增实体（`EntityState.Added`） | 填雪花 Id、`CreatedAt`/`UpdatedAt`/`CreatedBy`/`UpdatedBy` | 禁止 `entity.Id = snowflake.NextId()`；禁止手动赋 `CreatedAt = DateTimeOffset.UtcNow` |
| 修改实体（`EntityState.Modified`） | 填 `UpdatedAt`/`UpdatedBy` | 禁止在 domain 方法里手动 `UpdatedAt = DateTimeOffset.UtcNow` |
| 删除 `FullAuditableEntity`（`EntityState.Deleted`） | 改写为 `Modified`，填 `IsDeleted=true` + `DeletedAt`/`DeletedBy` + `UpdatedAt` | 禁止业务代码手动 `IsDeleted = true` 后改状态 |
| 保存后 | 收集 OldValues / NewValues JSON 快照 → 写审计库 | 禁止业务代码手动写 `AuditLog` |

**审计失败不阻断**：拦截器 `PersistSnapshotsAsync` 吞异常 + LoggerMessage 告警，**禁止业务代码在 SaveChanges 时依赖审计结果**。

### 8.14 ID 使用边界（雪花 long + Hashids 混淆）

**强制边界**（架构规格 2.3.8）：

| 场景 | ID 形态 | 处理层 |
|------|---------|--------|
| 进程内（事件契约 DTO、模块间查询接口、EF 实体主键、Outbox） | **真实 `long` 雪花 ID** | 直接传递，禁止在进程内做 Encode/Decode |
| 系统边界外（前端 API 请求/响应、开放 API、导出文件、外部回传存储） | **混淆字符串（Hashids 编码）** | 框架在收发边界的 Action Filter / 模型绑定器层自动加解密 |
| 跨系统存储引用 | 存混淆值 | 入库前必须先 `Decode`，禁止存混淆值直接入业务表 |

**禁止事项**：

- 业务代码禁止在 API 层手工调用 `IIdObfuscator.Encode` / `Decode`——由框架自动处理。
- `ICurrentUserContext.UserId` 类型为 `string?`（JWT Claim 以字符串承载），**禁止声明为 `long?`**。

**落地机制**（2026-10-22 确立，全平台单一实现）：

| 边界 | 实现 | 使用方式 |
|------|------|---------|
| 请求 / 响应体 | `QlxIdJsonMetadata`（`DefaultJsonTypeInfoResolver` + `Modifiers`） | 属性名为 `Id` 或以大写 `Id` 结尾且类型为 `long`/`long?` → 自动挂载 `HashidsJsonConverter` / `HashidsNullableJsonConverter`；DTO 零标注 |
| 路由 / 查询参数 | `HashidsLongModelBinderProvider`（插入 `MvcOptions.ModelBinderProviders[0]`） | Action 直接声明 `long`/`long?`，自动解码（Hashids 优先、纯数字回退）；缺省可空→`null`、非可空→`ModelState` 错误、非法值→"无效的资源 Id" |
| 统一注册 | `AddQlxJsonSerialization()` + `AddQlxIdModelBinding()`（`QlxMvcBuilderExtensions`） | Host 一次注册，所有模块自动复用；禁止模块自行注册绑定器或转换器 |
| 解析入口 | `ObfuscatedIdParser`（`Qlx.BuildingBlocks.Ids`，internal） | JSON Converter 与 ModelBinder 共用，禁止各自实现解析 |

**禁止**：

- 用 `options.JsonSerializerOptions.Converters.Add(new HashidsJsonConverter())` 做全局按类型注册——STJ 的类型转换器对**所有** `long` 生效，会误伤分页 `Total`、统计 `Value`、`RowsCopied` 等非 Id 数值。
- 在 DTO 上逐个属性标注 `[property: JsonConverter(typeof(HashidsJsonConverter))]`。

**未覆盖边界**：顶层 JSON 数组（如 `long[]` 成员 Id 集合）没有属性元数据 → 用 `string` 数组接收并在 Service 解码；属性名不符合约定却必须混淆时，才允许显式标注转换器。

### 8.15 禁止 N+1 查询与按需查询（强制）

**原则**：一次业务操作触发 N+1 条 SQL 或循环内逐条访问导航属性，会把数据库往返放大到不可控的数量级，是后端性能劣化的头号根源。本项目**所有 EF Core 查询必须遵循"一次批量、避免循环"**的原则，禁止以下两类反模式：

| 反模式 | 定义 | 触发场景 |
|--------|------|---------|
| **N+1 查询** | 先查主实体列表（1 条 SQL），再在循环 / LINQ 投影 / 逐个对象上触发关联子查询（N 条 SQL） | `foreach` 里调用 `_repository.FindByIdAsync(id)` / `.Include().ThenInclude()` 放到循环里 / 对已经 materialize 的实体访问未加载导航属性 |
| **按需查询** | 在 `Select` 投影表达式、属性初始化器、DTO 映射等**已经脱离 EF LINQ 上下文**的位置，访问 `collection[i].Navigation` 或 `entity.Collection.Select(...)` 触发隐式查询 | 禁用延迟加载后表现为 `NullReferenceException`，若启用延迟加载则等价于 N+1；**本项目已禁用延迟加载，按需查询会直接报错——这正是我们想要的，用作编译 / 运行时第一道防线** |

#### 8.15.1 三条正解（按场景优先级）

**① Include / ThenInclude 预加载**

主实体已知、关联数据较深、且需要在同一业务路径消费时，用 `Include` 一次性声明所有需要的导航属性。

```csharp
// ✅ 正确：一次 SQL，预加载全部需要的关联数据
var orders = await context.PurchaseOrders
    .Include(o => o.Lines)
        .ThenInclude(l => l.Product)
    .Include(o => o.Supplier)
    .AsNoTracking()
    .Where(o => o.TenantId == tenantId && o.Status == OrderStatus.Approved)
    .ToListAsync(cancellationToken);

// ❌ 错误：循环里逐个查（N+1）
var orders = await context.PurchaseOrders.ToListAsync(cancellationToken);   // 1 条
foreach (var order in orders)
{
    var supplier = await context.Suppliers.FindAsync(order.SupplierId, cancellationToken);   // N 条
    // ...
}
```

**② Select 投影 + 匿名 / DTO 类型**

只需要主实体的部分字段 + 关联的部分字段时，**禁止 `Include` 整实体**，直接在 `Select` 里投影出所需字段，让 EF Core 编译成一条定制化 SQL。

```csharp
// ✅ 正确：投影成 DTO，一条 SQL，只取需要的列
var summaries = await context.PurchaseOrders
    .Where(o => o.TenantId == tenantId && o.Status == OrderStatus.Approved)
    .Select(o => new
    {
        o.Id,
        o.OrderNo,
        o.TotalAmount,
        SupplierName = o.Supplier != null ? o.Supplier.Name : null,   // 关联字段直接投影
        LineCount   = o.Lines.Count                                  // 聚合也直接投影
    })
    .ToListAsync(cancellationToken);

// ❌ 错误：Include 整实体 + 循环里逐字段取值（虽然 SQL 只 1 条，但浪费 IO + 循环内访问导航属性在禁用延迟加载时会炸）
```

**③ 批量聚合 / 分离查询**

需要对关联集合做 `Sum` / `Count` / `Max` 等聚合、或主实体与关联数据不需要在同一内存结构里时，**直接单独聚合查询**，避免把整表拉进内存。

```csharp
// ✅ 正确：批量聚合，一条 SQL 返回每个订单的合计
var totals = await context.PurchaseOrderLines
    .Where(l => l.Order.TenantId == tenantId && l.Order.Status == OrderStatus.Approved)
    .GroupBy(l => l.OrderId)
    .Select(g => new { OrderId = g.Key, Total = g.Sum(l => l.Amount * l.Quantity) })
    .ToListAsync(cancellationToken);

// ❌ 错误：先拉订单列表，循环内逐个 Sum（N+1 或内存聚合）
```

#### 8.15.2 禁用延迟加载

`TenantDbContext` / `PortalDbContext` / `AuditDbContext` 一律显式 `UseLazyLoadingProxies(false)` 或**完全不注册**延迟加载代理——本项目已在 `TenantDbContext` 基类中统一关闭，禁止任何业务 DbContext 或临时查询绕过开启。禁用延迟加载本身就是按需查询的第一道防线（访问未加载导航属性会抛异常或返回 `null`）。

#### 8.15.3 合规自查清单（评审时逐项核对）

| # | 检查项 | 判定 |
|---|--------|------|
| 1 | `.ToListAsync()` / `.ToArrayAsync()` 之后（即 materialize 之后），是否出现任何对 DbSet 的再次查询？ | 命中 = N+1 |
| 2 | `foreach` / `for` / LINQ `ForEach` 循环体内，是否出现 `_dbSet.Find...` / `Where...` / `FirstOrDefault...` / `Single...`？ | 命中 = N+1 |
| 3 | Select 投影表达式里，是否访问了 `collection[i].Navigation` 这类已经脱离 EF 的路径？ | 命中 = 按需查询 |
| 4 | 是否使用了延迟加载代理（项目全局关闭）？ | 命中 = 违反 §8.2 + §8.15.2 |
| 5 | 循环内是否访问了任何导航属性（`.Lines` / `.Supplier` / `.Children`）？ | 命中 = N+1（禁用延迟加载时会直接抛异常） |

#### 8.15.4 排查命令（评审自查）

```text
# materialize 之后再次查询（ToList/ToArray/FirstOrDefault 之后同一方法内又出现 .Where/Find）
grep -nE "\.(ToListAsync|ToList|ToArrayAsync|FirstOrDefaultAsync|SingleOrDefaultAsync)\b" src -A 20 | grep -iE "\.(Where|Find|First|Single|Include)\("

# 循环内出现数据库查询
grep -nE "(foreach|for|while).*" src -A 5 | grep -iE "\.(Where|FindAny|FirstOrDefault|Single|ToList|ExecuteUpdate)"

# 启用延迟加载
grep -rn "UseLazyLoadingProxies" src
grep -rn "virtual.*ICollection" src --include="*.cs" | grep -i "public"
```

#### 8.15.5 允许的批量查询例外

以下场景**不属于** N+1，是合理的多次查询：

| 场景 | 说明 |
|------|------|
| **主从分离：先查主实体，再单独批量查所有关联** | 不是循环 N 次，而是 2~3 条 SQL（主表 1 条 + 关联表批量 1 条），然后内存里做字典映射 |
| **跨模块查询** | 模块化单体铁律禁止跨模块 Join，合法做法是先查本模块主实体，再通过 `Qlx.Abstractions` 同步查询接口批量拉他模块数据（接口内部也是一条批量 SQL） |
| **分页场景** | 第一页查主列表 + 一条 count，不触发循环子查询 |

**跨模块查询典型正解**：

```csharp
// ✅ 正确：先查本模块主实体（一条 SQL），再批量查他模块数据（一条 SQL）
var orders = await _db.PurchaseOrders
    .Where(o => ids.Contains(o.Id))
    .ToListAsync(cancellationToken);

// 通过 Abstractions 同步查询接口批量拉商品名称（一条 SQL）
var productNames = await _goodsQueryService.GetProductNamesByIdsAsync(
    orders.SelectMany(o => o.Lines.Select(l => l.ProductId)).Distinct().ToList(),
    cancellationToken);   // 接口内部实现：WHERE Id IN (...)，一条批量 SQL

// 内存组装
foreach (var order in orders)
    foreach (var line in order.Lines)
        line.ProductName = productNames.GetValueOrDefault(line.ProductId);
```

#### 8.15.6 改写关联表 / 中间表：禁止"先查 → RemoveRange → 循环 Add"反模式

关联表 / 中间表改写（如角色-权限、成员-角色关系替换）常见反模式：

```csharp
// ❌ 错误：先查全量 materialize（1 条 SQL + 变更跟踪）→ RemoveRange → 循环 Add（N 条跟踪变更）
var existing = await db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(ct);
db.RolePermissions.RemoveRange(existing);
foreach (var code in codes)
    db.RolePermissions.Add(new RolePermission(roleId, tenantId, code));
```

**三条正解**（按实体基类选择）：

| 实体基类 | 正解 | 说明 |
|---------|------|------|
| `Entity`（无审计、无软删） | `ExecuteDeleteAsync` + `AddRangeAsync`（直接 SQL，绕过拦截器语义一致） | 本项目 RolePermission / MemberRole 即此类 |
| `AuditableEntity`（有审计字段） | 保留 `RemoveRange` + `AddRangeAsync` + 单次 `SaveChangesAsync`（拦截器自动填充审计字段） | 硬删会丢失 UpdatedAt 等审计时间戳 |
| `FullAuditableEntity`（有软删） | 直接调用 EF `ExecuteUpdateAsync` 把 `IsDeleted=true` + 新 Insert，或保留 RemoveRange 让拦截器改写为软删 | `ExecuteDeleteAsync` 会绕过软删拦截器造成硬删，**严禁** |

```csharp
// ✅ Entity 基类（无审计/无软删）：ExecuteDelete + AddRangeAsync
await db.RolePermissions.Where(rp => rp.RoleId == roleId).ExecuteDeleteAsync(ct);
var rows = codes.Distinct().Select(c => new RolePermission(roleId, tenantId, c)).ToList();
if (rows.Count > 0) await db.RolePermissions.AddRangeAsync(rows, ct);

// ✅ FullAuditableEntity 基类（有软删）：保留 RemoveRange 让拦截器改写
var existing = await db.FooLinks.Where(x => x.ParentId == parentId).ToListAsync(ct);
db.FooLinks.RemoveRange(existing); // 拦截器会改写为 IsDeleted=true
await db.FooLinks.AddRangeAsync(newRows, ct);
await db.SaveChangesAsync(ct);
```

**核心原则**：`ExecuteDeleteAsync` / `ExecuteUpdateAsync` 是直接 SQL，**绕过拦截器和 ChangeTracker**——只有当实体既无审计字段又无软删时才能用；有任一特性的实体必须走 `RemoveRange` + `Add/AddRange` + 单次 `SaveChangesAsync`，让拦截器接管审计填充或软删改写。

---

## 九、强制禁止清单（违反即视为不合规）

| # | 禁止事项 | 对应章节 |
|---|---------|---------|
| 1 | 禁止在文件头省略摘要注释 | 第零节 |
| 2 | 禁止公开成员使用非 PascalCase 命名 | 第一节 1.1 |
| 3 | 禁止注释解释代码"做了什么"，只允许解释"为什么" | 第二节 2.2 |
| 4 | 禁止高层模块直接依赖具体实现类 | 第三节 3.1 |
| 5 | 禁止用 switch/if 硬编码业务分支分发，必须用多态 | 第三节 3.3 |
| 6 | 禁止业务模块之间直接调用，必须通过事件总线 | 第三节 3.5 |
| 7 | 禁止重复实现公共逻辑，必须抽离到 `Qlx.BuildingBlocks` / `Qlx.Abstractions` | 第四节 |
| 8 | 禁止在 Controller / API 层写业务逻辑 | 第七节 7.1 |
| 9 | 禁止业务 Service 注册为 Singleton（多租户场景必须 Scoped） | 第七节 7.2 |
| 10 | 禁止跨模块注入 / 引用他模块 DbContext（每模块独立 DbContext） | 第七节 7.3 |
| 11 | 禁止硬编码名称字符串，必须使用 `nameof` 运算符 | 第六节 6.8 / 6.9.4 |
| 12 | 禁止定义 `async void` 方法 | 第六节 6.7 |
| 13 | 禁止直接 `throw new Exception()` 抛出通用异常 | 第六节 6.5 |
| 14 | 禁止在 EF Core 查询中使用未支持特性导致客户端求值 | 第八节 8.6 |
| 15 | 禁止手动修改数据库结构，必须用迁移 | 第八节 8.1 |
| 16 | 禁止保留注释掉的废弃代码 | 第二节 2.2 |
| 17 | 禁止业务模块手工建表绕过迁移，必须通过本模块 EF Core Migration | 第八节 8.1 |
| 18 | 禁止业务实体 / Service 手工写 TenantId 过滤（由 TenantDbContext 全局查询过滤自动注入） | 第七节 7.3、第八节 8.10 |
| 19 | 禁止重复书写类型名的 `new`（必须用目标类型 `new` / 集合表达式） | 第六节 6.9.1、6.9.2 |
| 20 | 禁止传统字段 + 构造函数样板注入（应用主构造函数） | 第六节 6.9.3 |
| 21 | 禁止硬编码字段名字符串（必须用 `nameof`） | 第六节 6.9.4 |
| 22 | 禁止冗余 `.ToList()` / 旧式集合初始化（必须用集合表达式） | 第六节 6.10.1 |
| 23 | 禁止依赖类型推断导致可空性不匹配（接口契约优先，显式标注 `Task.FromResult<T?>`） | 第六节 6.10.2 |
| 24 | 禁止丢失 `CancellationToken` 转发、禁止 `.Result` / `.Wait()` 同步阻塞 | 第六节 6.10.3 |
| 25 | 禁止空 `Dispose()`，必须调用 `GC.SuppressFinalize(this)` | 第六节 6.10.4 |
| 26 | 禁止用 `#pragma warning disable` 抑制分析器（除在同一处注明充分理由） | 第六节 6.10 |
| 27 | 禁止动态构造未预编译正则（必须 `[GeneratedRegex]` 源生成，或静态字段 + `RegexOptions.Compiled` + 超时） | 第六节 6.11 |
| 28 | 禁止用 `typeof(x) == typeof(T)` / `x.GetType() == typeof(T)` 做类型判断，必须用 `is` 模式匹配 / 属性模式 | 第六节 6.9.5 |
| 29 | 禁止使用每次调用都解析模式的静态便捷重载（如 `Regex.IsMatch(input, pattern)`） | 第六节 6.11.3 |
| 30 | 禁止跨边界数据载体（DTO / 事件契约 / 响应包装）用可变 `class`，必须用 `record` | 第五节 5.2 |
| 31 | 禁止在 `record` 中定义可变属性（`{ get; set; }`） | 第五节 5.4 |
| 32 | 禁止把 `record` 用作 EF 实体 / 聚合根 | 第五节 5.7 |
| 33 | 禁止把 `record` 用作 DI 服务类型（Service / Repository / Handler / Controller / JobHandler） | 第五节 5.7 |
| 34 | 禁止把密钥、令牌、连接串等敏感值作为 `record` 属性（自动 `ToString()` 会泄漏） | 第五节 5.5 |
| 35 | 禁止配置 Options 用位置参数 `record`（必须 `init` 属性写法 + 默认值） | 第五节 5.4 |
| 36 | 禁止 `record struct` 超过 16 字节或定义可变字段（必须 `readonly record struct`） | 第五节 5.3 |
| 37 | 禁止跨模块 Join 他模块数据表或引用他模块 DbContext / 实体 | 第七节 7.5、第八节 8.9 |
| 38 | 禁止绕过模块 Manifest 私自注册路由 / 菜单 / 权限点 / 后台任务 / 事件订阅 | 第七节 7.5 |
| 39 | 禁止实体不继承 `Entity` / `AuditableEntity` / `FullAuditableEntity` / `TenantAuditableEntity` / `TenantFullAuditableEntity` 基类，禁止自定义 Id / 审计字段 | 第八节 8.10 |
| 40 | 禁止业务模块 DbContext 不继承 `TenantDbContext`（PortalDbContext / AuditDbContext 豁免） | 第八节 8.11 |
| 41 | 禁止 `AuditDbContext` 继承 `TenantDbContext` 或注入审计拦截器（会循环递归） | 第八节 8.11 |
| 42 | 禁止 `DbContext` 子类在 `OnConfiguring` / `OnModelCreating` 中忘记调用 `base.OnConfiguring` / `base.OnModelCreating` | 第八节 8.11 |
| 43 | 禁止业务代码手动赋雪花 Id、`CreatedAt`、`UpdatedAt`、`IsDeleted`、写审计日志（拦截器自动处理） | 第八节 8.13 |
| 44 | 禁止在 domain 方法里手动 `UpdatedAt = DateTimeOffset.UtcNow`（拦截器自动填充） | 第八节 8.13 |
| 45 | 禁止自行 `new JsonSerializerOptions`（必须引用 `QlxJsonDefaults.Default` / `PreserveNull`） | 第六节 6.12 |
| 46 | 禁止业务运行参数（审批阈值 / 信用区间 / 预警天数 / 费用标准等）写进 `config/*.json`（必须走数据库参数表） | 第七节 7.6 |
| 47 | 禁止明文存密钥、连接串、DB 凭据、API Key 到 `config/*.json`（只存引用 + Data Protection 解密） | 第七节 7.6 |
| 48 | 禁止运行时裸读 `IConfiguration["..."]` 字符串键（必须强类型 Options + `ValidateOnStart`） | 第七节 7.6 |
| 49 | 禁止业务代码在 API 层手工调用 `IIdObfuscator.Encode` / `Decode`（框架在边界自动处理） | 第八节 8.14 |
| 50 | 禁止 `ICurrentUserContext.UserId` 声明为 `long?`（必须 `string?`，JWT Claim 以字符串承载） | 第八节 8.14 |
| 51 | 禁止业务代码在进程内事件 / 模块间查询接口中传递混淆字符串 ID（进程内一律真实 `long`） | 第八节 8.14 |
| 52 | 禁止各模块硬编码 Data Protection purpose 或跨模块共享常量（必须引用 `Qlx.Abstractions.Constants` 全局入口） | 第七节 7.7 |
| 53 | 禁止 Controller / 公开 Service / 公开仓储与基础设施接口的 `CancellationToken` 参数带 `= default`（仅私有 / internal 方法允许） | 第六节 6.13.1 |
| 54 | 禁止 `CancellationToken` 参数后再出现必需参数（可选参数允许跟在其后） | 第六节 6.13.1 |
| 55 | 禁止 EF Core 异步方法调用无 token 重载（`ToListAsync()` / `SaveChangesAsync()` / `ExecuteUpdateAsync(...)` 等必须显式透传） | 第六节 6.13.2 |
| 56 | 禁止硬编码 `CancellationToken.None`（仅 §6.13.2 豁免点 1/2 场景允许，且必须注释【豁免点 N】+ 原因） | 第六节 6.13.2 |
| 57 | 禁止业务方法内部随意新建 `CancellationTokenSource`（CTS 仅限生命周期所有者：BackgroundService 停止信号、租约续期循环等） | 第六节 6.13.2 |
| 58 | 禁止吞 `OperationCanceledException`（仅限等待内部可控循环按预期取消的空 catch；外部令牌触发的取消必须向上传播） | 第六节 6.13.2 |
| 59 | 禁止 N+1 查询：先查主实体列表再在循环 / materialize 后逐个访问 DbSet / 导航属性（必须 Include 预加载、Select 投影或批量聚合，见 §8.15） | 第八节 8.15 |
| 60 | 禁止启用延迟加载或按需查询（`UseLazyLoadingProxies` / `virtual ICollection` 等）；本项目全局禁用延迟加载以硬性阻断按需查询反模式 | 第八节 8.2、第八节 8.15.2 |
| 61 | 禁止改写关联表 / 中间表时用「先查全量 materialize → RemoveRange → 循环 Add」模式（Entity 基类必须 `ExecuteDeleteAsync` + `AddRangeAsync`；AuditableEntity / FullAuditableEntity 保留 RemoveRange 让拦截器接管审计 / 软删；详见 §8.15.6） | 第八节 8.15.6 |
