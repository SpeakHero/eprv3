/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-26 02:30:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 02:35:00 +08:00 中国标准时间 星期一
 * 描述：阶段三「数据字典 / 编码规则 / 导入导出 / 变更日志」测试——两级可见性、集团级写保护、
 *       系统内置项保护、编码模板校验与 ValidationException、模板组合注册、导入行号错误与租户校验、
 *       导出脱敏含编码、变更日志上限/过滤/脱敏、缓存前缀清理、模板级权限二次校验
 */

using System.Security.Claims;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.ImportExport;
using Qlx.Abstractions.Jobs;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.ImportExport;
using Qlx.BuildingBlocks.Numbering;
using Qlx.BuildingBlocks.Support.Logs;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.Modules.Bas;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Domain.Dictionary;
using Qlx.Modules.Bas.Domain.Goods;
using Qlx.Modules.Bas.Domain.Numbering;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Jobs;
using Qlx.Modules.Bas.Repositories;
using Qlx.Modules.Bas.Services;
using Qlx.Modules.Bas.Services.ImportExport;
using Qlx.Modules.Bas.Services.Numbering;

using PersonEntity = Qlx.Modules.Bas.Domain.Person.Person;
using PersonIdType = Qlx.Modules.Bas.Domain.Person.PersonIdType;
using PersonType = Qlx.Modules.Bas.Domain.Person.PersonType;
using SupplierEntity = Qlx.Modules.Bas.Domain.Supplier.Supplier;
using SupplierType = Qlx.Modules.Bas.Domain.Supplier.SupplierType;

namespace Qlx.BuildingBlocks.Tests.Bas;

/// <summary>
/// 数据字典 / 编码规则 / 导入导出 / 变更日志测试（服务规则 + 行级校验 + 权限二次校验 + 缓存清理）。
/// </summary>
public sealed class DictionaryCodeRuleImportTests
{
    private const long TenantId = 42L;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Harness
    {
        public required BasTenantDbContext Db { get; init; }

        public required BasTenantRepository Repo { get; init; }

        public required MemoryCacheService Cache { get; init; }

        public required InMemoryAuditService Audit { get; init; }

        public required StubCurrentMemberDataScopeResolver Scope { get; init; }

        public required DictionaryService Dictionary { get; init; }

        public required CodeRuleService CodeRuleService { get; init; }

        public required ChangeLogService ChangeLog { get; init; }

        public required BasCodeNumberGenerator CodeGenerator { get; init; }

        public required IDataProtectionProvider Protection { get; init; }

        public required BasImportTemplateDefinitionProvider Definitions { get; init; }

        public required SupplierImportSink SupplierImport { get; init; }

        public required CustomerImportSink CustomerImport { get; init; }

        public required PersonImportSink PersonImport { get; init; }

        public required CategoryImportSink CategoryImport { get; init; }

        public required SupplierExportProvider SupplierExport { get; init; }

        public required CustomerExportProvider CustomerExport { get; init; }

        public required PersonExportProvider PersonExport { get; init; }

        public required CategoryExportProvider CategoryExport { get; init; }
    }

