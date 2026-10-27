# 阶段三实施细则（第一波 · 模块 01 基础信息中心）Spec

## Why

阶段 R 收口批已完成（后端 879/879、前端 176/176、构建 0 错误），阶段三可直接启动。当前 `Qlx.Modules.Bas` 仅覆盖租户自管理的组织/岗位/成员/角色，缺少供应商、客户、人员主数据、商品分类、计量单位、数据字典、编码规则、导入导出与变更日志，无法支撑后续 10 个业务模块的引用需求。本 spec 先固化阶段三「每模块 15 项交付物」的可执行落地细则，再按细则完成第一波模块 01，避免 11 个模块各自发挥导致架构漂移。

## What Changes

- 新增阶段三通用实施规范：项目结构、15 项交付物落地方式、DTO/Id 混淆、缓存、编码、状态机、事件、审批、任务、配置、测试与出口门禁。
- 在 `Qlx.Modules.Bas` 内完整化模块 01：新增组织历史、商品分类、计量单位、供应商、客户、人员主数据、数据字典、编码规则等领域实体与迁移。
- 新增 `/api/bas/*` 主数据端点与对应权限点/菜单；既有 `/api/tenant/*` 租户自管理端点保留（组织/岗位端点按 D2 调整）。
- 新增跨模块只读查询契约（组织/分类/供应商/客户/人员）与 3 个集成事件契约，落到 `Qlx.Abstractions`。
- 接入审批中心（7 个审批定义）与 `ApprovalCompletedEvent`；新增资质到期扫描与缓存清理两个后台任务。
- 统一发号：新增 Bas 内 `IBasCodeNumberGenerator`（`bas_code_rules` 驱动固定规则/年月/流水段），主数据编码不经 `IDocumentNumberGenerator`；岗位仍用 `IEncodingGenerator`。
- **BREAKING**：组织类型枚举重定义为 `Group/Subsidiary/Department/Project`，负责人由成员改为人员主数据引用（见 D2，含数据迁移与前端同步）。
- 导入模板注册表组合化：支持多模块各自注册模板定义（当前仅空实现，无法多模块共存）。
- 前端新增/扩展模块 01 页面（组织、分类、单位、供应商、客户、人员、字典、编码规则、变更日志），并完成浏览器闭环。
- 非破坏性：不重做成员/角色页面，不改动既有响应包契约 `{ success, code, message, data, timestamp }`。

## Impact

- Affected specs：`docs/开发推进计划.md` §阶段三模块 01、`docs/模块需求/01-基础信息中心.md`、`.trae/rules/global-cache-layering-standard.md`（C-x/B-x/V-x/N-x/E-x）、`.trae/rules/global-csharp-standard.md`、`docs/saas-architecture-spec.md`。
- Affected code：
  - `src/Qlx.Modules.Bas/`（Domain/Dtos/Services/Controllers/Jobs/Migrations/Manifest/EndpointPolicy）
  - `src/Qlx.Abstractions/Queries/`、`src/Qlx.Abstractions/Events/Bas/`、`src/Qlx.Abstractions/Constants/QlxDataProtectionPurposes.cs`
  - `src/Qlx.BuildingBlocks/ImportExport/`（组合注册表 + `IImportTemplateDefinitionProvider`）
  - `src/Qlx.Modules.Support/Services/Files/SupportSeedService.cs`（模块 01 默认审批流程种子）
  - `src/Qlx.Modules.Support/Services/Approvals/ApprovalFlowRegistry.cs`（本 bizType 零命中时补种）
  - `src/Qlx.Web/src/`（types/api/views/components/router/menu）
  - `tests/Qlx.BuildingBlocks.Tests/Bas/`、`tests/Qlx.ArchitectureTests/`、`src/Qlx.Web/src/**/__tests__/`
  - `config/modules/bas/bas.json`、`config/common/authorization.json`（如需覆盖）
- 数据库：新增 `bas_*` 表与索引，新增一次或多次 EF 迁移；不删除既有表（仅按 D2 迁移列数据）。

## 关键设计决策（X-4 会签项）

