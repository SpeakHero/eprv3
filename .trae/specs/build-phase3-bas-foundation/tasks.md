# Tasks

> 依据：`spec.md`（阶段三实施细则 · 第一波模块 01）。执行顺序固定，前置任务未完成不得启动后继任务；每完成一个任务即执行一次构建 + 测试，测试数量只增不减。

- [x] Task 1: 阶段三通用规范落地与 Bas 模块骨架
  - [x] 1.1 新增 `config/modules/bas/bas.json`：仅放技术参数（`CacheTtlSeconds=300`、`CertScanBatchSize=200`），业务阈值不写入配置（走 `por_parameters`）。
  - [x] 1.2 扩展 `BasModule.Manifest`：新增主数据权限点（`bas.supplier.*`/`bas.customer.*`/`bas.person.*`/`bas.category.*`/`bas.unit.*`/`bas.dict.*`/`bas.coderule.*`/`bas.changelog.*`）、8 条菜单（路由 `/manage/master/*` 与 `/manage/organizations`）、两个任务 `bas.supplier.cert.expiry` 与 `bas.cache.cleanup`（`JobDefinition` 六段 CRON）、订阅事件 `ApprovalCompletedEvent`。
  - [x] 1.3 扩展 `BasEndpointPolicyMap`：为新控制器登记控制器级 `RequireTenantContext` 与动作级权限点，与新增端点逐一对应。
  - [x] 1.4 在 `BasServiceCollectionExtensions.AddQlxBas` 中注册新增服务、仓储、任务处理器、事件消费者与导入模板 provider（Scoped/Singleton 按生命周期规范）；**组合导入模板注册表在此处 `AddSingleton` 先行注册**（Host 中 `AddQlxBas()` 早于 `AddQlxModules()`，`AddQlxImportExport` 的 `TryAddSingleton` 空实现会自动失效）。
  - [x] 1.5 建立 `Domain/{Org,Goods,Supplier,Customer,Person,Dictionary,Numbering}` 目录与空分类；确认 `BasTenantDbContext.DbSets.cs` 按域分组。
  - [x] 1.6 验证：`dotnet build Qlx.Erpv3.slnx` 0 警告 0 错误、`dotnet test` ≥879 全绿、架构测试通过。
  - [x] 1.7 支撑中心审批种子（`Qlx.Modules.Support`）：在 `SupportSeedService.s_approvalFlows` 追加 7 条 Bas 默认流程（`bas.org.adjust`、`bas.supplier.admission`、`bas.supplier.bank.change`、`bas.customer.admission`、`bas.customer.credit.adjust`、`bas.category.disable`、`bas.position.overstaff`），节点审批人统一 `ApprovalApproverTypes.Role` + `admin`；同时把 `ApprovalFlowRegistry.ResolveEnabledAsync` 补种条件改为「本 bizType 命中 0 时补种一次后重查」（按 `DefCode` 幂等，不覆盖已停用/自定义）。
  - [x] 1.8 任务 `bas.cache.cleanup`：`IJobHandler` 以 `ITenantContext.TenantId` 调用 `RemoveByPrefixAsync($"tenant:{tenantId}:bas:")`，幂等并落 `job_executions`。

- [x] Task 2: 跨模块契约与集成事件（`Qlx.Abstractions`）
  - [x] 2.1 新增 `Queries/IOrgQueryService.cs`、`IProductCategoryQueryService.cs`、`ISupplierQueryService.cs`、`ICustomerQueryService.cs`、`IPersonQueryService.cs`，行 DTO 为具名 record（`long Id` 自动 Hashids），仅扁平原始字段，禁止树/展示拼接。
  - [x] 2.2 新增 `Events/Bas/OrganizationCreatedEvent.cs`、`SupplierApprovedEvent.cs`、`CreditLineAdjustedEvent.cs`，继承 `IntegrationEvent`，含 TenantCode/BizId/BizNo 等最小事实字段。
  - [x] 2.3 在 `QlxDataProtectionPurposes` 追加 `BasPersonSensitive = "Qlx.Erpv3.Bas.PersonSensitive"`。
  - [x] 2.4 在 Bas 内实现查询接口（读路径 `GetOrCreateAsync`，键 `BuildTenantKey("bas", ...)`，写路径只删缓存）；实现组织创建事件的持久发布。**落地拆分**：组织查询随 Task 3、分类查询随 Task 4、供应商/客户查询与两个事件随 Task 5、人员查询随 Task 6 一并实现（避免为不存在的实体预先写空实现）。
  - [x] 2.5 验证：架构测试（引用图/隔离/缓存守护）全绿；单元测试覆盖查询接口缓存命中与失效。