    private static Harness Build()
    {
        var basConnection = new SqliteConnection("DataSource=:memory:");
        basConnection.Open();
        var logConnection = new SqliteConnection("DataSource=:memory:");
        logConnection.Open();

        var resolver = new JobStubTenantDatabaseResolver();
        var db = new BasTenantDbContext(
            resolver,
            TenantId,
            new DbContextOptionsBuilder<BasTenantDbContext>().UseSqlite(basConnection).Options);
        _ = db.Database.EnsureCreated();

        var logDb = new TenantLogDbContext(
            resolver,
            TenantId,
            new DbContextOptionsBuilder<TenantLogDbContext>().UseSqlite(logConnection).Options);
        _ = logDb.Database.EnsureCreated();

        var repo = new BasTenantRepository(db, logDb);
        var serials = new InMemorySerialSequenceStore();
        var cache = new MemoryCacheService(Options.Create(new CacheOptions()));
        var audit = new InMemoryAuditService();
        var scope = new StubCurrentMemberDataScopeResolver();
        var tenantCtx = new TestTenantContext(TenantId);
        IDataProtectionProvider protection = new EphemeralDataProtectionProvider();
        var codeGenerator = new BasCodeNumberGenerator(repo, serials);
        var definitions = new BasImportTemplateDefinitionProvider();

        return new Harness
        {
            Db = db,
            Repo = repo,
            Cache = cache,
            Audit = audit,
            Scope = scope,
            Dictionary = new DictionaryService(tenantCtx, repo, scope, cache),
            CodeRuleService = new CodeRuleService(tenantCtx, repo, cache),
            ChangeLog = new ChangeLogService(tenantCtx, audit),
            CodeGenerator = codeGenerator,
            Protection = protection,
            Definitions = definitions,
            SupplierImport = new SupplierImportSink(tenantCtx, repo, codeGenerator),
            CustomerImport = new CustomerImportSink(tenantCtx, repo, codeGenerator),
            PersonImport = new PersonImportSink(tenantCtx, repo, codeGenerator, protection),
            CategoryImport = new CategoryImportSink(tenantCtx, repo, codeGenerator),
            SupplierExport = new SupplierExportProvider(tenantCtx, repo),
            CustomerExport = new CustomerExportProvider(tenantCtx, repo),
            PersonExport = new PersonExportProvider(tenantCtx, repo, protection),
            CategoryExport = new CategoryExportProvider(tenantCtx, repo),
        };
    }

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells)
    {
        Dictionary<string, object?> row = new(StringComparer.Ordinal);
        foreach ((string key, object? value) in cells)
        {
            row[key] = value;
        }

        return row;
    }

    // ============ 7.1 / 7.2 数据字典 ============

    [Fact]
    public async Task DictionaryTypesFilterByTwoLevelVisibility()
    {
        Harness h = Build();
        Organization root = new(TenantId, "集团", OrgNodeType.Group, null, code: "ORG-GS-01");
        await h.Repo.AddOrgAsync(root, Ct);
        _ = await h.Repo.SaveChangesAsync(Ct);
        Organization sub = new(TenantId, "子公司", OrgNodeType.Subsidiary, root.Id, code: "ORG-SUB-01");
        await h.Repo.AddOrgAsync(sub, Ct);
        _ = await h.Repo.SaveChangesAsync(Ct);

        await h.Repo.AddDictTypeAsync(
            new DictType(TenantId, "group_type", "集团字典", DictOwnerLevel.Group, null), Ct);
        await h.Repo.AddDictTypeAsync(
            new DictType(TenantId, "sub_type", "子公司字典", DictOwnerLevel.Subsidiary, sub.Id), Ct);
        _ = await h.Repo.SaveChangesAsync(Ct);

        h.Scope.Context = new MemberDataScopeContext(1L, DataScope.Dept, sub.Id);
        IReadOnlyList<DictTypeRow> ownSubsidiary = await h.Dictionary.ListTypesAsync(null, null, Ct);
        Assert.Equal(2, ownSubsidiary.Count);

        h.Scope.Context = new MemberDataScopeContext(1L, DataScope.Dept, root.Id);
        IReadOnlyList<DictTypeRow> otherSubsidiary = await h.Dictionary.ListTypesAsync(null, null, Ct);
        Assert.Single(otherSubsidiary);
        Assert.Equal("group_type", otherSubsidiary[0].TypeCode);
    }

    [Fact]
    public async Task TenantAdminSeesGroupAndAllSubsidiaryDictionaryTypes()
    {
        Harness h = Build();
        (_, _, DictType subBType) = await SeedTwoSubsidiaryDictsAsync(h);

        // 租户管理员（scopeResolver 返回 null）：集团级 + 全部子公司级；详情不受子公司级可见性限制
        IReadOnlyList<DictTypeRow> all = await h.Dictionary.ListTypesAsync(null, null, Ct);
        Assert.Equal(3, all.Count);
        Assert.Equal("sub_b_type", (await h.Dictionary.GetTypeAsync(subBType.Id, Ct)).TypeCode);
    }

    [Fact]
    public async Task SubsidiaryMemberSeesGroupAndOwnOrgDictionaryTypesOnly()
    {
        Harness h = Build();
        (long subAId, _, DictType subBType) = await SeedTwoSubsidiaryDictsAsync(h);

        h.Scope.Context = new MemberDataScopeContext(1L, DataScope.Dept, subAId);
        IReadOnlyList<DictTypeRow> ownSubsidiary = await h.Dictionary.ListTypesAsync(null, null, Ct);
        Assert.Equal(2, ownSubsidiary.Count);
        Assert.Contains(ownSubsidiary, static row => row.TypeCode == "group_type");
        Assert.Contains(ownSubsidiary, static row => row.TypeCode == "sub_a_type");
        Assert.DoesNotContain(ownSubsidiary, static row => row.TypeCode == "sub_b_type");

        _ = await Assert.ThrowsAsync<ForbiddenException>(
            () => h.Dictionary.GetTypeAsync(subBType.Id, Ct));
    }

    [Fact]
    public async Task PortalModeWithoutMemberContextSeesAllDictionaryTypes()
    {
        Harness h = Build();
        (long subAId, _, _) = await SeedTwoSubsidiaryDictsAsync(h);

        h.Scope.Context = new MemberDataScopeContext(1L, DataScope.Dept, subAId);
        IReadOnlyList<DictTypeRow> ownSubsidiary = await h.Dictionary.ListTypesAsync(null, null, Ct);
        Assert.Equal(2, ownSubsidiary.Count);

        // 无成员上下文的门户模式与 TenantAdmin 同一条 scope=null 路径，缓存范围键（all）不得沿用上一范围
        h.Scope.Context = null;
        IReadOnlyList<DictTypeRow> all = await h.Dictionary.ListTypesAsync(null, null, Ct);
        Assert.Equal(3, all.Count);
    }

    /// <summary>种子：集团根组织 + 两家子公司 + 集团级 / A / B 各一条子公司级字典。</summary>
    private static async Task<(long SubAId, DictType SubAType, DictType SubBType)> SeedTwoSubsidiaryDictsAsync(
        Harness h)
    {
        Organization root = new(TenantId, "集团", OrgNodeType.Group, null, code: "ORG-GS-01");
        await h.Repo.AddOrgAsync(root, Ct);
        _ = await h.Repo.SaveChangesAsync(Ct);
        Organization subA = new(TenantId, "子公司A", OrgNodeType.Subsidiary, root.Id, code: "ORG-SUB-A");
        Organization subB = new(TenantId, "子公司B", OrgNodeType.Subsidiary, root.Id, code: "ORG-SUB-B");
        await h.Repo.AddOrgAsync(subA, Ct);
        await h.Repo.AddOrgAsync(subB, Ct);
        _ = await h.Repo.SaveChangesAsync(Ct);

        await h.Repo.AddDictTypeAsync(
            new DictType(TenantId, "group_type", "集团字典", DictOwnerLevel.Group, null), Ct);
        DictType subAType = new(TenantId, "sub_a_type", "子公司A字典", DictOwnerLevel.Subsidiary, subA.Id);
        DictType subBType = new(TenantId, "sub_b_type", "子公司B字典", DictOwnerLevel.Subsidiary, subB.Id);
        await h.Repo.AddDictTypeAsync(subAType, Ct);
        await h.Repo.AddDictTypeAsync(subBType, Ct);
        _ = await h.Repo.SaveChangesAsync(Ct);

        return (subA.Id, subAType, subBType);
    }

    [Fact]
    public async Task GroupLevelDictionaryWriteRequiresTenantRootOrg()
    {
        Harness h = Build();
        Organization root = new(TenantId, "集团", OrgNodeType.Group, null, code: "ORG-GS-01");
        await h.Repo.AddOrgAsync(root, Ct);
        _ = await h.Repo.SaveChangesAsync(Ct);
        Organization department = new(TenantId, "部门", OrgNodeType.Department, root.Id, code: "ORG-BM-01");
        await h.Repo.AddOrgAsync(department, Ct);
        Organization otherSub = new(TenantId, "其它子公司", OrgNodeType.Subsidiary, root.Id, code: "ORG-SUB-02");
        await h.Repo.AddOrgAsync(otherSub, Ct);
        _ = await h.Repo.SaveChangesAsync(Ct);

        // 非根组织维护集团级字典 → 403
        h.Scope.Context = new MemberDataScopeContext(1L, DataScope.Dept, department.Id);
        _ = await Assert.ThrowsAsync<ForbiddenException>(() => h.Dictionary.CreateTypeAsync(
            new SaveDictTypePayload("group_type", "集团字典", (int)DictOwnerLevel.Group, null), Ct));

        // 租户根组织维护集团级字典 → 成功
        h.Scope.Context = new MemberDataScopeContext(1L, DataScope.All, root.Id);
        DictTypeRow created = await h.Dictionary.CreateTypeAsync(
            new SaveDictTypePayload("group_type", "集团字典", (int)DictOwnerLevel.Group, null), Ct);
        Assert.Equal("group_type", created.TypeCode);
        Assert.Null(created.OrgId);

        // 子公司级字典跨组织维护 → 403
        h.Scope.Context = new MemberDataScopeContext(1L, DataScope.Dept, department.Id);
        _ = await Assert.ThrowsAsync<ForbiddenException>(() => h.Dictionary.CreateTypeAsync(
            new SaveDictTypePayload("sub_type", "子公司字典", (int)DictOwnerLevel.Subsidiary, otherSub.Id), Ct));
    }

    [Fact]
    public async Task SystemDictItemCannotBeDeletedButCanBeToggled()
    {
        Harness h = Build();
        await h.Repo.AddDictTypeAsync(
            new DictType(TenantId, "sys_type", "系统字典", DictOwnerLevel.Group, null, isSystem: true), Ct);
        DictItem item = new(TenantId, "sys_type", "yes", "是", 1, isSystem: true);
        await h.Repo.AddDictItemAsync(item, Ct);
        _ = await h.Repo.SaveChangesAsync(Ct);

        ConflictException deleteError = await Assert.ThrowsAsync<ConflictException>(
            () => h.Dictionary.DeleteItemAsync(item.Id, Ct));
        Assert.Equal("DICT_ITEM_BUILTIN", deleteError.ErrorCode);

        DictItemRow toggled = await h.Dictionary.ToggleItemAsync(item.Id, Ct);
        Assert.Equal((int)EntityStatus.Disabled, toggled.Status);
    }

    // ============ 7.3 编码规则 ============

    [Fact]
    public async Task CodeRuleTemplateRejectsIllegalSegments()
    {
        Assert.True(CodeRuleTemplate.TryParse("""["{type}","-","{seq:2}"]""", out string[] segments, out string? error));
        Assert.Null(error);
        Assert.Equal(3, segments.Length);

        Assert.False(CodeRuleTemplate.TryParse("""["{bad}"]""", out _, out string? invalidError));
        Assert.False(string.IsNullOrWhiteSpace(invalidError));

        Harness h = Build();
        string first = await h.CodeGenerator.NextAsync(
            "supplier", TenantId, null, null, (candidate, token) => Task.FromResult(false), Ct);
        string second = await h.CodeGenerator.NextAsync(
            "supplier", TenantId, null, null, (candidate, token) => Task.FromResult(false), Ct);
        Assert.Equal("SUP-000001", first);
        Assert.Equal("SUP-000002", second);

        // 已落库的非法规则：渲染期按 422 校验异常返回（不得抛 InvalidOperationException）
        await h.Repo.AddCodeRuleAsync(new CodeRule(TenantId, "customer", "CUS-", """["{bad}"]"""), Ct);
        _ = await h.Repo.SaveChangesAsync(Ct);
        ValidationException invalidRule = await Assert.ThrowsAsync<ValidationException>(() =>
            h.CodeGenerator.NextAsync(
                "customer", TenantId, null, null, (candidate, token) => Task.FromResult(false), Ct));
        Assert.Equal("segmentsJson", invalidRule.FieldErrors[0].FieldName);
    }

    [Fact]
    public async Task CodeRuleServiceTogglesStatusOnly()
    {
        Harness h = Build();
        await h.Repo.AddCodeRuleAsync(
            new CodeRule(TenantId, "supplier", "SUP-", """["{seq:6}"]"""), Ct);
        _ = await h.Repo.SaveChangesAsync(Ct);

        IReadOnlyList<CodeRuleRow> list = await h.CodeRuleService.ListAsync(null, null, Ct);
        Assert.Single(list);
        Assert.Equal((int)EntityStatus.Enabled, list[0].Status);

        CodeRuleRow disabled = await h.CodeRuleService.DisableAsync("supplier", Ct);
        Assert.Equal((int)EntityStatus.Disabled, disabled.Status);

        CodeRuleRow enabled = await h.CodeRuleService.EnableAsync("supplier", Ct);
        Assert.Equal((int)EntityStatus.Enabled, enabled.Status);

        _ = await Assert.ThrowsAsync<NotFoundException>(
            () => h.CodeRuleService.EnableAsync("unknown", Ct));
    }

    // ============ 7.4 导入导出 ============

    [Fact]
    public async Task TemplateProviderRegistersFourImportAndFourExportTemplates()
    {
        Harness h = Build();
        var registry = new CompositeImportTemplateRegistry([h.Definitions]);

        string[] importCodes =
        [
            BasImportTemplateDefinitionProvider.SupplierImport,
            BasImportTemplateDefinitionProvider.CustomerImport,
            BasImportTemplateDefinitionProvider.PersonImport,
            BasImportTemplateDefinitionProvider.CategoryImport,
        ];

        string[] exportCodes =
        [
            BasImportTemplateDefinitionProvider.SupplierExport,
            BasImportTemplateDefinitionProvider.CustomerExport,
            BasImportTemplateDefinitionProvider.PersonExport,
            BasImportTemplateDefinitionProvider.CategoryExport,
        ];

        foreach (string code in importCodes)
        {
            ImportTemplateDefinition definition = registry.Find(code)
                ?? throw new InvalidOperationException($"导入模板未注册：{code}");
            Assert.Equal(QlxModuleCodes.Bas, definition.ModuleCode);
            Assert.DoesNotContain(definition.Columns, static c => c.ColumnName == "code");
        }

        foreach (string code in exportCodes)
        {
            ImportTemplateDefinition definition = registry.Find(code)
                ?? throw new InvalidOperationException($"导出模板未注册：{code}");
            Assert.Contains(definition.Columns, static c => c.ColumnName == "code");
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task PersonImportReportsRowNumberedErrors()
    {
        Harness h = Build();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
        [
            Row(("name", ""), ("personType", "1"), ("idType", "9"), ("idNo", ""), ("mobile", "")),
        ];

        ValidationException error = await Assert.ThrowsAsync<ValidationException>(
            () => h.PersonImport.WriteRowsAsync(rows, GuidIdMapper.ToGuid(TenantId), Ct));

        Assert.Contains(
            error.FieldErrors,
            e => e.FieldName == "name" && e.ErrorMessage.Contains("第 2 行", StringComparison.Ordinal));
        Assert.Contains(error.FieldErrors, static e => e.FieldName == "idType");
        Assert.Contains(error.FieldErrors, static e => e.FieldName == "idNo");
    }

    [Fact]
    public async Task ImportSinkRejectsTenantMismatchAndInvalidTenantGuid()
    {
        Harness h = Build();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
        [
            Row(("name", "成都示例科技有限公司"), ("uscc", "91510000MA00000001"), ("supplierType", "2")),
        ];

        ValidationException invalidGuid = await Assert.ThrowsAsync<ValidationException>(
            () => h.SupplierImport.WriteRowsAsync(rows, Guid.Empty, Ct));
        Assert.Equal("tenantId", invalidGuid.FieldErrors[0].FieldName);

        _ = await Assert.ThrowsAsync<ForbiddenException>(
            () => h.SupplierImport.WriteRowsAsync(rows, GuidIdMapper.ToGuid(99L), Ct));
    }

    [Fact]
    public async Task SupplierImportCreatesSupplierWithGeneratedCodeAndRejectsDuplicate()
    {
        Harness h = Build();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
        [
            Row(("name", "成都示例科技有限公司"), ("uscc", "91510000MA00000001"), ("supplierType", "2")),
        ];

        int written = await h.SupplierImport.WriteRowsAsync(rows, GuidIdMapper.ToGuid(TenantId), Ct);
        Assert.Equal(1, written);

        SupplierEntity supplier = await h.Db.Suppliers.SingleAsync(Ct);
        Assert.StartsWith("SUP-", supplier.Code, StringComparison.Ordinal);
        Assert.Equal("91510000MA00000001", supplier.Uscc);
        Assert.Equal(SupplierType.Dealer, supplier.SupplierType);

        // 库内统一社会信用代码防重（同一批次再次导入）
        ValidationException duplicate = await Assert.ThrowsAsync<ValidationException>(
            () => h.SupplierImport.WriteRowsAsync(rows, GuidIdMapper.ToGuid(TenantId), Ct));
        Assert.Contains(duplicate.FieldErrors, static e => e.FieldName == "uscc");
    }

    [Fact]
    public async Task CategoryImportGeneratesHierarchicalCodes()
    {
        Harness h = Build();
        ItemCategory root = new(TenantId, "办公用品", null, 1, "01", null);
        await h.Repo.AddCategoryAsync(root, Ct);
        _ = await h.Repo.SaveChangesAsync(Ct);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = [Row(("name", "文具"), ("parentCode", "01"))];
        int written = await h.CategoryImport.WriteRowsAsync(rows, GuidIdMapper.ToGuid(TenantId), Ct);
        Assert.Equal(1, written);

        ItemCategory created = await h.Db.ItemCategories.SingleAsync(c => c.Name == "文具", Ct);
        Assert.Equal("0101", created.Code);
        Assert.Equal(2, created.LevelNo);
        Assert.Equal(root.Id, created.ParentId!.Value);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> invalidRows =
            [Row(("name", "无效分类"), ("parentCode", "99"))];
        ValidationException error = await Assert.ThrowsAsync<ValidationException>(
            () => h.CategoryImport.WriteRowsAsync(invalidRows, GuidIdMapper.ToGuid(TenantId), Ct));
        Assert.Contains(
            error.FieldErrors,
            static e => e.FieldName == "parentCode" && e.ErrorMessage.Contains("第 2 行", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportProvidersMaskSensitiveFieldsAndIncludeCodes()
    {
        Harness h = Build();
        await h.Repo.AddSupplierAsync(
            new SupplierEntity(TenantId, "SUP-000001", "供应商甲", "91510000MA00000001", SupplierType.Dealer), Ct);
        await h.Repo.AddCustomerAsync(
            new Qlx.Modules.Bas.Domain.Customer.Customer(
                TenantId, "CUS-000001", "客户甲", "91510000MA00000002", 2), Ct);

        IDataProtector protector = h.Protection.CreateProtector(QlxDataProtectionPurposes.BasPersonSensitive);
        await h.Repo.AddPersonAsync(
            new PersonEntity(
                TenantId,
                "EMP-000001",
                "张三",
                PersonType.Employee,
                PersonIdType.IdCard,
                protector.Protect("510100199001010011"),
                "hash-0001",
                protector.Protect("13800000001"),
                new DateOnly(2026, 1, 1)), Ct);
        _ = await h.Repo.SaveChangesAsync(Ct);

        Guid tenantGuid = GuidIdMapper.ToGuid(TenantId);
        IReadOnlyDictionary<string, object?> criteria = new Dictionary<string, object?>(StringComparer.Ordinal);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> suppliers =
            await h.SupplierExport.GetRowsAsync(criteria, tenantGuid, Ct);
        Assert.Single(suppliers);
        Assert.Equal("SUP-000001", suppliers[0]["code"]);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> customers =
            await h.CustomerExport.GetRowsAsync(criteria, tenantGuid, Ct);
        Assert.Single(customers);
        Assert.Equal("CUS-000001", customers[0]["code"]);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> persons =
            await h.PersonExport.GetRowsAsync(criteria, tenantGuid, Ct);
        Assert.Single(persons);

        string maskedIdNo = Assert.IsType<string>(persons[0]["idNoMasked"]);
        Assert.Equal("510100****0011", maskedIdNo);
        Assert.DoesNotContain("510100199001010011", maskedIdNo, StringComparison.Ordinal);

        string maskedMobile = Assert.IsType<string>(persons[0]["mobileMasked"]);
        Assert.Equal("138****0001", maskedMobile);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> categories =
            await h.CategoryExport.GetRowsAsync(criteria, tenantGuid, Ct);
        Assert.Empty(categories);
    }

    // ============ 7.5 变更日志 ============

    [Fact]
    public async Task ChangeLogAppliesFilterHardLimitPagingAndMasking()
    {
        Harness h = Build();
        for (int index = 0; index < 510; index++)
        {
            await h.Audit.CaptureChangeAsync(
                new AuditLogEntry(
                    index + 1L,
                    index % 2 == 0 ? "Supplier" : "Customer",
                    index + 1L,
                    "UPDATE",
                    """{"name":"甲","idNo":"510100199001010011"}""",
                    """{"mobile":"13800000001","nested":{"phone":"13900000002"}}""",
                    "user-9",
                    DateTimeOffset.UtcNow.AddMinutes(-index),
                    TenantId),
                Ct);
        }

        PagedResult<ChangeLogRow> firstPage = await h.ChangeLog.QueryAsync(
            null, null, null, null, null, 1, 20, Ct);
        Assert.Equal(500, firstPage.TotalCount);
        Assert.Equal(20, firstPage.Items.Count);

        ChangeLogRow first = firstPage.Items[0];
        Assert.Equal("Supplier", first.EntityType);
        Assert.DoesNotContain("510100199001010011", first.OldValues, StringComparison.Ordinal);
        Assert.Contains("****", first.OldValues, StringComparison.Ordinal);
        Assert.DoesNotContain("13800000001", first.NewValues, StringComparison.Ordinal);
        Assert.DoesNotContain("13900000002", first.NewValues, StringComparison.Ordinal);

        PagedResult<ChangeLogRow> beyond = await h.ChangeLog.QueryAsync(
            null, null, null, null, null, 26, 20, Ct);
        Assert.Empty(beyond.Items);

        PagedResult<ChangeLogRow> supplierOnly = await h.ChangeLog.QueryAsync(
            "Supplier", null, null, null, null, 1, 20, Ct);
        Assert.All(supplierOnly.Items, static row => Assert.Equal("Supplier", row.EntityType));
        Assert.Equal(255, supplierOnly.TotalCount);
    }

    // ============ C-6 缓存清理 ============

    [Fact]
    public async Task CacheCleanupJobRemovesOnlyCurrentTenantBasPrefix()
    {
        Harness h = Build();
        string basKey = QlxCacheKey.BuildTenantKey(TenantId, QlxModuleCodes.Bas, "dict:all");
        string otherModuleKey = QlxCacheKey.BuildTenantKey(TenantId, "por", "dict:all");
        string otherTenantKey = QlxCacheKey.BuildTenantKey(99L, QlxModuleCodes.Bas, "dict:all");

        await h.Cache.SetAsync(basKey, "bas", Ct);
        await h.Cache.SetAsync(otherModuleKey, "por", Ct);
        await h.Cache.SetAsync(otherTenantKey, "other", Ct);

        var job = new BasCacheCleanupJobHandler(
            new TestTenantContext(TenantId), h.Cache, NullLogger<BasCacheCleanupJobHandler>.Instance);
        CompletedResult result = await job.ExecuteAsync(
            new JobContext
            {
                ExecutionId = Guid.NewGuid(),
                TaskCode = job.TaskCode,
                TenantId = GuidIdMapper.ToGuid(TenantId),
            },
            Ct);

        Assert.True(result.IsSuccess);
        Assert.Null(await h.Cache.GetAsync<string>(basKey, Ct));
        Assert.Equal("por", await h.Cache.GetAsync<string>(otherModuleKey, Ct));
        Assert.Equal("other", await h.Cache.GetAsync<string>(otherTenantKey, Ct));
    }

    // ============ 7.4 模板级权限二次校验 ============

    [Fact]
    public async Task ImportExportServiceEnforcesTemplatePermission()
    {
        Harness h = Build();
        var stub = new StubImportExportService();
        var httpContext = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var service = new BasImportExportService(
            new TestTenantContext(TenantId), stub, h.Definitions, accessor);

        // 拥有供应商权限：导入放行，租户 Guid 由上下文单向映射
        httpContext.User = Principal(authenticated: true, tenantClaim: true, memberRole: null, permissions: "bas.supplier.action");
        ImportResult imported = await service.ImportAsync(
            BasImportTemplateDefinitionProvider.SupplierImport, new MemoryStream(), Ct);
        Assert.Same(stub.Result, imported);
        Assert.Equal(GuidIdMapper.ToGuid(TenantId), stub.CapturedTenantId!.Value);

        // 无客户权限：403
        _ = await Assert.ThrowsAsync<ForbiddenException>(() => service.ImportAsync(
            BasImportTemplateDefinitionProvider.CustomerImport, new MemoryStream(), Ct));

        // 模板后缀不符：404
        _ = await Assert.ThrowsAsync<NotFoundException>(() => service.ExportAsync(
            BasImportTemplateDefinitionProvider.SupplierImport,
            new Dictionary<string, object?>(StringComparer.Ordinal),
            Ct));

        // TenantAdmin 旁路
        httpContext.User = Principal(true, true, QlxMemberRoleValues.TenantAdmin, permissions: null);
        _ = await service.ImportAsync(
            BasImportTemplateDefinitionProvider.CustomerImport, new MemoryStream(), Ct);

        // 未认证：403
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        _ = await Assert.ThrowsAsync<ForbiddenException>(() => service.ImportAsync(
            BasImportTemplateDefinitionProvider.SupplierImport, new MemoryStream(), Ct));
    }

    private static ClaimsPrincipal Principal(
        bool authenticated, bool tenantClaim, string? memberRole, string? permissions)
    {
        List<Claim> claims = [];
        if (tenantClaim)
        {
            claims.Add(new Claim(QlxClaimConstants.TenantId, "tenant-1"));
        }

        if (memberRole is not null)
        {
            claims.Add(new Claim(QlxClaimConstants.MemberRole, memberRole));
        }

        if (permissions is not null)
        {
            claims.Add(new Claim(QlxClaimConstants.Permissions, permissions));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null));
    }

    private sealed class StubImportExportService : IImportExportService
    {
        public ImportResult Result { get; } = new(1, 1, 0, [], TimeSpan.Zero);

        public Guid? CapturedTenantId { get; private set; }

        public Task<ImportResult> ImportAsync(
            Stream stream, string templateCode, Guid tenantId, CancellationToken cancellationToken)
        {
            CapturedTenantId = tenantId;
            return Task.FromResult(Result);
        }

        public Task<ExportResult> ExportAsync(
            string templateCode,
            IReadOnlyDictionary<string, object?> criteria,
            Guid tenantId,
            CancellationToken cancellationToken)
            => Task.FromResult(new ExportResult(
                new MemoryStream(),
                "stub.xlsx",
                ImportExportService.XlsxContentType,
                0,
                TimeSpan.Zero));

        public Task<ImportTemplateInfo?> GetTemplateAsync(string templateCode, CancellationToken cancellationToken)
            => Task.FromResult<ImportTemplateInfo?>(null);
    }
}
