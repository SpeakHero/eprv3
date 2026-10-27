# Checklist

> 验收结论（2026-10-25）：全部条目通过。证据见 `docs/阶段三第一波-模块01-批次实施记录-20261025.md` 与 `docs/test-shots/bas-20261025/`。

## 一、工程规范与架构边界

- [x] Bas 模块仅引用 `Qlx.Abstractions` + `Qlx.BuildingBlocks`，无模块间项目引用
- [x] 新实体全部继承 `AuditableEntity` + `ITenantEntity`，主键统一 `long`（沿用 Bas 既有 IDENTITY 自增约定，不调用 `SnowflakeKeyConvention`），行为方法封装状态变更
- [x] 表名全部带 `bas_` 前缀，列名 snake_case 由 `TenantDbContext` 统一转换
- [x] `BasModule.Manifest` 完整声明权限点、菜单、任务、订阅事件，且 ModuleCode/TablePrefix 来自 `QlxModuleCodes`
- [x] `BasEndpointPolicyMap` 覆盖全部新增控制器与动作，无遗漏动作级权限点
- [x] `tests/Qlx.ArchitectureTests` 全部通过（引用图 / 隔离 / 缓存守护，20 用例全绿）

## 二、领域与数据

- [x] 组织类型按 D2 重定义并完成数据迁移（1→1、2→3、3→3，负责人置空），迁移后校验通过（`Type` 仅 1~4、无重复 `TenantId+Code`），回滚脚本随迁移提交
- [x] 组织版本快照在新增/调级/停用/合并时生成，按日期回看返回正确快照
- [x] 岗位在岗人数来自人员任职记录汇总，停用前置在岗=0，超编走 `bas.position.overstaff`
- [x] 商品分类层级编码连续且并发唯一，层级上限读 `por_parameters.bas.category.maxLevel`
- [x] 计量单位组/单位/换算率完整，换算率变更写审计且不回溯历史单据
- [x] 供应商/客户主表与 7 张子表齐备，`TenantId+Code`、`TenantId+Uscc` 唯一约束生效
- [x] 人员敏感字段密文存储（D5 purpose），`IdNoHash` 唯一索引实现证件防重
- [x] 人员离职/再入职流程复用 personId 并新增任职记录，岗位在岗数同步

## 三、编码与职责边界

- [x] 组织/供应商/客户/人员编码由 Bas 统一发号器 `IBasCodeNumberGenerator`（`bas_code_rules` 驱动）生成，未使用 `IDocumentNumberGenerator`，前端无编码输入框
- [x] 岗位编码由 `IEncodingGenerator`（`pos-` 前缀）生成，冲突自动追加序号
- [x] 分类编码按层级生成（父编码 + 2 位流水），未采用前端传码或散装拼号
- [x] 流水号经 `ISerialSequenceStore` 原子取号，序号键含租户段，并发创建无重号
- [x] 后端响应 DTO 全部为具名 record，无匿名对象、无树形嵌套、无展示拼装/枚举中文映射
- [x] 业务 Id 全部经全局 Hashids 混淆边界下发，DTO 未手工编解码

## 四、缓存、校验与安全

- [x] 只读主数据查询全部经 `ICacheService.GetOrCreateAsync`，键经 `QlxCacheKey.BuildTenantKey(tenantId, "bas", ...)`
- [x] 写路径只删缓存不写缓存，使用 `RemoveByPrefixAsync`/`RemoveAsync`；TTL 5 分钟且未关抖动；不存在结果落 `NullMarker`（30 秒）
- [x] `bas.cache.cleanup` 任务已注册并只清当前租户 `tenant:{tenantId}:bas:` 前缀，执行结果落 `job_executions`
- [x] 后端对前端提交执行独立完整校验（DataAnnotations + 业务规则），不信任前端校验
- [x] 敏感字段默认脱敏，明文查看需 `bas.person.field.plaintext` 且写审计
- [x] 无缓存提供方 SDK 直引，无散装缓存键字符串

## 五、事件、审批与任务

- [x] `OrganizationCreatedEvent`、`SupplierApprovedEvent`、`CreditLineAdjustedEvent` 经 `PublishDurableAsync` 投递
- [x] `ApprovalCompletedEvent` 消费按 BizType 过滤且幂等（同一实例重复到达不重复生效）；消费者实现类必须 `public`（架构测试守护）
- [x] 7 个审批定义（`bas.org.adjust` 等）在 Support 种子中可幂等创建，节点审批人统一 `ROLE=admin`
- [x] `ApprovalFlowRegistry.ResolveEnabledAsync` 在本 bizType 零命中时补种一次后重查，存量租户首次发起 Bas 审批可解析到流程
- [x] 审批阈值均读 `ISystemParameterProvider`，无硬编码阈值
- [x] `bas.supplier.cert.expiry` 任务实现 `IJobHandler`，分批扫描、幂等，并落任务执行日志

## 六、导入导出与变更日志

- [x] `IImportTemplateRegistry` 已组合化（`IImportTemplateDefinitionProvider` + `CompositeImportTemplateRegistry`），由 `AddQlxBas()` 先行注册，多 provider 可共存且无 Bas 宿主仍回落空实现
- [x] 供应商/客户/人员/分类 4 个导入模板可下载、可导入，错误行返回行号与原因
- [x] 导入落点批量写入，无逐行数据库往返；落点内 `Guid→long` 租户还原与 `ITenantContext` 交叉校验生效
- [x] 变更日志写入经 `IAuditService.CaptureChangeAsync`（事务提交后显式调用），查询经 `QueryChangesAsync` 过滤对象类型/实体 Id/操作人/时间，敏感字段脱敏
- [x] 未新建变更日志表，未直连 `AuditDbContext`，未在 `plog_audit` 上做编码字符串扫描

## 七、前端

- [x] `/manage/organizations` 支持编码只读、四级类型、人员负责人、生效/失效日期、版本历史、审批状态
- [x] 8 个主数据页面路由可达、菜单按权限裁剪、按钮级权限生效
- [x] 树组装、枚举映射、显示格式化全部在前端公共层，未在后端实现
- [x] 表单提交前 UX 校验与后端 422 错误提示均可见（含人员「员工」类型组织/岗位条件必填）
- [x] `npx vitest run` 23 文件 235 全绿（≥176），`npm run build` 0 错误

## 八、验收与回写

- [x] `dotnet build Qlx.Erpv3.slnx` 0 警告 0 错误（构建前已停 Host）
- [x] `dotnet test` 1258 全绿（BuildingBlocks 1238 + Architecture 20），无跳过用例
- [x] Host 启动成功、`/health` 200、Manifest 同步与租户迁移日志正常
- [x] 浏览器闭环完成并留存截图证据（`docs/test-shots/bas-20261025/`，42 张）
- [x] `docs/项目资产登记台账-20261024.md`、`docs/下一阶段工作准备与推进计划-20261024.md`、`docs/开发推进计划.md` 已回写状态与偏差（D2/D3/D6/D7/D8，D3 附 N-1 会签结论）