- [x] Task 3: 组织架构与岗位（含历史版本、审批、事件）
  - [x] 3.1 领域实体：扩展 `Organization`（`Code`、`OrgNodeType` 按 D2 重定义、`LeaderPersonId`、`EffectDate`、`ExpiryDate`、`Version`、`SortOrder`、`Status`、`IsDeleted`）；新增 `OrgNodeHistory`（快照 JSON + Version + EffectiveFrom + ChangedBy/At）；扩展 `Position`（`Duty`、`HeadcountPlan`、`CurrentCount`、`Status`、`IsDeleted`）。
  - [x] 3.2 新增 EF 迁移（`BasTenantDbContext` 工厂生成），含 D2 数据迁移 SQL（旧 1→1、2→3、3→3，负责人置空）与唯一索引（`TenantId+Code`、`ParentId`、`Status`）。
  - [x] 3.3 编码与规则：新增 `Domain/Numbering/CodeRule`（`BizType`、`Prefix`、`SegmentsJson`、`Status`，不存流水值）与默认规则种子（`org`=`ORG-{type}-{seq:2}`、`supplier`=`SUP-{seq:6}`、`customer`=`CUS-{seq:6}`、`employee`=`EMP-{seq:6}`、`labour`=`LAB-{seq:6}`、`category`=`{parent}{seq:2}`）；实现 `IBasCodeNumberGenerator`（解析 `{fixed}`/`{yyyyMM}`/`{seq:N}` 段，经 `ISerialSequenceStore` 取号，键 `bas-code:{bizType}:{tenantId}:{parent}`，唯一性冲突重试上限 100）；组织编码走该生成器，岗位编码走既有 `IEncodingGenerator`（`pos-` 前缀）。**禁止使用 `IDocumentNumberGenerator` 生成主数据编码**。
  - [x] 3.4 服务：组织 CRUD/调级/合并/停用前置校验（无在用岗位；在职人员校验随 Task 6 补）、版本快照与按日期回看；岗位 CRUD、编制调整、在岗统计（人员任职记录汇总）、停用前置（在岗=0）；组织/岗位每次写操作在事务提交后调用 `IAuditService.CaptureChangeAsync`（旧值/新值 JSON，`EntityType` 用实体名）。
  - [x] 3.5 审批：发起 `bas.org.adjust`、`bas.position.overstaff`（`StartApprovalRequest.BizType` 用同一编码）；订阅 `ApprovalCompletedEvent`，仅处理 `BizType` ∈ {`bas.org.adjust`,`bas.position.overstaff`}，通过后生效、驳回不变更、重复事件幂等（按 `ApprovalInstanceId` 判重）。
  - [x] 3.6 事件：`OrganizationCreatedEvent` 持久发布；Manifest `SubscribedEvents` 登记。
  - [x] 3.7 新增 `Controllers/OrgController.cs`、`Controllers/PositionController.cs`（`/api/bas/orgs`、`/api/bas/positions`），返回值 `ApiResponse<T>`，写端点挂 `[AuditOperation]`；更新端点策略映射。
  - [x] 3.8 测试：Sqlite 内存库覆盖编码唯一（`IBasCodeNumberGenerator` 段解析/并发递增/冲突重试）、停用阻断、版本回看、超编守卫、审批完成回写与幂等、`CaptureChangeAsync` 落审计；验证构建 + 全量测试。

- [x] Task 4: 商品分类与计量单位
  - [x] 4.1 领域实体：`ItemCategory`（`Code`、`Name`、`ParentId`、`LevelNo`、`Status`、`UnitGroupId`、`IsDeleted`）、`UnitGroup`、`Unit`（`Precision`）、`UnitConversion`（`Rate` decimal(18,8)）。
  - [x] 4.2 EF 迁移：`bas_item_categories`/`bas_unit_groups`/`bas_units`/`bas_unit_conversions` + 唯一索引与父级索引。
  - [x] 4.3 服务：层级编码经 `IBasCodeNumberGenerator`（`category` 规则 `{parent}{seq:2}`，父编码作流水键的 `parent` 段）、层级上限读 `por_parameters.bas.category.maxLevel`（默认 4）、引用保护（被商品引用仅可停用；商品模块未落地前先校验下级分类）、停用审批 `bas.category.disable`、换算率变更写审计（`IAuditService.CaptureChangeAsync`）。
  - [x] 4.4 新增 `Controllers/CategoryController.cs`、`Controllers/UnitController.cs`（`/api/bas/categories`、`/api/bas/unit-groups`、`/api/bas/units`），端点策略与审计同步。
  - [x] 4.5 测试：层级编码连续性/并发唯一、层级上限 422、引用停用、换算率变更留痕；构建 + 全量测试。

