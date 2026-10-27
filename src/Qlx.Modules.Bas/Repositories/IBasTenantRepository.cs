/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.2.0 beta
 * 创建时间：2026-10-21
 * 修改时间：2026-10-25 23:55:00 +08:00 中国标准时间 星期日
 * 描述：Bas 模块仓储接口——纯数据访问层，禁止业务逻辑。
 */

using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Domain.Dictionary;
using Qlx.Modules.Bas.Domain.Goods;
using Qlx.Modules.Bas.Domain.Numbering;

namespace Qlx.Modules.Bas.Repositories;

/// <summary>
/// Bas 租户域仓储——封装对 BasTenantDbContext 的访问。
/// 读路径统一 AsNoTracking；写路径 FindTracked 保持跟踪态。
/// </summary>
internal interface IBasTenantRepository
{
    // === Organization ===
    Task<IReadOnlyList<Organization>> ListAllOrgsAsync(CancellationToken ct);
    Task<Organization?> FindOrgAsync(long id, CancellationToken ct);
    Task<bool> IsOrgCodeExistsAsync(string code, CancellationToken ct);
    Task AddOrgAsync(Organization org, CancellationToken ct);
    Task UpdateOrgAsync(Organization org, CancellationToken ct);
    Task RemoveOrgAsync(Organization org, CancellationToken ct);
    Task<bool> HasChildOrgsAsync(long parentId, CancellationToken ct);
    Task<bool> HasMembersUnderOrgAsync(long orgId, CancellationToken ct);

    // === OrgNodeHistory（组织版本快照）===
    Task<IReadOnlyList<OrgNodeHistory>> ListOrgHistoriesAsync(long orgId, CancellationToken ct);
    Task<IReadOnlyList<OrgNodeHistory>> ListAllOrgHistoriesAsync(CancellationToken ct);
    Task AddOrgHistoryAsync(OrgNodeHistory history, CancellationToken ct);

    // === BasApprovalRequest（审批挂起请求）===
    Task<BasApprovalRequest?> FindApprovalRequestAsync(long id, CancellationToken ct);
    Task<BasApprovalRequest?> FindApprovalRequestByInstanceAsync(string approvalInstanceId, CancellationToken ct);
    Task AddApprovalRequestAsync(BasApprovalRequest request, CancellationToken ct);

    // === CodeRule（编码规则）===
    Task<CodeRule?> FindCodeRuleAsync(string bizType, CancellationToken ct);
    Task AddCodeRuleAsync(CodeRule rule, CancellationToken ct);

    /// <summary>编码规则扁平列表（业务类型 / 启停状态筛选，按业务类型排序）。</summary>
    Task<IReadOnlyList<CodeRule>> ListCodeRulesAsync(string? bizType, int? status, CancellationToken ct);
    Task UpdateCodeRuleAsync(CodeRule rule, CancellationToken ct);

