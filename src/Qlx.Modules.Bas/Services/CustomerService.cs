/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 19:30:00 +08:00 中国标准时间 星期日
 * 描述：客户主数据服务实现——统一发号器编码 + 信用额度校验 + 额度分级审批阈值 + 脱敏审计 +
 *       失效优先缓存（写路径只删缓存，读路径 GetOrCreateAsync）
 */

using System.Globalization;
using System.Text.Json;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Modules;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Repositories;
using Qlx.Modules.Bas.Services.Numbering;

using CreditGrade = Qlx.Modules.Bas.Domain.Customer.CreditGrade;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 客户主数据服务实现。编码经 <see cref="IBasCodeNumberGenerator"/> 生成（N-1，前端不传码）；
/// 信用额度为原始 decimal 金额，不做显示格式化（B-2-2）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="codeNumberGenerator">统一发号器（客户编码）。</param>
/// <param name="approvalService">审批中心契约（准入 / 额度调整申请）。</param>
/// <param name="parameters">系统参数读取（额度审批阈值）。</param>
/// <param name="cache">统一缓存服务。</param>
/// <param name="audit">审计服务。</param>
/// <param name="snowflake">雪花 Id 生成器（审计事件 Id）。</param>
/// <param name="userContext">当前用户上下文（审计变更人）。</param>
internal sealed class CustomerService(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    IBasCodeNumberGenerator codeNumberGenerator,
    IApprovalService approvalService,
    ISystemParameterProvider parameters,
    ICacheService cache,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake,
    ICurrentUserContext userContext) : ICustomerService
{
    /// <summary>客户准入审批业务类型。</summary>
    internal const string AdmissionBizType = "bas.customer.admission";

    /// <summary>客户信用额度调整审批业务类型（本批统一单定义）。</summary>
    internal const string CreditAdjustBizType = "bas.customer.credit.adjust";

    /// <summary>信用额度审批阈值参数键（仅记录于 BizData / 标题）。</summary>
    internal const string CreditThresholdParamKey = "bas.customer.credit.approvalThreshold";

    /// <summary>信用额度审批阈值默认值。</summary>
    internal const int DefaultCreditThreshold = 100000;

    private const string CodeBizType = "customer";

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
    public async Task<PagedResult<CustomerRow>> ListAsync(
        int? creditGrade, int? status, string? keyword, int page, int pageSize, CancellationToken ct)
    {
        int safePage = Math.Max(page, 1);
        int safeSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 200);
        int skip = (safePage - 1) * safeSize;

        (IReadOnlyList<Domain.Customer.Customer> Items, int Total) result = await repo
            .QueryCustomersAsync(creditGrade, status, keyword?.Trim(), skip, safeSize, ct)
            .ConfigureAwait(false);

        return new PagedResult<CustomerRow>(
            [.. result.Items.Select(Map)], result.Total, safePage, safeSize);
    }

    /// <inheritdoc />
    public async Task<CustomerDetailRow> GetAsync(long id, CancellationToken ct)
    {
        CustomerRow master = await GetMasterAsync(id, ct).ConfigureAwait(false);

        IReadOnlyList<Domain.Customer.CustomerBank> banks = await repo
            .ListCustomerBanksAsync(id, ct).ConfigureAwait(false);
        IReadOnlyList<Domain.Customer.CustomerInvoice> invoices = await repo
            .ListCustomerInvoicesAsync(id, ct).ConfigureAwait(false);
        IReadOnlyList<Domain.Customer.CustomerCreditLog> creditLogs = await repo
            .ListCustomerCreditLogsAsync(id, ct).ConfigureAwait(false);

        return new CustomerDetailRow(
            master,
            [.. banks.Select(Map)],
            [.. invoices.Select(Map)],
            [.. creditLogs.Select(Map)]);
    }

    /// <inheritdoc />
    public async Task<CustomerCreditViewRow> GetCreditViewAsync(long id, CancellationToken ct)
    {
        CustomerRow master = await GetMasterAsync(id, ct).ConfigureAwait(false);
        return new CustomerCreditViewRow(
            master.Id,
            master.CreditLimit,
            master.CreditUsed,
            master.CreditLimit - master.CreditUsed,
            master.CreditGrade,
            master.Status);
    }

    // ============ 写路径 ============

    /// <inheritdoc />
    public async Task<CustomerRow> CreateAsync(SaveCustomerPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        CustomerRow row = await repo.ExecuteInTransactionAsync(
            token => CreateCoreAsync(payload, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync("INSERT", nameof(Domain.Customer.Customer), row.Id, null, Serialize(row), ct)
            .ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return row;
    }

    /// <inheritdoc />
    public async Task<CustomerRow> UpdateAsync(
        long id, SaveCustomerPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        (CustomerRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(
            token => UpdateCoreAsync(id, payload, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync(
            "UPDATE", nameof(Domain.Customer.Customer), result.Row.Id, result.OldJson, Serialize(result.Row), ct)
            .ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task EnsureCreditAvailableAsync(long id, decimal orderAmount, CancellationToken ct)
    {
        Domain.Customer.Customer customer = await FindCustomerOrThrowAsync(id, ct).ConfigureAwait(false);

        if (customer.CreditUsed + orderAmount > customer.CreditLimit)
        {
            throw new ConflictException("信用额度不足，请走特批流程", "CREDIT_EXCEEDED");
        }
    }

    // ============ 审批挂起与应用 ============

    /// <inheritdoc />
    public async Task<ApprovalRequestRow> SubmitAdmissionRequestAsync(
        long id, string? reason, CancellationToken ct)
    {
        Domain.Customer.Customer customer = await FindCustomerOrThrowAsync(id, ct).ConfigureAwait(false);
        if (customer.Status == PartnerStatus.Closed)
        {
            throw new ConflictException("已终止客户不可再次准入", "CUSTOMER_STATE_INVALID");
        }

        if (customer.Status == PartnerStatus.Active)
        {
            throw new ConflictException("客户已在合作中，无需重复准入", "CUSTOMER_STATE_INVALID");
        }

        BasApprovalRequest request = new(
            TenantId,
            AdmissionBizType,
            JsonSerializer.Serialize(
                new CustomerAdmissionPayload(customer.Id, reason), QlxJsonDefaults.PreserveNull));
        await repo.AddApprovalRequestAsync(request, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        string instanceId = await approvalService.StartApprovalAsync(
            new StartApprovalRequest(
                ApprovalDefinitionCode: AdmissionBizType,
                BizType: AdmissionBizType,
                BizId: request.Id.ToString(CultureInfo.InvariantCulture),
                BizNo: customer.Code,
                InitiatorUserId: ChangedBy,
                Title: $"客户准入申请：{customer.Name}",
                Description: reason,
                BizData: new Dictionary<string, object?>
                {
                    ["customerId"] = customer.Id,
                }),
            ct).ConfigureAwait(false);

        request.BindInstance(instanceId);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(request);
    }

    /// <inheritdoc />
    public async Task ApplyAdmissionAsync(CustomerAdmissionPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Domain.Customer.Customer customer = await FindCustomerOrThrowAsync(payload.CustomerId, ct)
            .ConfigureAwait(false);

        string oldJson = Serialize(Map(customer));
        if (customer.Status != PartnerStatus.Active)
        {
            customer.Approve();
        }

        await repo.UpdateCustomerAsync(customer, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        CustomerRow row = Map(customer);
        await CaptureAuditAsync(
            "UPDATE", nameof(Domain.Customer.Customer), row.Id, oldJson, Serialize(row), ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ApprovalRequestRow> SubmitCreditAdjustRequestAsync(
        long id, CustomerCreditAdjustPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        _ = ParseCreditGrade(payload.NewCreditGrade);
        if (payload.NewLimit < 0)
        {
            throw new ValidationException("newLimit", "信用额度不能为负数");
        }

        Domain.Customer.Customer customer = await FindCustomerOrThrowAsync(id, ct).ConfigureAwait(false);
        CustomerCreditAdjustPayload normalized = payload with { CustomerId = customer.Id };

        int threshold = await parameters
            .GetIntAsync(CreditThresholdParamKey, DefaultCreditThreshold, ct).ConfigureAwait(false);

        BasApprovalRequest request = new(
            TenantId,
            CreditAdjustBizType,
            JsonSerializer.Serialize(normalized, QlxJsonDefaults.PreserveNull));
        await repo.AddApprovalRequestAsync(request, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        string instanceId = await approvalService.StartApprovalAsync(
            new StartApprovalRequest(
                ApprovalDefinitionCode: CreditAdjustBizType,
                BizType: CreditAdjustBizType,
                BizId: request.Id.ToString(CultureInfo.InvariantCulture),
                BizNo: customer.Code,
                InitiatorUserId: ChangedBy,
                Title: $"客户信用额度调整申请：{customer.Name}（阈值 {threshold.ToString(CultureInfo.InvariantCulture)}）",
                Description: payload.Reason,
                BizData: new Dictionary<string, object?>
                {
                    ["customerId"] = customer.Id,
                    ["newLimit"] = normalized.NewLimit,
                    ["threshold"] = threshold,
                    ["thresholdExceeded"] = normalized.NewLimit > threshold,
                }),
            ct).ConfigureAwait(false);

        request.BindInstance(instanceId);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(request);
    }

    /// <inheritdoc />
    public async Task<CustomerCreditApplied> ApplyCreditAdjustAsync(
        CustomerCreditAdjustPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        CreditGrade newGrade = ParseCreditGrade(payload.NewCreditGrade);

        Domain.Customer.Customer customer = await FindCustomerOrThrowAsync(payload.CustomerId, ct)
            .ConfigureAwait(false);

        decimal oldLimit = customer.CreditLimit;
        CreditGrade oldGrade = customer.CreditGrade;
        string oldJson = Serialize(Map(customer));

        customer.AdjustCredit(newGrade, payload.NewLimit);
        await repo.AddCustomerCreditLogAsync(
            new Domain.Customer.CustomerCreditLog(
                TenantId,
                customer.Id,
                oldGrade,
                newGrade,
                oldLimit,
                payload.NewLimit,
                payload.Reason,
                ChangedBy),
            ct).ConfigureAwait(false);

        await repo.UpdateCustomerAsync(customer, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        CustomerRow row = Map(customer);
        await CaptureAuditAsync(
            "UPDATE", nameof(Domain.Customer.Customer), row.Id, oldJson, Serialize(row), ct)
            .ConfigureAwait(false);

        return new CustomerCreditApplied(
            row.Id, row.Code, oldLimit, payload.NewLimit, (int)newGrade);
    }

    // ============ 子表 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerBankRow>> ListBanksAsync(long id, CancellationToken ct)
    {
        _ = await FindCustomerOrThrowAsync(id, ct).ConfigureAwait(false);
        IReadOnlyList<Domain.Customer.CustomerBank> banks = await repo
            .ListCustomerBanksAsync(id, ct).ConfigureAwait(false);
        return [.. banks.Select(Map)];
    }

    /// <inheritdoc />
    public async Task<CustomerBankRow> SaveBankAsync(
        long id, SaveCustomerBankPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
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

        (CustomerBankRow Row, string NewJson) result = await repo.ExecuteInTransactionAsync(
            async token =>
            {
                _ = await FindCustomerOrThrowAsync(id, token).ConfigureAwait(false);

                if (payload.IsDefault)
                {
                    IReadOnlyList<Domain.Customer.CustomerBank> existing = await repo
                        .ListCustomerBanksAsync(id, token).ConfigureAwait(false);
                    foreach (Domain.Customer.CustomerBank bank in existing.Where(b => b.IsDefault))
                    {
                        bank.UnsetDefault();
                        await repo.UpdateCustomerBankAsync(bank, token).ConfigureAwait(false);
                    }
                }

                Domain.Customer.CustomerBank entity = new(
                    TenantId,
                    id,
                    payload.BankName.Trim(),
                    payload.AccountName.Trim(),
                    payload.AccountNo.Trim(),
                    payload.IsDefault);
                await repo.AddCustomerBankAsync(entity, token).ConfigureAwait(false);
                _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);

                CustomerBankRow row = Map(entity);
                return (row, JsonSerializer.Serialize(
                    new
                    {
                        row.Id,
                        row.CustomerId,
                        row.BankName,
                        row.AccountName,
                        AccountNo = MaskAccountNo(row.AccountNo),
                        row.IsDefault,
                        row.Status,
                    },
                    QlxJsonDefaults.PreserveNull));
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync(
            "INSERT", nameof(Domain.Customer.CustomerBank), result.Row.Id, null, result.NewJson, ct)
            .ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerInvoiceRow>> ListInvoicesAsync(
        long id, CancellationToken ct)
    {
        _ = await FindCustomerOrThrowAsync(id, ct).ConfigureAwait(false);
        IReadOnlyList<Domain.Customer.CustomerInvoice> invoices = await repo
            .ListCustomerInvoicesAsync(id, ct).ConfigureAwait(false);
        return [.. invoices.Select(Map)];
    }

    /// <inheritdoc />
    public async Task<CustomerInvoiceRow> SaveInvoiceAsync(
        long id, SaveCustomerInvoicePayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        _ = await FindCustomerOrThrowAsync(id, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(payload.Title))
        {
            throw new ValidationException("title", "发票抬头不能为空");
        }

        if (payload.InvoiceType is < 1 or > 3)
        {
            throw new ValidationException("invoiceType", "未知的开票类型");
        }

        Domain.Customer.CustomerInvoice invoice = new(
            TenantId,
            id,
            payload.Title.Trim(),
            payload.TaxNo?.Trim(),
            payload.BankInfo?.Trim(),
            payload.AddrPhone?.Trim(),
            payload.InvoiceType);

        await repo.AddCustomerInvoiceAsync(invoice, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        CustomerInvoiceRow row = Map(invoice);
        await CaptureAuditAsync(
            "INSERT",
            nameof(Domain.Customer.CustomerInvoice),
            row.Id,
            null,
            JsonSerializer.Serialize(
                new
                {
                    row.Id,
                    row.CustomerId,
                    row.Title,
                    row.TaxNo,
                    BankInfo = MaskAccountNo(row.BankInfo ?? string.Empty),
                    row.AddrPhone,
                    row.InvoiceType,
                    row.Status,
                },
                QlxJsonDefaults.PreserveNull),
            ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return row;
    }

    // ============ 内部实现 ============

    private async Task<CustomerRow> GetMasterAsync(long id, CancellationToken ct)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId, QlxModuleCodes.Bas, $"customer:{id.ToString(CultureInfo.InvariantCulture)}");

        CustomerRow? row = await cache.GetOrCreateAsync<CustomerRow?>(
            cacheKey,
            factory: async token =>
            {
                Domain.Customer.Customer? customer = await repo.FindCustomerAsync(id, token).ConfigureAwait(false);
                return customer is null ? null : Map(customer);
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);

        return row ?? throw new NotFoundException("客户", id.ToString(CultureInfo.InvariantCulture));
    }

    private async Task<CustomerRow> CreateCoreAsync(SaveCustomerPayload payload, CancellationToken ct)
    {
        string name = RequireName(payload.Name);
        string uscc = RequireUscc(payload.Uscc);

        if (await repo.IsCustomerUsccExistsAsync(uscc, excludeId: null, ct).ConfigureAwait(false))
        {
            throw new ConflictException("统一社会信用代码已存在", "CUSTOMER_USCC_DUPLICATED");
        }

        string code = await codeNumberGenerator.NextAsync(
            CodeBizType,
            TenantId,
            parentCode: null,
            typeCode: null,
            isConflict: (candidate, token) => repo.IsCustomerCodeExistsAsync(candidate, token),
            ct: ct).ConfigureAwait(false);

        Domain.Customer.Customer customer = new(TenantId, code, name, uscc, payload.CustomerType);
        customer.UpdateProfile(
            name, uscc, payload.CustomerType, payload.Contact?.Trim(), payload.Phone?.Trim(), payload.PaymentTerms?.Trim());

        await repo.AddCustomerAsync(customer, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(customer);
    }

    private async Task<(CustomerRow Row, string OldJson)> UpdateCoreAsync(
        long id, SaveCustomerPayload payload, CancellationToken ct)
    {
        Domain.Customer.Customer customer = await FindCustomerOrThrowAsync(id, ct).ConfigureAwait(false);
        string name = RequireName(payload.Name);
        string uscc = RequireUscc(payload.Uscc);
        string oldJson = Serialize(Map(customer));

        if (await repo.IsCustomerUsccExistsAsync(uscc, id, ct).ConfigureAwait(false))
        {
            throw new ConflictException("统一社会信用代码已存在", "CUSTOMER_USCC_DUPLICATED");
        }

        customer.UpdateProfile(
            name, uscc, payload.CustomerType, payload.Contact?.Trim(), payload.Phone?.Trim(), payload.PaymentTerms?.Trim());
        await repo.UpdateCustomerAsync(customer, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return (Map(customer), oldJson);
    }

    private async Task<Domain.Customer.Customer> FindCustomerOrThrowAsync(long id, CancellationToken ct)
        => await repo.FindCustomerAsync(id, ct).ConfigureAwait(false)
           ?? throw new NotFoundException("客户", id.ToString(CultureInfo.InvariantCulture));

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

    private Task<long> InvalidateCacheAsync(CancellationToken ct)
        => cache.RemoveByPrefixAsync(
            $"{QlxCacheKey.TenantKeyPrefix}:{TenantId.ToString(CultureInfo.InvariantCulture)}:{QlxModuleCodes.Bas}:",
            ct);

    private static string MaskAccountNo(string accountNo)
        => accountNo.Length <= 4
            ? "****"
            : string.Concat("****", accountNo.AsSpan(accountNo.Length - 4));

    // ============ 映射与校验 ============

    private static CustomerRow Map(Domain.Customer.Customer customer)
        => new(
            customer.Id,
            customer.Code,
            customer.Name,
            customer.Uscc,
            customer.CustomerType,
            customer.Contact,
            customer.Phone,
            (int)customer.CreditGrade,
            customer.CreditLimit,
            customer.CreditUsed,
            customer.PaymentTerms,
            (int)customer.Status,
            customer.UpdatedAt);

    private static CustomerBankRow Map(Domain.Customer.CustomerBank bank)
        => new(
            bank.Id,
            bank.CustomerId,
            bank.BankName,
            bank.AccountName,
            bank.AccountNo,
            bank.IsDefault,
            (int)bank.Status);

    private static CustomerInvoiceRow Map(Domain.Customer.CustomerInvoice invoice)
        => new(
            invoice.Id,
            invoice.CustomerId,
            invoice.Title,
            invoice.TaxNo,
            invoice.BankInfo,
            invoice.AddrPhone,
            invoice.InvoiceType,
            (int)invoice.Status);

    private static CustomerCreditLogRow Map(Domain.Customer.CustomerCreditLog log)
        => new(
            log.Id,
            log.CustomerId,
            (int)log.OldCreditGrade,
            (int)log.NewCreditGrade,
            log.OldLimit,
            log.NewLimit,
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
            throw new ValidationException("name", "客户名称不能为空");
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

    private static CreditGrade ParseCreditGrade(int grade)
        => Enum.IsDefined(typeof(CreditGrade), grade)
            ? (CreditGrade)grade
            : throw new ValidationException("newCreditGrade", $"未知的信用等级：{grade.ToString(CultureInfo.InvariantCulture)}");
}