- [x] Task 5: 供应商与客户主数据
  - [x] 5.1 领域实体：`Supplier` + `SupplierCert`/`SupplierBank`/`SupplierInvoice`/`SupplierGradeLog`；`Customer` + `CustomerBank`/`CustomerInvoice`/`CustomerCreditLog`；状态枚举 `Pending/Active/Frozen/Closed`，评级枚举 `A/B/C/Blacklist`，信用等级 `A/B/C/D`。
  - [x] 5.2 EF 迁移：8 张 `bas_*` 表 + `TenantId+Code`、`TenantId+Uscc` 唯一索引与子表外键索引。
  - [x] 5.3 服务：编码 `SUP-`/`CUS-` 经 `IBasCodeNumberGenerator`（规则 `supplier`/`customer`）、准入/评级/黑名单/冻结/终止状态机（`StateMachine` 守卫）、信用校验公式与占用汇总、额度分级审批阈值读参数、子表 CRUD 与关键变更留痕（`IAuditService.CaptureChangeAsync`，银行/开票字段脱敏后落快照）。
  - [x] 5.4 审批接入：`bas.supplier.admission`、`bas.supplier.bank.change`、`bas.customer.admission`、`bas.customer.credit.adjust`；订阅 `ApprovalCompletedEvent` 执行状态流转，发布 `SupplierApprovedEvent`、`CreditLineAdjustedEvent`。
  - [x] 5.5 后台任务 `bas.supplier.cert.expiry`：分批扫描到期资质，按 `bas.supplier.cert.warnDays`（默认 30）预警，过期自动 FROZEN，经 `IMessageService` 推送；实现 `IJobHandler` 并注册 Manifest。
  - [x] 5.6 新增 `Controllers/SupplierController.cs`、`Controllers/CustomerController.cs`（列表分页筛选、详情 Tab、子表端点、提交准入、额度调整），端点策略与审计同步。
  - [x] 5.7 测试：准入状态机、黑名单拦截、信用超限 409、资质到期任务（时间注入）、审批回写幂等；构建 + 全量测试。

- [x] Task 6: 人员主数据
  - [x] 6.1 领域实体：`Person`（`Code`、`Name`、`PersonType`、`IdType`、`IdNoCipher`、`IdNoHash`、`MobileCipher`、`EmergencyContact/Phone`、`OrgId`、`PositionId`、`EmployStatus`、`EntryDate`、`LeaveDate`、`BankCardNoCipher`、`IsDeleted`）、`PersonAssignment`、`PersonCert`；`IdNoHash` 建唯一索引实现防重。
  - [x] 6.2 EF 迁移：`bas_persons`/`bas_person_assignments`/`bas_person_certs` + 索引。
  - [x] 6.3 服务：编码 `EMP-`/`LAB-` 经 `IBasCodeNumberGenerator`（规则 `employee`/`labour`，按人员类型选择）、证件号格式校验（复用 `CommonValidationPatterns`）、加密/解密（`IDataProtectionProvider` + D5 purpose）、脱敏规则、离职/再入职（复用 personId 新增任职记录）、岗位在岗数回写；写路径 `IAuditService.CaptureChangeAsync` 快照中敏感字段一律脱敏值。
  - [x] 6.4 新增 `Controllers/PersonController.cs`（列表、详情、增改、离职、再入职、明文查看），明文端点挂 `bas.person.field.plaintext` 权限并写审计。
  - [x] 6.5 测试：证件防重、脱敏输出、再入职、离职回写在岗数、明文越权 403；构建 + 全量测试。