| # | 决策 | 说明 |
|---|------|------|
| D1 | 模块归属与端点分区 | 扩展 `Qlx.Modules.Bas`，不新建项目；`/api/tenant/*` 保留租户自管理，新增 `/api/bas/*` 主数据端点；权限点统一 `bas.<resource>.<action>`（全小写，如 `bas.coderule.menu`、`bas.changelog.menu`）。 |
| D2 | 组织类型与负责人（**BREAKING**） | `OrgNodeType` 重定义为 `Group=1 / Subsidiary=2 / Department=3 / Project=4`；`Organization.ManagerMemberId` 改为 `LeaderPersonId`（引用 `bas_persons`）。迁移映射：旧 1→1（Company→Group）、旧 2→3（Department→Department）、旧 3→3（Team→Department），旧负责人关联置空；前端组织类型联合类型/标签/图标同步更新。迁移须同批提供：迁移前计数快照、映射后校验（`Type` 仅含 1~4、`LeaderPersonId` 全空、无重复 `TenantId+Code`）、回滚脚本（按备份表恢复 `Type`/`ManagerMemberId`）。开发期数据量小，不做在线双写过渡。 |
| D3 | 编码策略与 N-x 偏差 | **不使用 `IDocumentNumberGenerator` 生成主数据编码**（该生成器固定输出「前缀-组织码-yyyyMM-流水」，组织码必填，适用于单据号，套到主数据会产生 `SUP-HQ-202610-000001`，与详设不符）。主数据编码由 Bas 内统一发号器 `IBasCodeNumberGenerator` 生成，规则来自 `bas_code_rules`（`SegmentsJson` 支持 `{fixed}` / `{yyyyMM}` / `{seq:N}` 三类段）：组织 `ORG-{类型码}-{seq:2}`、供应商 `SUP-{seq:6}`、客户 `CUS-{seq:6}`、员工 `EMP-{seq:6}`、劳务 `LAB-{seq:6}`、商品分类「父编码 + 2 位流水」；流水统一经 `ISerialSequenceStore` 原子取号（键 `bas-code:{bizType}:{tenantId}:{parent}`，键内含租户段）。岗位仍走 `IEncodingGenerator`（`pos-` 前缀 + 拼音首字母，与既有实现一致）。禁止前端提交编码字段。**偏差登记**：N-1「所有业务编码必须经 `IEncodingGenerator`」对主数据部分让位于模块详设 R9「统一发号服务 + 固定规则」；产品决策已于 2026-10-25 确认采用固定规则发号器，架构会签结论随批次文档回写（`docs/开发推进计划.md` 模块 01 偏差记录）。 |
| D4 | 枚举下发 | 新增 `/api/bas/*` DTO 的状态/类型字段一律下发数值枚举；既有 `/api/tenant/*` 字符串枚举契约保持不变，避免同批双重破坏。 |
| D5 | 敏感字段 | `Person.IdNo`、`Person.BankCardNo`、`Person.Mobile` 使用 ASP.NET Data Protection（purpose 常量 `Qlx.Erpv3.Bas.PersonSensitive`）加密存储；列表/详情默认脱敏；明文查看走独立权限点 `bas.person.field.plaintext` 并写审计。 |
| D6 | 变更日志 | 复用 `plog_audit`（审计库 `qlx_portal`，按 `tenant_id` 隔离），**不新建变更日志表**。注意 B4 起 `AuditSaveChangesInterceptor` 只做审计字段填充与软删改写，**不再自动写变更快照**；因此 Bas 写路径在事务提交后显式调用 `IAuditService.CaptureChangeAsync`（`AuditLogEntry`：EntityType/EntityId/Operation/OldValues/NewValues/ChangedBy/ChangedAt/TenantId，敏感字段先脱敏），查询页经 `IAuditService.QueryChangesAsync(tenantId, ...)` 过滤，禁止直连 `AuditDbContext` 或租户业务 DbContext 查审计表。「编码过滤」由前端先把编码解析为主数据 Id（列表接口）再按 `entityId` 查询，不在审计表做编码字符串扫描。 |
| D7 | 审批默认流程 | 7 个审批定义（`bas.org.adjust`、`bas.supplier.admission`、`bas.supplier.bank.change`、`bas.customer.admission`、`bas.customer.credit.adjust`、`bas.category.disable`、`bas.position.overstaff`）加入 `SupportSeedService` 种子，节点审批人统一 `ApprovalApproverTypes.Role` + `admin`（因 `BasApprovalApproverSource` 当前仅解析 `admin`/`member` 与 `DEPT_LEADER`，写入采购/财务角色会导致审批人解析为空、实例卡死）；角色化审批链待模块 06 落地后参数化扩展。**存量租户触发**：现有懒种子仅在「租户零流程定义」时触发，存量租户已有 4 个默认流程不会自动补种新增 7 个；本批同时把 `ApprovalFlowRegistry.ResolveEnabledAsync` 的补种条件改为「本 `bizType` 命中为 0 时补种一次后重查」，种子按 `DefCode` 幂等（已存在/已停用的一律跳过，不覆盖租户自定义与停用状态）。 |
| D8 | 导入模板注册表组合化 | `BuildingBlocks` 新增 `IImportTemplateDefinitionProvider` 与 `CompositeImportTemplateRegistry`（遍历 `IEnumerable<IImportTemplateDefinitionProvider>`），模块各自注册 provider；不改 2A 冻结契约（`IImportExportService` / `IImportRowSink` / `IExportDataProvider` 签名不变）。**注册顺序**：组合注册表由 `AddQlxBas()` 以 `AddSingleton<IImportTemplateRegistry, CompositeImportTemplateRegistry>()` 先注册；`AddQlxImportExport()` 内的 `TryAddSingleton<..., EmptyImportTemplateRegistry>()` 在其后执行自动失效（Host 装配顺序 `AddQlxBas()` 先于 `AddQlxModules()`）。无 Bas 的宿主仍保留空实现兜底。**tenantId 适配**：`IImportExportService`/`IImportRowSink` 的 `tenantId` 为 2A 冻结的 `Guid`，系统租户 Id 为 `long`；Bas 侧落点/数据源用 `GuidIdMapper.TryToLong(tenantId, out long tid)` 还原并与 `ITenantContext.TenantId` 交叉校验，校验失败抛 `BusinessException`，不改冻结契约。 |
| D9 | 缓存策略 | 主数据只读查询（组织/分类/单位/字典/编码规则）走 `ICacheService.GetOrCreateAsync`；键 `QlxCacheKey.BuildTenantKey(tenantId, "bas", "<domain>:<key>")`；`CacheEntryOptions.AbsoluteExpirationRelativeToNow = 5 分钟`（提供方默认 ±10% 抖动，禁止 `DisableJitter`），不存在结果由提供方写 30 秒 `NullMarker`（`NullValueExpiration` 不显式调大）；写路径提交后只 `RemoveByPrefixAsync($"tenant:{tenantId}:bas:")`，禁止双写；列表分页与 keyword 查询不缓存。**C-6 清理任务**：新增 `bas.cache.cleanup` 后台任务（`IJobHandler`，Manifest 注册，每日），任务随租户调度、以 `ITenantContext.TenantId` 只清 `tenant:{currentTenantId}:bas:` 前缀，幂等、结果落 `job_executions`。 |

