/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.2.0 beta
 * 创建时间：2026-10-21
 * 修改时间：2026-10-25 23:55:00 +08:00 中国标准时间 星期日
 * 描述：Bas 模块仓储 EF 实现——纯数据访问，AsNoTracking 读、Find 跟踪态写。
 */

using Microsoft.EntityFrameworkCore;
using Qlx.BuildingBlocks.Support.Logs;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Domain.Dictionary;
using Qlx.Modules.Bas.Domain.Goods;
using Qlx.Modules.Bas.Domain.Numbering;

namespace Qlx.Modules.Bas.Repositories;

internal sealed class BasTenantRepository(BasTenantDbContext db, TenantLogDbContext logDb) : IBasTenantRepository
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);

        // 已在外层事务中：直接复用，避免同一 DbContext 重复 BeginTransaction
        if (_activeTransaction is not null)
        {
            return await action(ct);
        }

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(ct);
        _activeTransaction = transaction;
        try
        {
            T result = await action(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        finally
        {
            _activeTransaction = null;
        }
    }

    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? _activeTransaction;

    // ============ Organization ============

    public async Task<IReadOnlyList<Organization>> ListAllOrgsAsync(CancellationToken ct)
        => await db.Organizations.AsNoTracking().OrderBy(o => o.SortOrder).ToListAsync(ct);

    public Task<Organization?> FindOrgAsync(long id, CancellationToken ct)
        => db.Organizations.FindAsync([id], ct).AsTask();

    public Task<bool> IsOrgCodeExistsAsync(string code, CancellationToken ct)
        => db.Organizations.AnyAsync(o => o.Code == code, ct);

    public Task AddOrgAsync(Organization org, CancellationToken ct)
    {
        db.Organizations.Add(org);
        return Task.CompletedTask;
    }

    public Task UpdateOrgAsync(Organization org, CancellationToken ct)
    {
        db.Organizations.Update(org);
        return Task.CompletedTask;
    }

    public Task RemoveOrgAsync(Organization org, CancellationToken ct)
    {
        db.Organizations.Remove(org);
        return Task.CompletedTask;
    }

    public async Task<bool> HasChildOrgsAsync(long parentId, CancellationToken ct)
        => await db.Organizations.AnyAsync(o => o.ParentId == parentId && !o.IsDeleted, ct);

    // 注：成员 OrgId 存在 Portal 库 por_tenant_members，需 Service 层通过 ITenantMemberQuery 查询
    // 此处保留接口占位，返回 false 由消费方自行跨库判断
    public Task<bool> HasMembersUnderOrgAsync(long orgId, CancellationToken ct)
        => Task.FromResult(false);

    // ============ OrgNodeHistory ============

    public async Task<IReadOnlyList<OrgNodeHistory>> ListOrgHistoriesAsync(long orgId, CancellationToken ct)
        => await db.OrgNodeHistories.AsNoTracking()
            .Where(h => h.OrgId == orgId)
            .OrderBy(h => h.Version)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<OrgNodeHistory>> ListAllOrgHistoriesAsync(CancellationToken ct)
        => await db.OrgNodeHistories.AsNoTracking()
            .OrderBy(h => h.OrgId).ThenBy(h => h.Version)
            .ToListAsync(ct);

    public Task AddOrgHistoryAsync(OrgNodeHistory history, CancellationToken ct)
    {
        db.OrgNodeHistories.Add(history);
        return Task.CompletedTask;
    }

    // ============ BasApprovalRequest ============

    public Task<BasApprovalRequest?> FindApprovalRequestAsync(long id, CancellationToken ct)
        => db.ApprovalRequests.FindAsync([id], ct).AsTask();

    public Task<BasApprovalRequest?> FindApprovalRequestByInstanceAsync(
        string approvalInstanceId, CancellationToken ct)
        => db.ApprovalRequests.FirstOrDefaultAsync(r => r.ApprovalInstanceId == approvalInstanceId, ct);

    public Task AddApprovalRequestAsync(BasApprovalRequest request, CancellationToken ct)
    {
        db.ApprovalRequests.Add(request);
        return Task.CompletedTask;
    }

    // ============ CodeRule ============

    public Task<CodeRule?> FindCodeRuleAsync(string bizType, CancellationToken ct)
        => db.CodeRules.FirstOrDefaultAsync(r => r.BizType == bizType, ct);

    public Task AddCodeRuleAsync(CodeRule rule, CancellationToken ct)
    {
        db.CodeRules.Add(rule);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<CodeRule>> ListCodeRulesAsync(
        string? bizType, int? status, CancellationToken ct)
    {
        IQueryable<CodeRule> q = db.CodeRules.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(bizType))
        {
            q = q.Where(r => r.BizType == bizType);
        }

        if (status.HasValue)
        {
            q = q.Where(r => (int)r.Status == status.Value);
        }

        return await q.OrderBy(r => r.BizType).ToListAsync(ct);
    }

    public Task UpdateCodeRuleAsync(CodeRule rule, CancellationToken ct)
    {
        db.CodeRules.Update(rule);
        return Task.CompletedTask;
    }

    // ============ Dictionary（数据字典类型 / 字典项）============

    public async Task<IReadOnlyList<DictType>> QueryDictTypesAsync(
        bool unrestricted, long? orgId, int? ownerLevel, int? status, CancellationToken ct)
    {
        // 两级可见性在 SQL 层过滤：集团级全租户可见；子公司级仅本组织可见（orgId 为空时子公司级不可见）。
        // unrestricted（TenantAdmin 全范围 / 无成员上下文的门户模式）不过滤层级可见性。
        IQueryable<DictType> q = unrestricted
            ? db.DictTypes.AsNoTracking()
            : db.DictTypes.AsNoTracking().Where(t => t.OwnerLevel == DictOwnerLevel.Group
                || (t.OwnerLevel == DictOwnerLevel.Subsidiary && orgId != null && t.OrgId == orgId));

        if (ownerLevel.HasValue)
        {
            q = q.Where(t => (int)t.OwnerLevel == ownerLevel.Value);
        }

        if (status.HasValue)
        {
            q = q.Where(t => (int)t.Status == status.Value);
        }

        return await q.OrderBy(t => t.OwnerLevel).ThenBy(t => t.TypeCode).ToListAsync(ct);
    }

    public Task<DictType?> FindDictTypeAsync(long id, CancellationToken ct)
        => db.DictTypes.FindAsync([id], ct).AsTask();

    public Task<DictType?> FindDictTypeByCodeAsync(string typeCode, CancellationToken ct)
        => db.DictTypes.AsNoTracking().FirstOrDefaultAsync(t => t.TypeCode == typeCode, ct);

    public Task<bool> IsDictTypeCodeExistsAsync(string typeCode, long? excludeId, CancellationToken ct)
        => db.DictTypes.AnyAsync(t =>
            t.TypeCode == typeCode && (!excludeId.HasValue || t.Id != excludeId.Value), ct);

    public Task AddDictTypeAsync(DictType type, CancellationToken ct)
    {
        db.DictTypes.Add(type);
        return Task.CompletedTask;
    }

    public Task UpdateDictTypeAsync(DictType type, CancellationToken ct)
    {
        db.DictTypes.Update(type);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<DictItem>> QueryDictItemsAsync(
        string typeCode, int? status, CancellationToken ct)
    {
        IQueryable<DictItem> q = db.DictItems.AsNoTracking().Where(i => i.TypeCode == typeCode);
        if (status.HasValue)
        {
            q = q.Where(i => (int)i.Status == status.Value);
        }

        return await q.OrderBy(i => i.SortNo).ThenBy(i => i.ItemCode).ToListAsync(ct);
    }

    public Task<DictItem?> FindDictItemAsync(long id, CancellationToken ct)
        => db.DictItems.FindAsync([id], ct).AsTask();

    public Task<bool> IsDictItemCodeExistsAsync(
        string typeCode, string itemCode, long? excludeId, CancellationToken ct)
        => db.DictItems.AnyAsync(i =>
            i.TypeCode == typeCode && i.ItemCode == itemCode
            && (!excludeId.HasValue || i.Id != excludeId.Value), ct);

    public Task AddDictItemAsync(DictItem item, CancellationToken ct)
    {
        db.DictItems.Add(item);
        return Task.CompletedTask;
    }

    public Task UpdateDictItemAsync(DictItem item, CancellationToken ct)
    {
        db.DictItems.Update(item);
        return Task.CompletedTask;
    }

    public Task RemoveDictItemAsync(DictItem item, CancellationToken ct)
    {
        db.DictItems.Remove(item);
        return Task.CompletedTask;
    }

    // ============ Position ============

    public async Task<IReadOnlyList<Position>> ListAllPositionsAsync(CancellationToken ct)
        => await db.Positions.AsNoTracking()
            .OrderBy(p => p.OrgId).ThenBy(p => p.Name)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<Position> Items, int Total)> QueryPositionsAsync(
        IReadOnlyCollection<long>? orgIds, string? keyword, int skip, int take, CancellationToken ct)
    {
        IQueryable<Position> q = db.Positions.AsNoTracking();
        // 组织子树筛选（服务层已把「选中组织 + 全部下级」展开为 Id 集合，单条 IN 查询）
        if (orgIds is { Count: > 0 }) q = q.Where(p => orgIds.Contains(p.OrgId));
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            q = q.Where(p =>
                p.Name.Contains(keyword) || p.Code.Contains(keyword)
                || (p.Description != null && p.Description.Contains(keyword)));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(p => p.OrgId).ThenBy(p => p.Name)
            .Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }

    public Task<Position?> FindPositionAsync(long id, CancellationToken ct)
        => db.Positions.FindAsync([id], ct).AsTask();

    public async Task<IReadOnlyList<Position>> ListEnabledPositionsByOrgIdsAsync(
        IReadOnlyCollection<long> orgIds, CancellationToken ct)
    {
        if (orgIds.Count == 0)
        {
            return [];
        }

        return await db.Positions.AsNoTracking()
            .Where(p => orgIds.Contains(p.OrgId) && p.Status == EntityStatus.Enabled)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Position>> ListPositionsByIdsAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        // 跟踪态读取：调用方会 SetCurrentCount 后统一保存；已跟踪实例由标识解析直接复用，避免同键冲突
        return await db.Positions
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(ct);
    }

    public async Task<bool> IsPositionCodeExistsAsync(string code, long? excludeId, CancellationToken ct)
    {
        // 注：EF Core 翻译限制——
        //   ① 无法翻译 string.Equals(code1, code2, StringComparison) 重载（CA1862 要求的 OrdinalIgnoreCase 不可用）
        //   ② 无法翻译 string.ToLowerInvariant()（EF 10 不支持 invariant 方法映射）
        //   ③ ToLower() 会触发一串全球化分析器（CA1304/CA1311 等），但 SQL 端 LOWER(col) 默认是 CI，语义正确
        // 唯一可行方案：ToLower() + == ，SQL 端翻译为 LOWER(col) = @lower ，与数据库默认 CI 排序规则语义等价
#pragma warning disable CA1304, CA1311, CA1862
        var lower = code.ToLower();
        return await db.Positions.AnyAsync(p =>
            p.Code.ToLower() == lower
            && (!excludeId.HasValue || p.Id != excludeId.Value), ct);
#pragma warning restore CA1304, CA1311, CA1862
    }

    public Task AddPositionAsync(Position position, CancellationToken ct)
    {
        db.Positions.Add(position);
        return Task.CompletedTask;
    }

    Task IBasTenantRepository.UpdatePositionAsync(Position position, CancellationToken ct)
    {
        db.Positions.Update(position);
        return Task.CompletedTask;
    }

    Task IBasTenantRepository.RemovePositionAsync(Position position, CancellationToken ct)
    {
        db.Positions.Remove(position);
        return Task.CompletedTask;
    }

    // ============ Goods：商品分类 ============

    public async Task<IReadOnlyList<ItemCategory>> ListAllCategoriesAsync(CancellationToken ct)
        => await db.ItemCategories.AsNoTracking()
            .OrderBy(c => c.Code)
            .ToListAsync(ct);

    public Task<ItemCategory?> FindCategoryAsync(long id, CancellationToken ct)
        => db.ItemCategories.FindAsync([id], ct).AsTask();

    public Task<bool> IsCategoryCodeExistsAsync(string code, CancellationToken ct)
        => db.ItemCategories.AnyAsync(c => c.Code == code, ct);

    public Task<bool> HasChildCategoriesAsync(long parentId, CancellationToken ct)
        => db.ItemCategories.AnyAsync(c => c.ParentId == parentId, ct);

    public Task AddCategoryAsync(ItemCategory category, CancellationToken ct)
    {
        db.ItemCategories.Add(category);
        return Task.CompletedTask;
    }

    public Task UpdateCategoryAsync(ItemCategory category, CancellationToken ct)
    {
        db.ItemCategories.Update(category);
        return Task.CompletedTask;
    }

    // ============ Goods：计量单位组 ============

    public async Task<IReadOnlyList<UnitGroup>> ListAllUnitGroupsAsync(CancellationToken ct)
        => await db.UnitGroups.AsNoTracking()
            .OrderBy(g => g.Code)
            .ToListAsync(ct);

    public Task<UnitGroup?> FindUnitGroupAsync(long id, CancellationToken ct)
        => db.UnitGroups.FindAsync([id], ct).AsTask();

    public Task<bool> IsUnitGroupCodeExistsAsync(string code, CancellationToken ct)
        => db.UnitGroups.AnyAsync(g => g.Code == code, ct);

    public Task AddUnitGroupAsync(UnitGroup group, CancellationToken ct)
    {
        db.UnitGroups.Add(group);
        return Task.CompletedTask;
    }

    public Task UpdateUnitGroupAsync(UnitGroup group, CancellationToken ct)
    {
        db.UnitGroups.Update(group);
        return Task.CompletedTask;
    }

    // ============ Goods：计量单位 ============

    public async Task<IReadOnlyList<Unit>> ListAllUnitsAsync(CancellationToken ct)
        => await db.Units.AsNoTracking()
            .OrderBy(u => u.UnitGroupId).ThenBy(u => u.Code)
            .ToListAsync(ct);

    public Task<Unit?> FindUnitAsync(long id, CancellationToken ct)
        => db.Units.FindAsync([id], ct).AsTask();

    public Task<bool> IsUnitCodeExistsAsync(string code, CancellationToken ct)
        => db.Units.AnyAsync(u => u.Code == code, ct);

    public Task<int> CountUnitsInGroupAsync(long unitGroupId, CancellationToken ct)
        => db.Units.CountAsync(u => u.UnitGroupId == unitGroupId, ct);

    public Task AddUnitAsync(Unit unit, CancellationToken ct)
    {
        db.Units.Add(unit);
        return Task.CompletedTask;
    }

    public Task UpdateUnitAsync(Unit unit, CancellationToken ct)
    {
        db.Units.Update(unit);
        return Task.CompletedTask;
    }

    // ============ Goods：单位换算 ============

    public async Task<IReadOnlyList<UnitConversion>> ListAllUnitConversionsAsync(CancellationToken ct)
        => await db.UnitConversions.AsNoTracking()
            .OrderBy(c => c.UnitGroupId).ThenBy(c => c.FromUnitId).ThenBy(c => c.ToUnitId)
            .ToListAsync(ct);

    public Task<UnitConversion?> FindUnitConversionAsync(long id, CancellationToken ct)
        => db.UnitConversions.FindAsync([id], ct).AsTask();

    public async Task<bool> IsUnitConversionExistsAsync(
        long fromUnitId, long toUnitId, long? excludeId, CancellationToken ct)
        => await db.UnitConversions.AnyAsync(c =>
            c.FromUnitId == fromUnitId && c.ToUnitId == toUnitId
            && (!excludeId.HasValue || c.Id != excludeId.Value), ct);

    public Task AddUnitConversionAsync(UnitConversion conversion, CancellationToken ct)
    {
        db.UnitConversions.Add(conversion);
        return Task.CompletedTask;
    }

    public Task UpdateUnitConversionAsync(UnitConversion conversion, CancellationToken ct)
    {
        db.UnitConversions.Update(conversion);
        return Task.CompletedTask;
    }

    // ============ Supplier（供应商主表 + 子表）============

    public async Task<(IReadOnlyList<Domain.Supplier.Supplier> Items, int Total)> QuerySuppliersAsync(
        int? grade, int? status, string? keyword, int skip, int take, CancellationToken ct)
    {
        IQueryable<Domain.Supplier.Supplier> q = db.Suppliers.AsNoTracking();
        if (grade.HasValue) q = q.Where(s => (int)s.Grade == grade.Value);
        if (status.HasValue) q = q.Where(s => (int)s.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            q = q.Where(s =>
                s.Name.Contains(keyword) || s.Code.Contains(keyword) || s.Uscc.Contains(keyword));
        }

        int total = await q.CountAsync(ct);
        List<Domain.Supplier.Supplier> items = await q.OrderBy(s => s.Code)
            .Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }

    public Task<Domain.Supplier.Supplier?> FindSupplierAsync(long id, CancellationToken ct)
        => db.Suppliers.FindAsync([id], ct).AsTask();

    public async Task<IReadOnlyList<Domain.Supplier.Supplier>> ListActiveSuppliersAsync(CancellationToken ct)
        => await db.Suppliers.AsNoTracking()
            .Where(s => s.Status == PartnerStatus.Active)
            .OrderBy(s => s.Code)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Domain.Supplier.Supplier>> ListSuppliersByIdsAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Suppliers.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .ToListAsync(ct);
    }

    public Task<bool> IsSupplierCodeExistsAsync(string code, CancellationToken ct)
        => db.Suppliers.AnyAsync(s => s.Code == code, ct);

    public Task<bool> IsSupplierUsccExistsAsync(string uscc, long? excludeId, CancellationToken ct)
        => db.Suppliers.AnyAsync(s =>
            s.Uscc == uscc && (!excludeId.HasValue || s.Id != excludeId.Value), ct);

    public Task AddSupplierAsync(Domain.Supplier.Supplier supplier, CancellationToken ct)
    {
        db.Suppliers.Add(supplier);
        return Task.CompletedTask;
    }

    public Task UpdateSupplierAsync(Domain.Supplier.Supplier supplier, CancellationToken ct)
    {
        db.Suppliers.Update(supplier);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Domain.Supplier.SupplierCert>> ListSupplierCertsAsync(
        long supplierId, CancellationToken ct)
        => await db.SupplierCerts.AsNoTracking()
            .Where(c => c.SupplierId == supplierId)
            .OrderBy(c => c.ExpiryDate).ThenBy(c => c.Id)
            .ToListAsync(ct);

    public Task<Domain.Supplier.SupplierCert?> FindSupplierCertAsync(long id, CancellationToken ct)
        => db.SupplierCerts.FindAsync([id], ct).AsTask();

    public Task AddSupplierCertAsync(Domain.Supplier.SupplierCert cert, CancellationToken ct)
    {
        db.SupplierCerts.Add(cert);
        return Task.CompletedTask;
    }

    public Task UpdateSupplierCertAsync(Domain.Supplier.SupplierCert cert, CancellationToken ct)
    {
        db.SupplierCerts.Update(cert);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Domain.Supplier.SupplierCert>> ListSupplierCertsForScanAsync(
        long afterId, int take, CancellationToken ct)
        => await db.SupplierCerts.AsNoTracking()
            .Where(c => c.Id > afterId
                && c.Status == Domain.Supplier.CertificateStatus.Valid
                && c.ExpiryDate != null)
            .OrderBy(c => c.Id)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Domain.Supplier.SupplierBank>> ListSupplierBanksAsync(
        long supplierId, CancellationToken ct)
        => await db.SupplierBanks.AsNoTracking()
            .Where(b => b.SupplierId == supplierId)
            .OrderByDescending(b => b.IsDefault).ThenBy(b => b.Id)
            .ToListAsync(ct);

    public Task AddSupplierBankAsync(Domain.Supplier.SupplierBank bank, CancellationToken ct)
    {
        db.SupplierBanks.Add(bank);
        return Task.CompletedTask;
    }

    public Task UpdateSupplierBankAsync(Domain.Supplier.SupplierBank bank, CancellationToken ct)
    {
        db.SupplierBanks.Update(bank);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Domain.Supplier.SupplierInvoice>> ListSupplierInvoicesAsync(
        long supplierId, CancellationToken ct)
        => await db.SupplierInvoices.AsNoTracking()
            .Where(i => i.SupplierId == supplierId)
            .OrderBy(i => i.Id)
            .ToListAsync(ct);

    public Task AddSupplierInvoiceAsync(Domain.Supplier.SupplierInvoice invoice, CancellationToken ct)
    {
        db.SupplierInvoices.Add(invoice);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Domain.Supplier.SupplierGradeLog>> ListSupplierGradeLogsAsync(
        long supplierId, CancellationToken ct)
        => await db.SupplierGradeLogs.AsNoTracking()
            .Where(l => l.SupplierId == supplierId)
            .OrderByDescending(l => l.Id)
            .ToListAsync(ct);

    public Task AddSupplierGradeLogAsync(Domain.Supplier.SupplierGradeLog log, CancellationToken ct)
    {
        db.SupplierGradeLogs.Add(log);
        return Task.CompletedTask;
    }

    // ============ Customer（客户主表 + 子表）============

    public async Task<(IReadOnlyList<Domain.Customer.Customer> Items, int Total)> QueryCustomersAsync(
        int? creditGrade, int? status, string? keyword, int skip, int take, CancellationToken ct)
    {
        IQueryable<Domain.Customer.Customer> q = db.Customers.AsNoTracking();
        if (creditGrade.HasValue) q = q.Where(c => (int)c.CreditGrade == creditGrade.Value);
        if (status.HasValue) q = q.Where(c => (int)c.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            q = q.Where(c =>
                c.Name.Contains(keyword) || c.Code.Contains(keyword) || c.Uscc.Contains(keyword));
        }

        int total = await q.CountAsync(ct);
        List<Domain.Customer.Customer> items = await q.OrderBy(c => c.Code)
            .Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }

    public Task<Domain.Customer.Customer?> FindCustomerAsync(long id, CancellationToken ct)
        => db.Customers.FindAsync([id], ct).AsTask();

    public Task<bool> IsCustomerCodeExistsAsync(string code, CancellationToken ct)
        => db.Customers.AnyAsync(c => c.Code == code, ct);

    public Task<bool> IsCustomerUsccExistsAsync(string uscc, long? excludeId, CancellationToken ct)
        => db.Customers.AnyAsync(c =>
            c.Uscc == uscc && (!excludeId.HasValue || c.Id != excludeId.Value), ct);

    public Task AddCustomerAsync(Domain.Customer.Customer customer, CancellationToken ct)
    {
        db.Customers.Add(customer);
        return Task.CompletedTask;
    }

    public Task UpdateCustomerAsync(Domain.Customer.Customer customer, CancellationToken ct)
    {
        db.Customers.Update(customer);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Domain.Customer.CustomerBank>> ListCustomerBanksAsync(
        long customerId, CancellationToken ct)
        => await db.CustomerBanks.AsNoTracking()
            .Where(b => b.CustomerId == customerId)
            .OrderByDescending(b => b.IsDefault).ThenBy(b => b.Id)
            .ToListAsync(ct);

    public Task AddCustomerBankAsync(Domain.Customer.CustomerBank bank, CancellationToken ct)
    {
        db.CustomerBanks.Add(bank);
        return Task.CompletedTask;
    }

    public Task UpdateCustomerBankAsync(Domain.Customer.CustomerBank bank, CancellationToken ct)
    {
        db.CustomerBanks.Update(bank);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Domain.Customer.CustomerInvoice>> ListCustomerInvoicesAsync(
        long customerId, CancellationToken ct)
        => await db.CustomerInvoices.AsNoTracking()
            .Where(i => i.CustomerId == customerId)
            .OrderBy(i => i.Id)
            .ToListAsync(ct);

    public Task AddCustomerInvoiceAsync(Domain.Customer.CustomerInvoice invoice, CancellationToken ct)
    {
        db.CustomerInvoices.Add(invoice);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Domain.Customer.CustomerCreditLog>> ListCustomerCreditLogsAsync(
        long customerId, CancellationToken ct)
        => await db.CustomerCreditLogs.AsNoTracking()
            .Where(l => l.CustomerId == customerId)
            .OrderByDescending(l => l.Id)
            .ToListAsync(ct);

    public Task AddCustomerCreditLogAsync(Domain.Customer.CustomerCreditLog log, CancellationToken ct)
    {
        db.CustomerCreditLogs.Add(log);
        return Task.CompletedTask;
    }

    // ============ Person（人员主表 + 任职记录 + 证件档案）============

    public async Task<(IReadOnlyList<Domain.Person.Person> Items, int Total)> QueryPersonsAsync(
        int? personType,
        int? employStatus,
        long? orgId,
        long? positionId,
        string? keyword,
        int skip,
        int take,
        CancellationToken ct)
    {
        IQueryable<Domain.Person.Person> q = db.Persons.AsNoTracking();
        if (personType.HasValue) q = q.Where(p => (int)p.PersonType == personType.Value);
        if (employStatus.HasValue) q = q.Where(p => (int)p.EmployStatus == employStatus.Value);
        if (orgId.HasValue) q = q.Where(p => p.OrgId == orgId.Value);
        if (positionId.HasValue) q = q.Where(p => p.PositionId == positionId.Value);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            q = q.Where(p => p.Name.Contains(keyword) || p.Code.Contains(keyword));
        }

        int total = await q.CountAsync(ct);
        List<Domain.Person.Person> items = await q.OrderBy(p => p.Code)
            .Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }

    public Task<Domain.Person.Person?> FindPersonAsync(long id, CancellationToken ct)
        => db.Persons.FindAsync([id], ct).AsTask();

    public Task<bool> IsPersonCodeExistsAsync(string code, CancellationToken ct)
        => db.Persons.AnyAsync(p => p.Code == code, ct);

    public Task<bool> IsPersonIdNoExistsAsync(string idNoHash, long? excludeId, CancellationToken ct)
        => db.Persons.AnyAsync(p =>
            p.IdNoHash == idNoHash && (!excludeId.HasValue || p.Id != excludeId.Value), ct);

    public Task AddPersonAsync(Domain.Person.Person person, CancellationToken ct)
    {
        db.Persons.Add(person);
        return Task.CompletedTask;
    }

    public Task UpdatePersonAsync(Domain.Person.Person person, CancellationToken ct)
    {
        db.Persons.Update(person);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Domain.Person.Person>> ListActivePersonsByPositionAsync(
        long positionId, CancellationToken ct)
        => await db.Persons.AsNoTracking()
            .Where(p => p.PositionId == positionId
                && p.EmployStatus != Domain.Person.EmployStatus.Left)
            .OrderBy(p => p.Code)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<long, int>> CountActivePersonsByPositionsAsync(
        IReadOnlyCollection<long> positionIds, CancellationToken ct)
    {
        if (positionIds.Count == 0)
        {
            return new Dictionary<long, int>();
        }

        // 单条 GROUP BY 聚合（禁 N+1）；PositionId 为 null 的劳务人员不计入任何岗位
        var rows = await db.Persons.AsNoTracking()
            .Where(p => p.PositionId != null
                && positionIds.Contains(p.PositionId!.Value)
                && p.EmployStatus != Domain.Person.EmployStatus.Left)
            .GroupBy(p => p.PositionId!.Value)
            .Select(g => new { PositionId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows.ToDictionary(static r => r.PositionId, static r => r.Count);
    }

    public async Task<bool> HasActivePersonsUnderOrgsAsync(
        IReadOnlyCollection<long> orgIds, CancellationToken ct)
    {
        if (orgIds.Count == 0)
        {
            return false;
        }

        return await db.Persons.AsNoTracking()
            .AnyAsync(p => p.OrgId != null
                && orgIds.Contains(p.OrgId!.Value)
                && p.EmployStatus != Domain.Person.EmployStatus.Left, ct);
    }

    public async Task<IReadOnlyList<Domain.Person.PersonAssignment>> ListPersonAssignmentsAsync(
        long personId, CancellationToken ct)
        // 跟踪态读取：调岗/离职会 Close 当前任职后统一保存；已跟踪实例由标识解析复用，避免同键冲突
        => await db.PersonAssignments
            .Where(a => a.PersonId == personId)
            .OrderByDescending(a => a.StartDate).ThenByDescending(a => a.Id)
            .ToListAsync(ct);

    public Task AddPersonAssignmentAsync(
        Domain.Person.PersonAssignment assignment, CancellationToken ct)
    {
        db.PersonAssignments.Add(assignment);
        return Task.CompletedTask;
    }

    public Task UpdatePersonAssignmentAsync(
        Domain.Person.PersonAssignment assignment, CancellationToken ct)
    {
        db.PersonAssignments.Update(assignment);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Domain.Person.PersonCert>> ListPersonCertsAsync(
        long personId, CancellationToken ct)
        => await db.PersonCerts.AsNoTracking()
            .Where(c => c.PersonId == personId)
            .OrderBy(c => c.ExpiresOn).ThenBy(c => c.Id)
            .ToListAsync(ct);

    public Task<Domain.Person.PersonCert?> FindPersonCertAsync(long id, CancellationToken ct)
        => db.PersonCerts.FindAsync([id], ct).AsTask();

    public Task AddPersonCertAsync(Domain.Person.PersonCert cert, CancellationToken ct)
    {
        db.PersonCerts.Add(cert);
        return Task.CompletedTask;
    }

    public Task UpdatePersonCertAsync(Domain.Person.PersonCert cert, CancellationToken ct)
    {
        db.PersonCerts.Update(cert);
        return Task.CompletedTask;
    }

    public Task RemovePersonCertAsync(Domain.Person.PersonCert cert, CancellationToken ct)
    {
        // 证件档案无业务引用，硬删（审计快照由服务层先行采集）
        db.PersonCerts.Remove(cert);
        return Task.CompletedTask;
    }

    // ============ 导入导出（批量校验 / 批量读取）============

    public async Task<IReadOnlyCollection<string>> ListExistingSupplierUsccsAsync(
        IReadOnlyCollection<string> usccs, CancellationToken ct)
    {
        if (usccs.Count == 0)
        {
            return [];
        }

        return await db.Suppliers.AsNoTracking()
            .Where(s => usccs.Contains(s.Uscc))
            .Select(s => s.Uscc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyCollection<string>> ListExistingCustomerUsccsAsync(
        IReadOnlyCollection<string> usccs, CancellationToken ct)
    {
        if (usccs.Count == 0)
        {
            return [];
        }

        return await db.Customers.AsNoTracking()
            .Where(c => usccs.Contains(c.Uscc))
            .Select(c => c.Uscc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyCollection<string>> ListExistingPersonIdNoHashesAsync(
        IReadOnlyCollection<string> idNoHashes, CancellationToken ct)
    {
        if (idNoHashes.Count == 0)
        {
            return [];
        }

        return await db.Persons.AsNoTracking()
            .Where(p => idNoHashes.Contains(p.IdNoHash))
            .Select(p => p.IdNoHash)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Domain.Supplier.Supplier>> ListSuppliersForExportAsync(
        int? status, string? keyword, CancellationToken ct)
    {
        IQueryable<Domain.Supplier.Supplier> q = db.Suppliers.AsNoTracking();
        if (status.HasValue)
        {
            q = q.Where(s => (int)s.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            q = q.Where(s => s.Name.Contains(keyword) || s.Code.Contains(keyword));
        }

        return await q.OrderBy(s => s.Code).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Domain.Customer.Customer>> ListCustomersForExportAsync(
        int? status, string? keyword, CancellationToken ct)
    {
        IQueryable<Domain.Customer.Customer> q = db.Customers.AsNoTracking();
        if (status.HasValue)
        {
            q = q.Where(c => (int)c.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            q = q.Where(c => c.Name.Contains(keyword) || c.Code.Contains(keyword));
        }

        return await q.OrderBy(c => c.Code).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Domain.Person.Person>> ListPersonsForExportAsync(
        int? personType, int? employStatus, string? keyword, CancellationToken ct)
    {
        IQueryable<Domain.Person.Person> q = db.Persons.AsNoTracking();
        if (personType.HasValue)
        {
            q = q.Where(p => (int)p.PersonType == personType.Value);
        }

        if (employStatus.HasValue)
        {
            q = q.Where(p => (int)p.EmployStatus == employStatus.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            q = q.Where(p => p.Name.Contains(keyword) || p.Code.Contains(keyword));
        }

        return await q.OrderBy(p => p.Code).ToListAsync(ct);
    }

    // ============ Role ============

    public async Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken ct)
        => await db.Roles.AsNoTracking().OrderBy(r => r.IsBuiltin).ThenBy(r => r.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<(Role Role, int MemberCount, int PermissionCount)>> ListRolesWithCountsAsync(CancellationToken ct)
    {
        // 单次往返：相关标量子查询投影为 SELECT ..., (SELECT COUNT...)（禁 N+1）；
        // 双 GroupJoin 投影在部分提供程序（Sqlite）不可翻译，子查询形式全提供程序可用。
        // MemberRoles/RolePermissions 与 Roles 同租户全局过滤
        var rows = await db.Roles.AsNoTracking()
            .OrderBy(r => r.IsBuiltin).ThenBy(r => r.Name)
            .Select(r => new
            {
                Role = r,
                MemberCount = db.MemberRoles.Count(mr => mr.RoleId == r.Id),
                PermissionCount = db.RolePermissions.Count(rp => rp.RoleId == r.Id),
            })
            .ToListAsync(ct);
        return [.. rows.Select(x => (x.Role, x.MemberCount, x.PermissionCount))];
    }

    public async Task<(int MemberCount, int PermissionCount)> GetRoleCountsAsync(long roleId, CancellationToken ct)
    {
        int memberCount = await db.MemberRoles.CountAsync(mr => mr.RoleId == roleId, ct);
        int permissionCount = await db.RolePermissions.CountAsync(rp => rp.RoleId == roleId, ct);
        return (memberCount, permissionCount);
    }

    public Task<Role?> FindRoleAsync(long id, CancellationToken ct)
        => db.Roles.FindAsync([id], ct).AsTask();

    public async Task<bool> IsRoleCodeExistsAsync(string code, long? excludeId, CancellationToken ct)
    {
        // 注：EF Core 翻译限制——string.Equals + StringComparison / ToLowerInvariant 均不可翻译，
        // ToLower() 触发全球化分析器（CA1304/CA1311/CA1862），但 SQL LOWER 本身是 CI，语义正确
#pragma warning disable CA1304, CA1311, CA1862
        var lower = code.ToLower();
        return await db.Roles.AnyAsync(r =>
            r.Code.ToLower() == lower
            && (!excludeId.HasValue || r.Id != excludeId.Value), ct);
#pragma warning restore CA1304, CA1311, CA1862
    }

    public Task AddRoleAsync(Role role, CancellationToken ct) { db.Roles.Add(role); return Task.CompletedTask; }
    public Task UpdateRoleAsync(Role role, CancellationToken ct) { db.Roles.Update(role); return Task.CompletedTask; }
    public Task RemoveRoleAsync(Role role, CancellationToken ct) { db.Roles.Remove(role); return Task.CompletedTask; }

    // ============ RolePermission ============

    public async Task<IReadOnlyList<string>> ListRolePermissionCodesAsync(long roleId, CancellationToken ct)
        => await db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.PermissionCode)
            .ToListAsync(ct);

    public async Task ReplaceRolePermissionsAsync(long roleId, long tenantId, IReadOnlyList<string> codes, CancellationToken ct)
    {
        // Entity 基类（无审计字段 / 无软删），ExecuteDelete 绕过拦截器语义一致；TenantDbContext 全局 TenantId 过滤自动生效
        await db.RolePermissions.Where(rp => rp.RoleId == roleId).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var distinct = codes.Distinct(StringComparer.Ordinal).Select(code => new RolePermission(roleId, tenantId, code)).ToList();
        if (distinct.Count > 0)
        {
            await db.RolePermissions.AddRangeAsync(distinct, ct).ConfigureAwait(false);
        }
    }

    // ============ MemberRole ============

    public async Task<IReadOnlyList<long>> ListRoleMemberIdsAsync(long roleId, CancellationToken ct)
        => await db.MemberRoles.AsNoTracking()
            .Where(mr => mr.RoleId == roleId)
            .Select(mr => mr.TenantMemberId).ToListAsync(ct);

    public async Task<IReadOnlyList<string>> ListMemberRoleCodesAsync(long tenantMemberId, CancellationToken ct)
    {
        var roleIds = await db.MemberRoles.AsNoTracking()
            .Where(mr => mr.TenantMemberId == tenantMemberId)
            .Select(mr => mr.RoleId).ToListAsync(ct);
        if (roleIds.Count == 0) return [];

        return await db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Code).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Role>> ListMemberRolesAsync(long tenantMemberId, CancellationToken ct)
    {
        var roleIds = await db.MemberRoles.AsNoTracking()
            .Where(mr => mr.TenantMemberId == tenantMemberId)
            .Select(mr => mr.RoleId).ToListAsync(ct);
        if (roleIds.Count == 0) return [];

        return await db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id) && !r.IsDeleted)
            .ToListAsync(ct);
    }

    public async Task ReplaceMemberRolesAsync(
        long tenantMemberId, long tenantId, IReadOnlyList<(long RoleId, string RoleCode)> roles, CancellationToken ct)
    {
        // Entity 基类（无审计字段 / 无软删），ExecuteDelete 绕过拦截器语义一致；TenantDbContext 全局 TenantId 过滤自动生效
        await db.MemberRoles.Where(mr => mr.TenantMemberId == tenantMemberId).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var distinct = roles.DistinctBy(x => x.RoleId).Select(x => new MemberRole(tenantMemberId, x.RoleId, tenantId)).ToList();
        if (distinct.Count > 0)
        {
            await db.MemberRoles.AddRangeAsync(distinct, ct).ConfigureAwait(false);
        }
    }

    public async Task RemoveMemberRoleLinksAsync(long tenantMemberId, CancellationToken ct)
        => await db.MemberRoles.Where(mr => mr.TenantMemberId == tenantMemberId).ExecuteDeleteAsync(ct).ConfigureAwait(false);

    public async Task RemoveMemberRoleLinksByRoleAsync(long roleId, CancellationToken ct)
        => await db.MemberRoles.Where(mr => mr.RoleId == roleId).ExecuteDeleteAsync(ct).ConfigureAwait(false);

    public async Task ReplaceRoleMemberLinksAsync(
        long roleId, IReadOnlyList<long> tenantMemberIds, long tenantId, CancellationToken ct)
    {
        // Entity 基类（无审计字段 / 无软删），ExecuteDelete 绕过拦截器语义一致；TenantDbContext 全局 TenantId 过滤自动生效
        await db.MemberRoles.Where(mr => mr.RoleId == roleId).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var distinct = tenantMemberIds.Distinct().Select(memberId => new MemberRole(memberId, roleId, tenantId)).ToList();
        if (distinct.Count > 0)
        {
            await db.MemberRoles.AddRangeAsync(distinct, ct).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<(long TenantMemberId, long RoleId, string RoleCode)>> ListAllMemberRoleCodesFlatAsync(
        long tenantId, CancellationToken ct)
    {
        var raw = await (
            from mr in db.MemberRoles.AsNoTracking()
            join r in db.Roles.AsNoTracking() on mr.RoleId equals r.Id
            where mr.TenantId == tenantId
            select new { mr.TenantMemberId, mr.RoleId, RoleCode = r.Code })
            .ToListAsync(ct);
        return [.. raw.Select(x => (x.TenantMemberId, x.RoleId, x.RoleCode))];
    }

    // ============ 登录日志（租户库 log_login）============

    public async Task<(IReadOnlyList<TenantLogLoginEntity> Items, int Total)> QueryLoginLogsAsync(
        string loginAccount, int skip, int take, CancellationToken ct)
    {
        // TenantLogDbContext 基类全局 TenantId 过滤自动生效，仅需按账号过滤；
        // EventId 为雪花 Id（时间单调递增），按 EventId 倒序与按 LoginAt 倒序语义等价，
        // 且规避 Sqlite 不支持 DateTimeOffset 排序的提供程序差异（SQL Server 两者皆可）
        IQueryable<TenantLogLoginEntity> q = logDb.LoginLogs.AsNoTracking()
            .Where(l => l.LoginAccount == loginAccount);
        int total = await q.CountAsync(ct);
        List<TenantLogLoginEntity> items = await q.OrderByDescending(l => l.EventId)
            .Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }
}