- [x] Task 7: 字典、编码规则、导入导出与变更日志
  - [x] 7.1 领域实体：`DictType`（`TypeCode`、`TypeName`、`OwnerLevel`、`OrgId`、`Status`、`IsSystem`）、`DictItem`（`TypeCode`、`ItemCode`、`ItemName`、`SortNo`、`Status`、`IsSystem`）；迁移 + 唯一索引。
  - [x] 7.2 服务：两级字典可见性（集团级只读 + 本子公司级）、系统内置项保护、被引用项仅可停用；新增 `Controllers/DictionaryController.cs`。
  - [x] 7.3 编码规则页：`CodeRule` 实体已在 Task 3.3 落地；本任务实现规则查询/启停与分段模板语法校验（`{fixed}`/`{yyyyMM}`/`{seq:N}`，非法 422），规则本身不可修改；新增 `Controllers/CodeRuleController.cs`。
  - [x] 7.4 导入导出：`BuildingBlocks` 增加 `IImportTemplateDefinitionProvider` 与 `CompositeImportTemplateRegistry`（由 `AddQlxBas()` 先行注册，见 1.4）；Bas 注册 4 个模板（供应商/客户/人员/分类）、对应 `IImportRowSink` 与 `IExportDataProvider`，复用 `IImportExportService`；**落点内用 `GuidIdMapper.TryToLong(tenantId, out tid)` 还原租户并交叉校验 `ITenantContext.TenantId`**；新增 `Controllers/ImportExportController.cs`（模板下载、导入、导出）。
  - [x] 7.5 变更日志：新增 `Controllers/ChangeLogController.cs`，经 `IAuditService.QueryChangesAsync` 查询 `plog_audit`（对象类型/实体 Id/操作人/时间），服务层条数上限 500 + 页码切片；快照敏感字段读取时脱敏；不新建日志表、不直连 `AuditDbContext`。
  - [x] 7.6 测试：字典两级可见性、系统项保护、导入行级错误、模板组合注册（多 provider 并存 + 空实现兜底不回归）、`Guid→long` 租户还原校验、变更日志过滤与脱敏、`bas.cache.cleanup` 清理前缀；构建 + 全量测试。

- [x] Task 8: 前端模块 01 页面与契约对接
  - [x] 8.1 类型与 API：新增 `src/Qlx.Web/src/types/bas-master.ts`、`src/Qlx.Web/src/api/basMaster.ts`（统一经 `@/core/http`，按 `ApiResponse` 解包，非成功抛错）。
  - [x] 8.2 组织页增强：`TenantOrganizationView.vue` + `components/org-admin/*` 支持编码只读、四级类型（D2）、人员负责人选择、生效/失效日期、版本历史抽屉、调整审批状态（复用 `components/approval`）。
  - [x] 8.3 主数据页面：新增 `views/master/` 下商品分类、计量单位、供应商、客户、人员、字典、编码规则、变更日志页面；复用 `QlxDataTable`/`QlxPagePanel`/`QlxFormDialog`/`QlxTreePanel`/`QlxImportDialog`，树组装与枚举映射在前端公共层。
  - [x] 8.4 路由与菜单：在 `router/index.ts` 的 `/manage` 下挂载 `/manage/master/*` 子路由；同步 `menuStore` 消费后端菜单，按 `requiredPermissionCode` 裁剪。
  - [x] 8.5 测试与构建：新增 vitest（树组装、类型映射、formatters、API 解包）；`npx vitest run` ≥176 全绿、`npm run build` 0 错误。

- [x] Task 9: Host 冒烟与浏览器闭环
  - [x] 9.1 停止 Host 进程后重建，`dotnet run --project src/Qlx.Host` 启动成功、`/health` 200、Manifest 同步与迁移日志正常。
  - [x] 9.2 冒烟脚本/手工验证：组织新增→审批→生效→版本回看；供应商准入→资质预警任务→冻结；客户额度超限→审批；人员新增→脱敏→明文授权；分类新增→停用审批；字典/编码规则/变更日志查询；确认存量租户首次发起 Bas 审批时 7 个流程已自动补种。
  - [x] 9.3 浏览器闭环：登录租户 → 菜单可见 → 8 个页面主路径操作 → 权限裁剪验证；截图存 `docs/test-shots/bas-<日期>/`（`docs/test-shots/bas-20261025/` 共 42 张，含缺陷复测 `41`~`44`）。
  - [x] 9.4 验证：后端/前端全量测试与构建再次全绿（后端 1238+20，前端 23 文件 235，`npm run build` 0 错误）。