## ADDED Requirements

### Requirement: 阶段三模块工程规范（15 项交付物）

系统 SHALL 为每个业务模块按固定目录与 15 项交付物落地，禁止跨模块程序集引用，禁止 Controller 承载业务逻辑。

#### Scenario: 新模块装配
- **WHEN** 新模块项目加入 `Qlx.Erpv3.slnx` 并按规范注册 DI、Manifest、迁移计划
- **THEN** Host 启动后 Manifest 同步权限点/菜单、租户库按拓扑序迁移、模块许可过滤与端点授权规则生效，`/health` 返回 200

#### Scenario: 契约边界
- **WHEN** 运行 `tests/Qlx.ArchitectureTests`
- **THEN** 模块程序集仅引用 `Qlx.Abstractions` + `Qlx.BuildingBlocks`，无模块间引用，无缓存提供方 SDK 直引，全部断言通过

### Requirement: 模块 01 组织架构与岗位

系统 SHALL 提供集团/子公司/部门/项目四级组织树、组织版本快照与按日期回看、岗位编制与在岗统计，并执行停用前置校验。

#### Scenario: 组织新增与编码
- **WHEN** 管理员新增组织且未填写编码
- **THEN** 后端经 `IBasCodeNumberGenerator`（`bas_code_rules` 规则 `ORG-{类型码}-{seq:2}`）生成唯一编码，前端无编码输入框，同级重名返回 409