    // === Dictionary（数据字典类型 / 字典项）===
    /// <summary>
    /// 字典类型两级可见性查询（SQL 层过滤）：集团级全租户可见，子公司级仅 <paramref name="orgId"/> 可见。
    /// </summary>
    /// <param name="unrestricted">true 表示不做可见性过滤（TenantAdmin 全范围 / 无成员上下文的门户模式）；
    /// false 时按 <paramref name="orgId"/> 两级过滤（子公司级仅本组织可见）。</param>
    /// <param name="orgId">当前成员主属组织 Id（无组织时为 null）。</param>
    /// <param name="ownerLevel">按归属层级筛选（null=不限）。</param>
    /// <param name="status">按启停状态筛选（null=不限）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<DictType>> QueryDictTypesAsync(
        bool unrestricted, long? orgId, int? ownerLevel, int? status, CancellationToken ct);
    Task<DictType?> FindDictTypeAsync(long id, CancellationToken ct);
    Task<DictType?> FindDictTypeByCodeAsync(string typeCode, CancellationToken ct);
    Task<bool> IsDictTypeCodeExistsAsync(string typeCode, long? excludeId, CancellationToken ct);
    Task AddDictTypeAsync(DictType type, CancellationToken ct);
    Task UpdateDictTypeAsync(DictType type, CancellationToken ct);

    /// <summary>按类型编码取字典项扁平列表（状态筛选，按排序号升序）。</summary>
    Task<IReadOnlyList<DictItem>> QueryDictItemsAsync(string typeCode, int? status, CancellationToken ct);
    Task<DictItem?> FindDictItemAsync(long id, CancellationToken ct);
    Task<bool> IsDictItemCodeExistsAsync(
        string typeCode, string itemCode, long? excludeId, CancellationToken ct);
    Task AddDictItemAsync(DictItem item, CancellationToken ct);
    Task UpdateDictItemAsync(DictItem item, CancellationToken ct);

    /// <summary>硬删字典项（仅非系统内置且未被业务引用时由服务层调用）。</summary>
    Task RemoveDictItemAsync(DictItem item, CancellationToken ct);

    // === Position ===
    /// <summary>全量岗位扁平列表（只读查询缓存回源用，禁 N+1）。</summary>
    Task<IReadOnlyList<Position>> ListAllPositionsAsync(CancellationToken ct);

    /// <summary>岗位分页查询；orgIds 为组织子树 Id 集合（null/空 表示不限组织）。</summary>
    Task<(IReadOnlyList<Position> Items, int Total)> QueryPositionsAsync(
        IReadOnlyCollection<long>? orgIds, string? keyword, int skip, int take, CancellationToken ct);

    /// <summary>查询指定组织集合下全部启用岗位（组织停用前置校验用）。</summary>
    Task<IReadOnlyList<Position>> ListEnabledPositionsByOrgIdsAsync(IReadOnlyCollection<long> orgIds, CancellationToken ct);
    Task<Position?> FindPositionAsync(long id, CancellationToken ct);

    /// <summary>按 Id 批量取岗位（跟踪态，在岗数回写用；单条 IN 查询，禁 N+1）。</summary>
    Task<IReadOnlyList<Position>> ListPositionsByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct);
    Task<bool> IsPositionCodeExistsAsync(string code, long? excludeId, CancellationToken ct);
    Task AddPositionAsync(Position position, CancellationToken ct);
    Task UpdatePositionAsync(Position position, CancellationToken ct);
    Task RemovePositionAsync(Position position, CancellationToken ct);

    // === Goods：商品分类 ===
    Task<IReadOnlyList<ItemCategory>> ListAllCategoriesAsync(CancellationToken ct);
    Task<ItemCategory?> FindCategoryAsync(long id, CancellationToken ct);
    Task<bool> IsCategoryCodeExistsAsync(string code, CancellationToken ct);
    Task<bool> HasChildCategoriesAsync(long parentId, CancellationToken ct);
    Task AddCategoryAsync(ItemCategory category, CancellationToken ct);
    Task UpdateCategoryAsync(ItemCategory category, CancellationToken ct);

    // === Goods：计量单位组 ===
    Task<IReadOnlyList<UnitGroup>> ListAllUnitGroupsAsync(CancellationToken ct);
    Task<UnitGroup?> FindUnitGroupAsync(long id, CancellationToken ct);
    Task<bool> IsUnitGroupCodeExistsAsync(string code, CancellationToken ct);
    Task AddUnitGroupAsync(UnitGroup group, CancellationToken ct);
    Task UpdateUnitGroupAsync(UnitGroup group, CancellationToken ct);

    // === Goods：计量单位 ===
    Task<IReadOnlyList<Unit>> ListAllUnitsAsync(CancellationToken ct);
    Task<Unit?> FindUnitAsync(long id, CancellationToken ct);
    Task<bool> IsUnitCodeExistsAsync(string code, CancellationToken ct);
    Task<int> CountUnitsInGroupAsync(long unitGroupId, CancellationToken ct);
    Task AddUnitAsync(Unit unit, CancellationToken ct);
    Task UpdateUnitAsync(Unit unit, CancellationToken ct);

    // === Goods：单位换算 ===
    Task<IReadOnlyList<UnitConversion>> ListAllUnitConversionsAsync(CancellationToken ct);
    Task<UnitConversion?> FindUnitConversionAsync(long id, CancellationToken ct);
    Task<bool> IsUnitConversionExistsAsync(long fromUnitId, long toUnitId, long? excludeId, CancellationToken ct);
    Task AddUnitConversionAsync(UnitConversion conversion, CancellationToken ct);
    Task UpdateUnitConversionAsync(UnitConversion conversion, CancellationToken ct);

    // === Role ===
    Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken ct);

    /// <summary>角色列表 + 成员数/权限点聚合计数（单次往返，相关标量子查询投影，禁 N+1）。</summary>
    Task<IReadOnlyList<(Role Role, int MemberCount, int PermissionCount)>> ListRolesWithCountsAsync(CancellationToken ct);

    /// <summary>单角色成员数/权限点计数（更新回包用，两条 COUNT）。</summary>
    Task<(int MemberCount, int PermissionCount)> GetRoleCountsAsync(long roleId, CancellationToken ct);

    Task<Role?> FindRoleAsync(long id, CancellationToken ct);
    Task<bool> IsRoleCodeExistsAsync(string code, long? excludeId, CancellationToken ct);
    Task AddRoleAsync(Role role, CancellationToken ct);
    Task UpdateRoleAsync(Role role, CancellationToken ct);
    Task RemoveRoleAsync(Role role, CancellationToken ct);

    // === RolePermission ===
    Task<IReadOnlyList<string>> ListRolePermissionCodesAsync(long roleId, CancellationToken ct);
    Task ReplaceRolePermissionsAsync(long roleId, long tenantId, IReadOnlyList<string> codes, CancellationToken ct);

    // === MemberRole ===
    Task<IReadOnlyList<long>> ListRoleMemberIdsAsync(long roleId, CancellationToken ct);
    Task<IReadOnlyList<string>> ListMemberRoleCodesAsync(long tenantMemberId, CancellationToken ct);

    /// <summary>查询成员关联的全部角色实体（含 DefaultDataScope；X-5 数据范围解析用）。</summary>
    Task<IReadOnlyList<Role>> ListMemberRolesAsync(long tenantMemberId, CancellationToken ct);
    Task ReplaceMemberRolesAsync(long tenantMemberId, long tenantId, IReadOnlyList<(long RoleId, string RoleCode)> roles, CancellationToken ct);
    Task RemoveMemberRoleLinksAsync(long tenantMemberId, CancellationToken ct);
    Task RemoveMemberRoleLinksByRoleAsync(long roleId, CancellationToken ct);
    Task ReplaceRoleMemberLinksAsync(long roleId, IReadOnlyList<long> tenantMemberIds, long tenantId, CancellationToken ct);
    Task<IReadOnlyList<(long TenantMemberId, long RoleId, string RoleCode)>> ListAllMemberRoleCodesFlatAsync(long tenantId, CancellationToken ct);

    // === Supplier（供应商主表 + 子表）===
    /// <summary>供应商分页查询（评级 / 状态 / 关键字筛选，按编码排序；列表不缓存）。</summary>
    Task<(IReadOnlyList<Domain.Supplier.Supplier> Items, int Total)> QuerySuppliersAsync(
        int? grade, int? status, string? keyword, int skip, int take, CancellationToken ct);
    Task<Domain.Supplier.Supplier?> FindSupplierAsync(long id, CancellationToken ct);

    /// <summary>合作中（ACTIVE）供应商扁平列表（跨模块查询回源用，禁 N+1）。</summary>
    Task<IReadOnlyList<Domain.Supplier.Supplier>> ListActiveSuppliersAsync(CancellationToken ct);

    Task<IReadOnlyList<Domain.Supplier.Supplier>> ListSuppliersByIdsAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct);
    Task<bool> IsSupplierCodeExistsAsync(string code, CancellationToken ct);
    Task<bool> IsSupplierUsccExistsAsync(string uscc, long? excludeId, CancellationToken ct);
    Task AddSupplierAsync(Domain.Supplier.Supplier supplier, CancellationToken ct);
    Task UpdateSupplierAsync(Domain.Supplier.Supplier supplier, CancellationToken ct);

    Task<IReadOnlyList<Domain.Supplier.SupplierCert>> ListSupplierCertsAsync(long supplierId, CancellationToken ct);
    Task<Domain.Supplier.SupplierCert?> FindSupplierCertAsync(long id, CancellationToken ct);
    Task AddSupplierCertAsync(Domain.Supplier.SupplierCert cert, CancellationToken ct);
    Task UpdateSupplierCertAsync(Domain.Supplier.SupplierCert cert, CancellationToken ct);

    /// <summary>资质到期扫描批次（键集分页：仅有效证照，按 Id 升序，禁 N+1）。</summary>
    Task<IReadOnlyList<Domain.Supplier.SupplierCert>> ListSupplierCertsForScanAsync(
        long afterId, int take, CancellationToken ct);

    Task<IReadOnlyList<Domain.Supplier.SupplierBank>> ListSupplierBanksAsync(long supplierId, CancellationToken ct);
    Task AddSupplierBankAsync(Domain.Supplier.SupplierBank bank, CancellationToken ct);
    Task UpdateSupplierBankAsync(Domain.Supplier.SupplierBank bank, CancellationToken ct);
    Task<IReadOnlyList<Domain.Supplier.SupplierInvoice>> ListSupplierInvoicesAsync(long supplierId, CancellationToken ct);
    Task AddSupplierInvoiceAsync(Domain.Supplier.SupplierInvoice invoice, CancellationToken ct);
    Task<IReadOnlyList<Domain.Supplier.SupplierGradeLog>> ListSupplierGradeLogsAsync(long supplierId, CancellationToken ct);
    Task AddSupplierGradeLogAsync(Domain.Supplier.SupplierGradeLog log, CancellationToken ct);

    // === Customer（客户主表 + 子表）===
    /// <summary>客户分页查询（信用等级 / 状态 / 关键字筛选，按编码排序；列表不缓存）。</summary>
    Task<(IReadOnlyList<Domain.Customer.Customer> Items, int Total)> QueryCustomersAsync(
        int? creditGrade, int? status, string? keyword, int skip, int take, CancellationToken ct);
    Task<Domain.Customer.Customer?> FindCustomerAsync(long id, CancellationToken ct);
    Task<bool> IsCustomerCodeExistsAsync(string code, CancellationToken ct);
    Task<bool> IsCustomerUsccExistsAsync(string uscc, long? excludeId, CancellationToken ct);
    Task AddCustomerAsync(Domain.Customer.Customer customer, CancellationToken ct);
    Task UpdateCustomerAsync(Domain.Customer.Customer customer, CancellationToken ct);

    Task<IReadOnlyList<Domain.Customer.CustomerBank>> ListCustomerBanksAsync(long customerId, CancellationToken ct);
    Task AddCustomerBankAsync(Domain.Customer.CustomerBank bank, CancellationToken ct);
    Task UpdateCustomerBankAsync(Domain.Customer.CustomerBank bank, CancellationToken ct);
    Task<IReadOnlyList<Domain.Customer.CustomerInvoice>> ListCustomerInvoicesAsync(long customerId, CancellationToken ct);
    Task AddCustomerInvoiceAsync(Domain.Customer.CustomerInvoice invoice, CancellationToken ct);
    Task<IReadOnlyList<Domain.Customer.CustomerCreditLog>> ListCustomerCreditLogsAsync(
        long customerId, CancellationToken ct);
    Task AddCustomerCreditLogAsync(Domain.Customer.CustomerCreditLog log, CancellationToken ct);

    // === Person（人员主表 + 任职记录 + 证件档案）===
    /// <summary>人员分页查询（类型 / 在职状态 / 组织 / 岗位 / 关键字筛选，按编码排序；列表不缓存）。</summary>
    Task<(IReadOnlyList<Domain.Person.Person> Items, int Total)> QueryPersonsAsync(
        int? personType,
        int? employStatus,
        long? orgId,
        long? positionId,
        string? keyword,
        int skip,
        int take,
        CancellationToken ct);
    Task<Domain.Person.Person?> FindPersonAsync(long id, CancellationToken ct);
    Task<bool> IsPersonCodeExistsAsync(string code, CancellationToken ct);

    /// <summary>证件号哈希租户内防重（软删过滤由全局过滤自动生效）。</summary>
    Task<bool> IsPersonIdNoExistsAsync(string idNoHash, long? excludeId, CancellationToken ct);
    Task AddPersonAsync(Domain.Person.Person person, CancellationToken ct);
    Task UpdatePersonAsync(Domain.Person.Person person, CancellationToken ct);

    /// <summary>指定岗位下在职（非离职）人员扁平列表（跨模块查询回源 / 岗位在岗统计用，禁 N+1）。</summary>
    Task<IReadOnlyList<Domain.Person.Person>> ListActivePersonsByPositionAsync(
        long positionId, CancellationToken ct);

    /// <summary>按岗位批量统计在职（非离职）人数（单条 GROUP BY，禁 N+1）。</summary>
    Task<IReadOnlyDictionary<long, int>> CountActivePersonsByPositionsAsync(
        IReadOnlyCollection<long> positionIds, CancellationToken ct);

    /// <summary>组织集合（含子树）下是否存在在职人员（组织停用前置校验用）。</summary>
    Task<bool> HasActivePersonsUnderOrgsAsync(IReadOnlyCollection<long> orgIds, CancellationToken ct);

    /// <summary>人员任职记录（跟踪态：调岗/离职关闭当前记录后统一保存）。</summary>
    Task<IReadOnlyList<Domain.Person.PersonAssignment>> ListPersonAssignmentsAsync(
        long personId, CancellationToken ct);
    Task AddPersonAssignmentAsync(Domain.Person.PersonAssignment assignment, CancellationToken ct);
    Task UpdatePersonAssignmentAsync(Domain.Person.PersonAssignment assignment, CancellationToken ct);

    Task<IReadOnlyList<Domain.Person.PersonCert>> ListPersonCertsAsync(long personId, CancellationToken ct);
    Task<Domain.Person.PersonCert?> FindPersonCertAsync(long id, CancellationToken ct);
    Task AddPersonCertAsync(Domain.Person.PersonCert cert, CancellationToken ct);
    Task UpdatePersonCertAsync(Domain.Person.PersonCert cert, CancellationToken ct);
    Task RemovePersonCertAsync(Domain.Person.PersonCert cert, CancellationToken ct);

    // === 导入导出（批量校验 / 批量读取，禁逐行往返）===

    /// <summary>批量查询已存在的供应商统一社会信用代码（单条 IN 查询）。</summary>
    Task<IReadOnlyCollection<string>> ListExistingSupplierUsccsAsync(
        IReadOnlyCollection<string> usccs, CancellationToken ct);

    /// <summary>批量查询已存在的客户统一社会信用代码（单条 IN 查询）。</summary>
    Task<IReadOnlyCollection<string>> ListExistingCustomerUsccsAsync(
        IReadOnlyCollection<string> usccs, CancellationToken ct);

    /// <summary>批量查询已存在的人员证件号哈希（单条 IN 查询）。</summary>
    Task<IReadOnlyCollection<string>> ListExistingPersonIdNoHashesAsync(
        IReadOnlyCollection<string> idNoHashes, CancellationToken ct);

    /// <summary>导出用供应商全量扁平列表（状态 / 关键字筛选，按编码排序，单次查询）。</summary>
    Task<IReadOnlyList<Domain.Supplier.Supplier>> ListSuppliersForExportAsync(
        int? status, string? keyword, CancellationToken ct);

    /// <summary>导出用客户全量扁平列表（状态 / 关键字筛选，按编码排序，单次查询）。</summary>
    Task<IReadOnlyList<Domain.Customer.Customer>> ListCustomersForExportAsync(
        int? status, string? keyword, CancellationToken ct);

    /// <summary>导出用人员全量扁平列表（类型 / 在职状态 / 关键字筛选，按编码排序，单次查询）。</summary>
    Task<IReadOnlyList<Domain.Person.Person>> ListPersonsForExportAsync(
        int? personType, int? employStatus, string? keyword, CancellationToken ct);

    // === 通用 ===
    Task<int> SaveChangesAsync(CancellationToken ct);

    /// <summary>
    /// 在数据库事务内执行（已处于事务中时直接复用外层事务，支持嵌套调用）。
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);

    // === 登录日志（租户库 log_login，TenantLogDbContext 全局 TenantId 过滤） ===

    /// <summary>按登录账号分页查询登录日志（按雪花 EventId 倒序，与 LoginAt 倒序语义等价）。</summary>
    Task<(IReadOnlyList<Qlx.BuildingBlocks.Support.Logs.TenantLogLoginEntity> Items, int Total)> QueryLoginLogsAsync(
        string loginAccount, int skip, int take, CancellationToken ct);
}