- [x] Task 11: 修复审批人 Id 混淆缺陷（Task 9.2/9.3 失败项）
  - [x] 11.1 `BasApprovalApproverSource` 注入 `IIdObfuscator`，ROLE / DEPT_LEADER 解析结果统一 `Encode(PortalUserId)`，与 `ICurrentUserContext.UserId`（token sub claim，Hashids 混淆串）编码一致。
  - [x] 11.2 单元测试覆盖「解析结果与当前用户 Id 同编码」，并验证审批待办可按当前用户命中（不得以原始 long 落 `ApproverId`）。
  - [x] 11.3 验证：构建 + 后端全量测试全绿。

- [x] Task 12: 修复字典两级可见性缺陷（Task 9.2/9.3 失败项）
  - [x] 12.1 `DictionaryService.ListTypesAsync` 区分「TenantAdmin 全范围（scope=null）」与「成员有范围但无 OrgId」：前者不过滤 ownerLevel，后者子公司级不可见；缓存键含范围标记避免跨范围污染。
  - [x] 12.2 `EnsureVisibleAsync` 对 scope=null 放行（租户管理员可见全部子公司级字典）。
  - [x] 12.3 单元测试覆盖：租户管理员可见集团级 + 全部子公司级；子公司成员仅见集团级 + 本组织级；其他子公司级不可见。
  - [x] 12.4 验证：构建 + 后端全量测试全绿。

- [x] Task 13: 修复人员表单员工必填组织/岗位前端校验（Task 9.2/9.3 失败项）
  - [x] 13.1 `QlxFormDialog.FormField` 增加条件必填 `requiredWhen`（基于当前表单值），规则随表单值响应式重算。
  - [x] 13.2 `PersonMasterView` 新增/编辑表单中 `personType === 1`（员工）时 `orgId`/`positionId` 必填，劳务人员不受限。
  - [x] 13.3 前端单测覆盖条件必填判定；`npx vitest run` 全绿、`npm run build` 0 错误。

- [x] Task 14: 修复 Outbox 事件消费者跨程序集派发失败（复测中新发现）
  - [x] 14.1 根因：`OutboxDispatcher` 以 `dynamic` 调用 `ConsumeAsync`，Bas 的 `ApprovalCompletedEventConsumer` 为 `internal`，运行时绑定器降级为 `object` → `RuntimeBinderException`，`ApprovalCompletedEvent` 恒投递失败，审批通过后供应商状态不生效。
  - [x] 14.2 将 `ApprovalCompletedEventConsumer` 改为 `public`（附注释说明约束）。
  - [x] 14.3 架构测试新增守护：模块程序集中所有 `IIntegrationEventConsumer<>` 实现类必须 `public`。
  - [x] 14.4 验证：构建 + 后端全量测试全绿（1238+20）；重启 Host 后重放完整准入审批链，`bas_approval_requests` id=4 置 `status=2` + `applied_at` 非空、供应商 `SUP-000001` 状态 1→2（ACTIVE/合作中）。

- [x] Task 10: 文档回写与出口验收
  - [x] 10.1 回写 `docs/项目资产登记台账-20261024.md`（代码/测试/配置资产增量、遗留项状态）——新增 §9 本批次回写。
  - [x] 10.2 回写 `docs/下一阶段工作准备与推进计划-20261024.md` §三 M-3.1 状态与 §六准备清单（M-3.1 ✅ / S-1、S-5、S-6 打勾）。
  - [x] 10.3 在 `docs/开发推进计划.md` 模块 01 标记完成并记录偏差（D2/D3/D6/D7/D8，其中 D3 附 N-1 偏差会签结论）。
  - [x] 10.4 输出批次实施记录（含测试增量、冒烟与截图证据、遗留项）：`docs/阶段三第一波-模块01-批次实施记录-20261025.md`。

# Task Dependencies

- Task 2 依赖 Task 1（Manifest/DI 骨架）。
- Task 3 依赖 Task 2（查询契约与事件契约）与 Task 1.7（审批种子可解析，否则 3.5 无法联调）。
- Task 4、Task 5 依赖 Task 2 与 Task 3.3（统一发号器）；Task 5 另依赖 Task 3（组织/岗位引用）。
- Task 7 依赖 Task 1、Task 2；Task 7.3 依赖 Task 3.3（`CodeRule` 实体）；Task 7.4 依赖 Task 4~6 的实体（作为导入落点）。
- Task 8 依赖 Task 3~7 后端端点可用。
- Task 9 依赖 Task 8。
- Task 10 依赖 Task 9。