#### Scenario: 架构调整审批
- **WHEN** 组织的新增/调级/停用/合并提交审批
- **THEN** 服务调用 `IApprovalService` 发起 `bas.org.adjust`，审批通过事件到达后才落库生效并写版本快照；驳回则不生效

#### Scenario: 停用阻断
- **WHEN** 停用仍有在职人员或关联在用岗位的组织
- **THEN** 返回 409 并给出阻断清单，不改变状态

#### Scenario: 版本回看
- **WHEN** 按日期查询组织树
- **THEN** 返回该日期生效的版本快照（扁平字段 + parentId），前端组装树

#### Scenario: 超编守卫
- **WHEN** 人员入职导致岗位在岗数超过编制数
- **THEN** 入职提交转入 `bas.position.overstaff` 审批，审批通过后才计入在岗

### Requirement: 商品分类与计量单位

系统 SHALL 提供多级商品分类树与计量单位组/单位/换算率维护，分类编码按层级生成且受引用保护。

#### Scenario: 分类编码
- **WHEN** 在父分类下新增子分类
- **THEN** 编码为「父编码 + 2 位流水」，层级不超过 `por_parameters.bas.category.maxLevel`（默认 4），超限返回 422

#### Scenario: 分类停用
- **WHEN** 停用已被商品引用的分类
- **THEN** 仅允许停用不允许删除，停用走 `bas.category.disable` 审批并广播分类停用事件

#### Scenario: 换算率变更
- **WHEN** 修改单位换算率
- **THEN** 写入变更审计（旧值/新值/操作人/时间），历史单据不回溯重算

### Requirement: 供应商与客户主数据

系统 SHALL 提供供应商/客户主档案及资质、银行、开票、评级、信用子表，执行准入审批、黑名单拦截、资质到期冻结与信用额度校验。

#### Scenario: 供应商准入
- **WHEN** 新建供应商（PENDING）并提交准入
- **THEN** 发起 `bas.supplier.admission`；通过后状态置 ACTIVE 并持久发布 `SupplierApprovedEvent`；PENDING/BLACKLIST 不可被采购引用

#### Scenario: 资质到期
- **WHEN** 资质在 `bas.supplier.cert.warnDays`（默认 30）天内到期
- **THEN** 每日任务推送预警消息；已过期未更新则自动置 FROZEN 并发布评级/状态变更事件，解冻需重新准入

#### Scenario: 客户信用
- **WHEN** 下单校验信用额度
- **THEN** `credit_used + 本单金额 ≤ credit_limit`，超限返回 409 并提示特批；额度调整按 `bas.customer.credit.approvalThreshold` 决定审批层级，通过后发布 `CreditLineAdjustedEvent`

#### Scenario: 黑名单
- **WHEN** 供应商评级的等级调整为 BLACKLIST
- **THEN** 全模块拦截引用并推送预警，变更写评级历史

### Requirement: 人员主数据

系统 SHALL 提供员工/劳务人员主数据、任职记录与证件影像，敏感字段加密存储与脱敏展示，执行证件号防重与离职再入职。

#### Scenario: 证件号防重
- **WHEN** 新增人员的证件号与同租户有效记录重复
- **THEN** 返回 409 并提示已存在记录；离职后再入职复用原 personId 并新增任职记录

#### Scenario: 脱敏与明文
- **WHEN** 普通角色查看人员列表/详情
- **THEN** 证件号、银行卡、手机号按规则脱敏；拥有 `bas.person.field.plaintext` 的角色可请求明文接口，且每次查看写审计

#### Scenario: 离职联动
- **WHEN** 人员离职生效
- **THEN** 停用其任职记录、回写岗位在岗人数，并发布 `PersonLeftEvent` 供登录体系停用账号

### Requirement: 数据字典与编码规则

