/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 19:10:00 +08:00 中国标准时间 星期日
 * 描述：供应商主数据服务实现——统一发号器编码 + 状态机守卫 + 子表维护 + 审批挂起/应用 +
 *       脱敏审计 + 失效优先缓存（写路径只删缓存，读路径 GetOrCreateAsync）
 */

using System.Globalization;
using System.Text.Json;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Repositories;
using Qlx.Modules.Bas.Services.Numbering;

using SupplierGrade = Qlx.Modules.Bas.Domain.Supplier.SupplierGrade;
using SupplierType = Qlx.Modules.Bas.Domain.Supplier.SupplierType;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 供应商主数据服务实现。编码经 <see cref="IBasCodeNumberGenerator"/> 生成（N-1，前端不传码）；
/// 状态流转经领域状态机守卫（非法流转抛 <see cref="ConflictException"/>）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="codeNumberGenerator">统一发号器（供应商编码）。</param>
/// <param name="approvalService">审批中心契约（准入 / 银行变更申请）。</param>
/// <param name="cache">统一缓存服务。</param>
/// <param name="audit">审计服务。</param>
/// <param name="snowflake">雪花 Id 生成器（审计事件 Id）。</param>
/// <param name="userContext">当前用户上下文（审计变更人）。</param>
internal sealed class SupplierService(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    IBasCodeNumberGenerator codeNumberGenerator,
    IApprovalService approvalService,
    ICacheService cache,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake,
    ICurrentUserContext userContext) : ISupplierService
{
    /// <summary>供应商准入审批业务类型。</summary>
    internal const string AdmissionBizType = "bas.supplier.admission";

    /// <summary>供应商银行账户变更审批业务类型。</summary>
    internal const string BankChangeBizType = "bas.supplier.bank.change";

    private const string CodeBizType = "supplier";

    private static readonly CacheEntryOptions s_cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        NullValueExpiration = TimeSpan.FromSeconds(30),
    };

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    private string ChangedBy => userContext.UserId ?? "system";

    // ============ 读路径 ============

    /// <inheritdoc />
    public async Task<PagedResult<SupplierRow>> ListAsync(
        int? grade, int? status, string? keyword, int page, int pageSize, CancellationToken ct)
    {
        int safePage = Math.Max(page, 1);
        int safeSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 200);
        int skip = (safePage - 1) * safeSize;

        (IReadOnlyList<Domain.Supplier.Supplier> Items, int Total) result = await repo
            .QuerySuppliersAsync(grade, status, keyword?.Trim(), skip, safeSize, ct)
            .ConfigureAwait(false);

        return new PagedResult<SupplierRow>(
            [.. result.Items.Select(Map)], result.Total, safePage, safeSize);
    }

    /// <inheritdoc />
    public async Task<SupplierDetailRow> GetAsync(long id, CancellationToken ct)
    {
        SupplierRow master = await GetMasterAsync(id, ct).ConfigureAwait(false);

        IReadOnlyList<Domain.Supplier.SupplierCert> certs = await repo
            .ListSupplierCertsAsync(id, ct).ConfigureAwait(false);
        IReadOnlyList<Domain.Supplier.SupplierBank> banks = await repo
            .ListSupplierBanksAsync(id, ct).ConfigureAwait(false);
        IReadOnlyList<Domain.Supplier.SupplierInvoice> invoices = await repo
            .ListSupplierInvoicesAsync(id, ct).ConfigureAwait(false);
        IReadOnlyList<Domain.Supplier.SupplierGradeLog> gradeLogs = await repo
            .ListSupplierGradeLogsAsync(id, ct).ConfigureAwait(false);

        return new SupplierDetailRow(
            master,
            [.. certs.Select(Map)],
            [.. banks.Select(Map)],
            [.. invoices.Select(Map)],
            [.. gradeLogs.Select(Map)]);
    }

    // ============ 写路径 ============

    /// <inheritdoc />
    public async Task<SupplierRow> CreateAsync(SaveSupplierPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        SupplierRow row = await repo.ExecuteInTransactionAsync(
            token => CreateCoreAsync(payload, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync("INSERT", nameof(Domain.Supplier.Supplier), row.Id, null, Serialize(row), ct)
            .ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return row;
    }

    /// <inheritdoc />
    public async Task<SupplierRow> UpdateAsync(
        long id, SaveSupplierPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        (SupplierRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(
            token => UpdateCoreAsync(id, payload, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync(
            "UPDATE", nameof(Domain.Supplier.Supplier), result.Row.Id, result.OldJson, Serialize(result.Row), ct)
            .ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task<SupplierRow> ChangeGradeAsync(
        long id, ChangeSupplierGradePayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        SupplierGrade newGrade = ParseGrade(payload.Grade);

        (SupplierRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(
            async token =>
            {
                Domain.Supplier.Supplier supplier = await FindSupplierOrThrowAsync(id, token).ConfigureAwait(false);
                string oldJson = Serialize(Map(supplier));
                SupplierGrade oldGrade = supplier.Grade;

                supplier.ChangeGrade(newGrade);
                await repo.AddSupplierGradeLogAsync(
                    new Domain.Supplier.SupplierGradeLog(
                        TenantId, supplier.Id, oldGrade, newGrade, payload.Reason, ChangedBy),
                    token).ConfigureAwait(false);

                await repo.UpdateSupplierAsync(supplier, token).ConfigureAwait(false);
                _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(supplier), oldJson);
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync(
            "UPDATE", nameof(Domain.Supplier.Supplier), result.Row.Id, result.OldJson, Serialize(result.Row), ct)
            .ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task EnsurePurchasableAsync(long id, CancellationToken ct)
    {
        Domain.Supplier.Supplier supplier = await FindSupplierOrThrowAsync(id, ct).ConfigureAwait(false);

        if (supplier.Status != PartnerStatus.Active)
        {
            throw new ConflictException(
                "供应商未处于合作中（待准入 / 冻结 / 终止），不可采购", "SUPPLIER_NOT_ACTIVE");
        }

        if (supplier.Grade == SupplierGrade.Blacklist)
        {
            throw new ConflictException("供应商处于黑名单，全局禁止采购", "SUPPLIER_BLACKLISTED");
        }
    }

    // ============ 审批挂起与应用 ============

    /// <inheritdoc />
    public async Task<ApprovalRequestRow> SubmitAdmissionRequestAsync(
        long id, string? reason, CancellationToken ct)
    {
        Domain.Supplier.Supplier supplier = await FindSupplierOrThrowAsync(id, ct).ConfigureAwait(false);
        if (supplier.Status == PartnerStatus.Closed)
        {
            throw new ConflictException("已终止供应商不可再次准入", "SUPPLIER_STATE_INVALID");
        }

        if (supplier.Status == PartnerStatus.Active)
        {
            throw new ConflictException("供应商已在合作中，无需重复准入", "SUPPLIER_STATE_INVALID");
        }

        BasApprovalRequest request = new(
            TenantId,
            AdmissionBizType,
            JsonSerializer.Serialize(
                new SupplierAdmissionPayload(supplier.Id, reason), QlxJsonDefaults.PreserveNull));
        await repo.AddApprovalRequestAsync(request, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        string instanceId = await approvalService.StartApprovalAsync(
            new StartApprovalRequest(
                ApprovalDefinitionCode: AdmissionBizType,
                BizType: AdmissionBizType,
                BizId: request.Id.ToString(CultureInfo.InvariantCulture),
                BizNo: supplier.Code,
                InitiatorUserId: ChangedBy,
                Title: $"供应商准入申请：{supplier.Name}",
                Description: reason,
                BizData: new Dictionary<string, object?>
                {
                    ["supplierId"] = supplier.Id,
                }),
            ct).ConfigureAwait(false);

        request.BindInstance(instanceId);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(request);
    }

    /// <inheritdoc />
    public async Task<SupplierAdmissionApplied> ApplyAdmissionAsync(
        SupplierAdmissionPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Domain.Supplier.Supplier supplier = await FindSupplierOrThrowAsync(payload.SupplierId, ct)
            .ConfigureAwait(false);

        string oldJson = Serialize(Map(supplier));
        if (supplier.Status != PartnerStatus.Active)
        {
            supplier.Approve();
        }

        await repo.UpdateSupplierAsync(supplier, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        SupplierRow row = Map(supplier);
        await CaptureAuditAsync(
            "UPDATE", nameof(Domain.Supplier.Supplier), row.Id, oldJson, Serialize(row), ct)
            .ConfigureAwait(false);

        return new SupplierAdmissionApplied(row.Id, row.Code, row.Grade);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SupplierCertRow>> ListCertsAsync(long id, CancellationToken ct)
        => ListCertsCoreAsync(id, ct);

    /// <inheritdoc />
    public async Task<SupplierCertRow> SaveCertAsync(
        long id, SaveSupplierCertPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        _ = await FindSupplierOrThrowAsync(id, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(payload.CertType))
        {
            throw new ValidationException("certType", "资质类型不能为空");
        }

        if (string.IsNullOrWhiteSpace(payload.CertNo))
        {
            throw new ValidationException("certNo", "资质编号不能为空");
        }

        Domain.Supplier.SupplierCert cert = new(
            TenantId,
            id,
            payload.CertType.Trim(),
            payload.CertNo.Trim(),
            payload.IssueOrg?.Trim(),
            payload.EffectDate,
            payload.ExpiryDate,
            payload.FileId);

        await repo.AddSupplierCertAsync(cert, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        SupplierCertRow row = Map(cert);
        await CaptureAuditAsync(
            "INSERT", nameof(Domain.Supplier.SupplierCert), row.Id, null, Serialize(row), ct)
            .ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return row;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupplierBankRow>> ListBanksAsync(long id, CancellationToken ct)
    {
        _ = await FindSupplierOrThrowAsync(id, ct).ConfigureAwait(false);
        IReadOnlyList<Domain.Supplier.SupplierBank> banks = await repo
            .ListSupplierBanksAsync(id, ct).ConfigureAwait(false);
        return [.. banks.Select(Map)];
    }

    /// <inheritdoc />
    public async Task<SupplierBankRow> SaveBankAsync(
        long id, SaveSupplierBankPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        SupplierBankRow row = await repo.ExecuteInTransactionAsync(
            token => CreateBankCoreAsync(id, payload, token), ct).ConfigureAwait(false);

        await CaptureBankAuditAsync("INSERT", row, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return row;
    }

    /// <inheritdoc />
    public async Task<ApprovalRequestRow> SubmitBankChangeRequestAsync(
        long id, SaveSupplierBankPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidateBankPayload(payload);

        Domain.Supplier.Supplier supplier = await FindSupplierOrThrowAsync(id, ct).ConfigureAwait(false);

        BasApprovalRequest request = new(
            TenantId,
            BankChangeBizType,
            JsonSerializer.Serialize(
                new SupplierBankChangePayload(
                    supplier.Id,
                    payload.BankName.Trim(),
                    payload.AccountName.Trim(),
                    payload.AccountNo.Trim(),
                    payload.IsDefault),
                QlxJsonDefaults.PreserveNull));
        await repo.AddApprovalRequestAsync(request, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        string instanceId = await approvalService.StartApprovalAsync(
            new StartApprovalRequest(
                ApprovalDefinitionCode: BankChangeBizType,
                BizType: BankChangeBizType,
                BizId: request.Id.ToString(CultureInfo.InvariantCulture),
                BizNo: supplier.Code,
                InitiatorUserId: ChangedBy,
                Title: $"供应商银行账户变更申请：{supplier.Name}",
                Description: null,
                BizData: new Dictionary<string, object?>
                {
                    ["supplierId"] = supplier.Id,
                    ["accountNo"] = MaskAccountNo(payload.AccountNo.Trim()),
                }),
            ct).ConfigureAwait(false);

        request.BindInstance(instanceId);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(request);
    }

    /// <inheritdoc />
    public async Task ApplyBankChangeAsync(SupplierBankChangePayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        SaveSupplierBankPayload save = new(
            payload.BankName, payload.AccountName, payload.AccountNo, payload.IsDefault);
        SupplierBankRow row = await repo.ExecuteInTransactionAsync(
            token => CreateBankCoreAsync(payload.SupplierId, save, token), ct).ConfigureAwait(false);

        await CaptureBankAuditAsync("UPDATE", row, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupplierInvoiceRow>> ListInvoicesAsync(
        long id, CancellationToken ct)
    {
        _ = await FindSupplierOrThrowAsync(id, ct).ConfigureAwait(false);
        IReadOnlyList<Domain.Supplier.SupplierInvoice> invoices = await repo
            .ListSupplierInvoicesAsync(id, ct).ConfigureAwait(false);
        return [.. invoices.Select(Map)];
    }

    /// <inheritdoc />
    public async Task<SupplierInvoiceRow> SaveInvoiceAsync(
        long id, SaveSupplierInvoicePayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        _ = await FindSupplierOrThrowAsync(id, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(payload.Title))
        {
            throw new ValidationException("title", "发票抬头不能为空");
        }

        if (!Enum.IsDefined(typeof(Domain.Supplier.InvoiceType), payload.InvoiceType))
        {
            throw new ValidationException("invoiceType", "未知的开票类型");
        }

        Domain.Supplier.SupplierInvoice invoice = new(
            TenantId,
            id,
            payload.Title.Trim(),
            payload.TaxNo?.Trim(),
            payload.BankInfo?.Trim(),
            payload.AddrPhone?.Trim(),
            (Domain.Supplier.InvoiceType)payload.InvoiceType);

        await repo.AddSupplierInvoiceAsync(invoice, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        SupplierInvoiceRow row = Map(invoice);
        await CaptureInvoiceAuditAsync("INSERT", row, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return row;
    }

    // ============ 内部实现 ============

    private async Task<SupplierRow> GetMasterAsync(long id, CancellationToken ct)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId, QlxModuleCodes.Bas, $"supplier:{id.ToString(CultureInfo.InvariantCulture)}");

        SupplierRow? row = await cache.GetOrCreateAsync<SupplierRow?>(
            cacheKey,
            factory: async token =>
            {
                Domain.Supplier.Supplier? supplier = await repo.FindSupplierAsync(id, token).ConfigureAwait(false);
                return supplier is null ? null : Map(supplier);
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);

        return row ?? throw new NotFoundException("供应商", id.ToString(CultureInfo.InvariantCulture));
    }

    private async Task<IReadOnlyList<SupplierCertRow>> ListCertsCoreAsync(long id, CancellationToken ct)
    {
        IReadOnlyList<Domain.Supplier.SupplierCert> certs = await repo
            .ListSupplierCertsAsync(id, ct).ConfigureAwait(false);
        return [.. certs.Select(Map)];
    }

    private async Task<SupplierRow> CreateCoreAsync(SaveSupplierPayload payload, CancellationToken ct)
    {
        string name = RequireName(payload.Name);
        string uscc = RequireUscc(payload.Uscc);
        SupplierType supplierType = ParseType(payload.SupplierType);

        if (await repo.IsSupplierUsccExistsAsync(uscc, excludeId: null, ct).ConfigureAwait(false))
        {
            throw new ConflictException("统一社会信用代码已存在", "SUPPLIER_USCC_DUPLICATED");
        }

        string code = await codeNumberGenerator.NextAsync(
            CodeBizType,
            TenantId,
            parentCode: null,
            typeCode: null,
            isConflict: (candidate, token) => repo.IsSupplierCodeExistsAsync(candidate, token),
            ct: ct).ConfigureAwait(false);

        Domain.Supplier.Supplier supplier = new(TenantId, code, name, uscc, supplierType);
        await repo.AddSupplierAsync(supplier, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(supplier);
    }

    private async Task<(SupplierRow Row, string OldJson)> UpdateCoreAsync(
        long id, SaveSupplierPayload payload, CancellationToken ct)
    {
        Domain.Supplier.Supplier supplier = await FindSupplierOrThrowAsync(id, ct).ConfigureAwait(false);
        string name = RequireName(payload.Name);
        string uscc = RequireUscc(payload.Uscc);
        SupplierType supplierType = ParseType(payload.SupplierType);
        string oldJson = Serialize(Map(supplier));

        if (await repo.IsSupplierUsccExistsAsync(uscc, id, ct).ConfigureAwait(false))
        {
            throw new ConflictException("统一社会信用代码已存在", "SUPPLIER_USCC_DUPLICATED");
        }

        supplier.UpdateProfile(name, uscc, supplierType, payload.Contact?.Trim(), payload.Phone?.Trim());
        await repo.UpdateSupplierAsync(supplier, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return (Map(supplier), oldJson);
    }

    private async Task<SupplierBankRow> CreateBankCoreAsync(
        long supplierId, SaveSupplierBankPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidateBankPayload(payload);
        _ = await FindSupplierOrThrowAsync(supplierId, ct).ConfigureAwait(false);

        if (payload.IsDefault)
        {
            IReadOnlyList<Domain.Supplier.SupplierBank> existing = await repo
                .ListSupplierBanksAsync(supplierId, ct).ConfigureAwait(false);
            foreach (Domain.Supplier.SupplierBank bank in existing.Where(b => b.IsDefault))
            {
                bank.UnsetDefault();
                await repo.UpdateSupplierBankAsync(bank, ct).ConfigureAwait(false);
            }
        }

        Domain.Supplier.SupplierBank entity = new(
            TenantId,
            supplierId,
            payload.BankName.Trim(),
            payload.AccountName.Trim(),
            payload.AccountNo.Trim(),
            payload.IsDefault);

        await repo.AddSupplierBankAsync(entity, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(entity);
    }

    private static void ValidateBankPayload(SaveSupplierBankPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.BankName))
        {
            throw new ValidationException("bankName", "开户银行不能为空");
        }

        if (string.IsNullOrWhiteSpace(payload.AccountName))
        {
            throw new ValidationException("accountName", "账户名称不能为空");
        }

        if (string.IsNullOrWhiteSpace(payload.AccountNo))
        {
            throw new ValidationException("accountNo", "银行账号不能为空");
        }
    }

    private async Task<Domain.Supplier.Supplier> FindSupplierOrThrowAsync(long id, CancellationToken ct)
        => await repo.FindSupplierAsync(id, ct).ConfigureAwait(false)
           ?? throw new NotFoundException("供应商", id.ToString(CultureInfo.InvariantCulture));

    // ============ 审计与缓存 ============

    private Task CaptureAuditAsync(
        string operation, string entityName, long entityId, string? oldJson, string newJson, CancellationToken ct)
        => audit.CaptureChangeAsync(
            new AuditLogEntry(
                snowflake.NextId(),
                entityName,
                entityId,
                operation,
                oldJson,
                newJson,
                ChangedBy,
                DateTimeOffset.UtcNow,
                TenantId),
            ct);

    /// <summary>银行账户审计快照：账号仅保留后 4 位（C-7-3 敏感明文禁落快照）。</summary>
    private Task CaptureBankAuditAsync(string operation, SupplierBankRow row, CancellationToken ct)
        => CaptureAuditAsync(
            operation,
            nameof(Domain.Supplier.SupplierBank),
            row.Id,
            null,
            JsonSerializer.Serialize(
                new
                {
                    row.Id,
                    row.SupplierId,
                    row.BankName,
                    row.AccountName,
                    AccountNo = MaskAccountNo(row.AccountNo),
                    row.IsDefault,
                    row.Status,
                },
                QlxJsonDefaults.PreserveNull),
            ct);

    /// <summary>开票信息审计快照：开户行及账号字段脱敏后落库（C-7-3）。</summary>
    private Task CaptureInvoiceAuditAsync(string operation, SupplierInvoiceRow row, CancellationToken ct)
        => CaptureAuditAsync(
            operation,
            nameof(Domain.Supplier.SupplierInvoice),
            row.Id,
            null,
            JsonSerializer.Serialize(
                new
                {
                    row.Id,
                    row.SupplierId,
                    row.Title,
                    row.TaxNo,
                    BankInfo = MaskBankInfo(row.BankInfo),
                    row.AddrPhone,
                    row.InvoiceType,
                    row.Status,
                },
                QlxJsonDefaults.PreserveNull),
            ct);

    private Task<long> InvalidateCacheAsync(CancellationToken ct)
        => cache.RemoveByPrefixAsync(
            $"{QlxCacheKey.TenantKeyPrefix}:{TenantId.ToString(CultureInfo.InvariantCulture)}:{QlxModuleCodes.Bas}:",
            ct);

    private static string MaskAccountNo(string accountNo)
        => accountNo.Length <= 4
            ? "****"
            : string.Concat("****", accountNo.AsSpan(accountNo.Length - 4));

    private static string? MaskBankInfo(string? bankInfo)
    {
        if (string.IsNullOrWhiteSpace(bankInfo))
        {
            return bankInfo;
        }

        return bankInfo.Length <= 4
            ? "****"
            : string.Concat("****", bankInfo.AsSpan(bankInfo.Length - 4));
    }

    // ============ 映射与校验 ============

    private static SupplierRow Map(Domain.Supplier.Supplier supplier)
        => new(
            supplier.Id,
            supplier.Code,
            supplier.Name,
            supplier.Uscc,
            (int)supplier.SupplierType,
            supplier.Contact,
            supplier.Phone,
            (int)supplier.Grade,
            (int)supplier.Status,
            supplier.UpdatedAt);

    private static SupplierCertRow Map(Domain.Supplier.SupplierCert cert)
        => new(
            cert.Id,
            cert.SupplierId,
            cert.CertType,
            cert.CertNo,
            cert.IssueOrg,
            cert.EffectDate,
            cert.ExpiryDate,
            cert.FileId,
            (int)cert.Status);

    private static SupplierBankRow Map(Domain.Supplier.SupplierBank bank)
        => new(
            bank.Id,
            bank.SupplierId,
            bank.BankName,
            bank.AccountName,
            bank.AccountNo,
            bank.IsDefault,
            (int)bank.Status);

    private static SupplierInvoiceRow Map(Domain.Supplier.SupplierInvoice invoice)
        => new(
            invoice.Id,
            invoice.SupplierId,
            invoice.Title,
            invoice.TaxNo,
            invoice.BankInfo,
            invoice.AddrPhone,
            (int)invoice.InvoiceType,
            (int)invoice.Status);

    private static SupplierGradeLogRow Map(Domain.Supplier.SupplierGradeLog log)
        => new(
            log.Id,
            log.SupplierId,
            (int)log.OldGrade,
            (int)log.NewGrade,
            log.Reason,
            log.ApprovedBy,
            log.CreatedAt);

    private static ApprovalRequestRow Map(BasApprovalRequest request)
        => new(
            request.Id,
            request.ApprovalInstanceId,
            request.BizType,
            request.BizId,
            request.PayloadJson,
            (int)request.Status,
            request.AppliedAt,
            request.CreatedAt);

    private static string Serialize(object value)
        => JsonSerializer.Serialize(value, QlxJsonDefaults.PreserveNull);

    private static string RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "供应商名称不能为空");
        }

        return name.Trim();
    }

    private static string RequireUscc(string? uscc)
    {
        if (string.IsNullOrWhiteSpace(uscc))
        {
            throw new ValidationException("uscc", "统一社会信用代码不能为空");
        }

        return uscc.Trim();
    }

    private static SupplierType ParseType(int type)
        => Enum.IsDefined(typeof(SupplierType), type)
            ? (SupplierType)type
            : throw new ValidationException("supplierType", $"未知的供应商类型：{type.ToString(CultureInfo.InvariantCulture)}");

    private static SupplierGrade ParseGrade(int grade)
        => Enum.IsDefined(typeof(SupplierGrade), grade)
            ? (SupplierGrade)grade
            : throw new ValidationException("grade", $"未知的供应商评级：{grade.ToString(CultureInfo.InvariantCulture)}");
}