系统 SHALL 提供集团级/子公司级两级数据字典与编码规则配置查询，字典项被引用时仅可停用。

#### Scenario: 两级字典
- **WHEN** 子公司用户读取字典
- **THEN** 可见集团级（只读）+ 本子公司级；其他子公司级字典不可见

#### Scenario: 编码规则
- **WHEN** 管理员查看编码规则
- **THEN** 返回业务对象、前缀、分段定义、启停状态；规则本身不可修改，仅可启停；发号统一由 `IBasCodeNumberGenerator` 按规则执行，页面无手填编码；新规则首次保存时校验分段模板（`{fixed}` / `{yyyyMM}` / `{seq:N}`）语法，非法返回 422

### Requirement: 基础数据导入导出与变更日志

系统 SHALL 复用支撑中心导入导出能力，提供供应商/客户/人员/商品分类模板导入、错误行回显与筛选结果导出，并提供按对象/编码/操作人/时间反查的变更日志。

#### Scenario: 模板导入
- **WHEN** 上传供应商/客户/人员/分类 Excel
- **THEN** 表头与行级格式校验、编码唯一性/证件格式/引用关系校验，成功行落库，失败行返回行号与原因；批量写入禁止逐行往返

#### Scenario: 变更日志
- **WHEN** 按对象类型、编码、操作人、时间查询
- **THEN** 经 `IAuditService.QueryChangesAsync(当前租户, entityType, entityId, changedBy, from, to)` 从 `plog_audit` 返回变更前后值、操作人、时间，敏感字段按脱敏规则裁剪；`QueryChangesAsync` 无分页参数，Bas 服务层统一做条数上限（单次最多 500 条）与页码切片，Controller 只透传 `PagedRequest`

### Requirement: 跨模块契约与集成事件

系统 SHALL 将下游模块所需只读查询下沉 `Qlx.Abstractions`，并发布/订阅集成事件，模块间零直接引用。

#### Scenario: 下游查询
- **WHEN** 采购/销售/人员模块需要组织、分类、供应商、客户、人员数据
- **THEN** 仅依赖 `IOrgQueryService`、`IProductCategoryQueryService`、`ISupplierQueryService`、`ICustomerQueryService`、`IPersonQueryService`（扁平行 record，Id 自动混淆）

#### Scenario: 事件投递
- **WHEN** 组织创建、供应商准入通过、信用额度调整
- **THEN** 经 `IEventBus.PublishDurableAsync` 投递（Outbox，at-least-once），消费方幂等；Manifest 声明订阅事件

### Requirement: 审批接入

系统 SHALL 仅依赖 `IApprovalService` 发起/处理审批，条件分支走 condition-builder AST，阈值一律读 `ISystemParameterProvider`（`por_parameters`）。

#### Scenario: 审批完成回写
- **WHEN** 收到 `ApprovalCompletedEvent` 且 `BizType` 属于 Bas 域
- **THEN** 按定义编码执行生效/驳回动作，重复事件幂等（同一实例只生效一次）

### Requirement: 前端模块 01 页面

前端 SHALL 复用 `components/crud`、`org-admin`、`approval` 组件族，承担全部树组装、枚举映射与展示格式化，按权限点裁剪按钮/菜单。

#### Scenario: 组织页增强
- **WHEN** 进入 `/manage/organizations`
- **THEN** 支持编码只读回显、四级类型、负责人（人员）选择、生效/失效日期、版本历史抽屉与调整审批状态展示

#### Scenario: 主数据页面
- **WHEN** 访问供应商/客户/人员/分类/单位/字典/编码规则/变更日志页面
- **THEN** 列表分页筛选、表单校验、导入导出、详情 Tab 均可操作，空态/错误态有提示，构建与 vitest 全绿

### Requirement: 验收门禁与文档回写

每批次 SHALL 通过构建、测试、架构守护、Host 冒烟与浏览器闭环，测试数量只增不减（后端基线 879、前端基线 176）。

#### Scenario: 批次出口
- **WHEN** 一个批次完成
- **THEN** `dotnet build` 0 警告 0 错误、`dotnet test` ≥879 全绿、`npx vitest run` ≥176 全绿、`npm run build` 0 错误、`/health` 200、模块 01 主路径浏览器闭环截图存档，并回写资产台账与本计划状态列

## 自查校正（2026-10-25，实施前置）

初稿完成后按用户要求做代码级自查，发现 6 处与实现事实不符，已按下列口径校正；校正前不得进入实现（避免返工）。

| # | 代码事实 | 初稿问题 | 校正口径 |
|---|---|---|---|
| S1 | [`DocumentNumberGenerator.NextAsync`](file:///e:/ERPv2/eprv3/src/Qlx.BuildingBlocks/Numbering/DocumentNumberGenerator.cs#L28-L56) 强制 `prefix`+`orgCode`，输出恒含 `yyyyMM` | 主数据编码套用该生成器会产生 `SUP-HQ-202610-000001` | 主数据改用 Bas 内 `IBasCodeNumberGenerator`（`bas_code_rules` 驱动），`IDocumentNumberGenerator` 仅留给后续单据号（D3） |
| S2 | [`AuditSaveChangesInterceptor`](file:///e:/ERPv2/eprv3/src/Qlx.BuildingBlocks/Audit/AuditSaveChangesInterceptor.cs#L21-L29) B4 起只填审计字段/软删，**不写变更快照** | 误以为 `plog_audit` 自动落库 | 写路径显式调用 [`IAuditService.CaptureChangeAsync`](file:///e:/ERPv2/eprv3/src/Qlx.Abstractions/Audit/IAuditService.cs#L46-L68)，查询走 `QueryChangesAsync`（D6） |
| S3 | [`IImportExportService`](file:///e:/ERPv2/eprv3/src/Qlx.Abstractions/ImportExport/IImportExportService.cs#L25-L35) 与 `IImportRowSink` 的 `tenantId` 为 `Guid`，系统租户 Id 为 `long` | 未说明适配，落地必卡 | Bas 落点/数据源用 [`GuidIdMapper.TryToLong`](file:///e:/ERPv2/eprv3/src/Qlx.BuildingBlocks/Ids/GuidIdMapper.cs#L45-L56) 还原并与 `ITenantContext` 交叉校验（D8） |
| S4 | [`ApprovalFlowRegistry.ResolveEnabledAsync`](file:///e:/ERPv2/eprv3/src/Qlx.Modules.Support/Services/Approvals/ApprovalFlowRegistry.cs#L90-L145) 仅在「租户零流程定义」时懒种子 | 存量租户补不到新增 7 个流程，审批直接不可用 | 补种条件改为「本 bizType 命中 0 时补种一次后重查」，按 `DefCode` 幂等（D7） |
| S5 | [`BasApprovalApproverSource`](file:///e:/ERPv2/eprv3/src/Qlx.Modules.Bas/Services/BasApprovalApproverSource.cs) 仅解析 `admin`/`member`/`DEPT_LEADER` | 采购/财务角色链会解析为空导致实例卡死 | 种子节点统一 `ROLE=admin`；角色链延后到模块 06（D7） |
| S6 | [`AddQlxImportExport`](file:///e:/ERPv2/eprv3/src/Qlx.BuildingBlocks/ImportExport/ImportExportServiceCollectionExtensions.cs#L29-L36) 用 `TryAddSingleton` 空实现；Host 装配 `AddQlxBas()` 早于 `AddQlxModules()` | 未说明组合注册表替换顺序 | 组合注册表由 `AddQlxBas()` 先行 `AddSingleton`，`TryAdd` 自动失效；无 Bas 宿主保留兜底（D8） |

另确认无需改动：`QlxCacheKey.BuildTenantKey(long, string, string)`、`ICacheService.GetOrCreateAsync(key, factory, ct, options?)`、`CacheEntryOptions`（抖动默认开启 + `NullValueExpiration`）与 D9 表述一致。

## 非目标（本批不做）

- 不实现模块 02~11（后续按波次以 spec 增量追加）。
- 不重做租户成员/角色页面；不改造门户端。
- 不实现四库分离生产环境实测（L-15，阶段四前完成）。
- 不实现考勤设备对接、工资代发、发票真伪核验等后续模块能力。
