/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 22:10:00 +08:00 中国标准时间 星期日
 * 描述：人员主数据服务实现——统一发号器编码 + 证件 SHA-256 防重 + Data Protection 加密 +
 *       任职记录与在职状态机 + 岗位在岗数回写 + 脱敏审计 + 失效优先缓存 + 离职事件持久发布
 */

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.DataProtection;

using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Events.Bas;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.EventBus;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Validation;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Domain.Person;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Repositories;
using Qlx.Modules.Bas.Services.Numbering;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 人员主数据服务实现。编码经 <see cref="IBasCodeNumberGenerator"/> 按人员类型生成（N-1）；
/// 证件号 / 手机号 / 银行卡号仅存 Data Protection 密文，证件号另存 SHA-256 哈希用于租户内防重；
/// 写路径事务提交后落脱敏审计快照并失效 Bas 租户缓存前缀（C-5 失效优先）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="codeNumberGenerator">统一发号器（人员编码）。</param>
/// <param name="dataProtection">数据保护提供程序（人员敏感字段加解密）。</param>
/// <param name="cache">统一缓存服务。</param>
/// <param name="audit">审计服务。</param>
/// <param name="snowflake">雪花 Id 生成器（审计事件 Id）。</param>
/// <param name="userContext">当前用户上下文（审计变更人）。</param>
/// <param name="eventBus">事件总线（离职事件持久发布）。</param>
internal sealed class PersonService(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    IBasCodeNumberGenerator codeNumberGenerator,
    IDataProtectionProvider dataProtection,
    ICacheService cache,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake,
    ICurrentUserContext userContext,
    IEventBus eventBus) : IPersonService
{
    private const string EmployeeCodeBizType = "employee";

    private const string LabourCodeBizType = "labour";

    private static readonly CacheEntryOptions s_cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        NullValueExpiration = TimeSpan.FromSeconds(30),
    };

    private readonly IDataProtector _protector =
        dataProtection.CreateProtector(QlxDataProtectionPurposes.BasPersonSensitive);

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    private string ChangedBy => userContext.UserId ?? "system";

    // ============ 读路径 ============

    /// <inheritdoc />
    public async Task<PagedResult<PersonRow>> ListAsync(
        int? personType,
        int? employStatus,
        long? orgId,
        long? positionId,
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        int safePage = Math.Max(page, 1);
        int safeSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 200);
        int skip = (safePage - 1) * safeSize;

        (IReadOnlyList<Person> Items, int Total) result = await repo
            .QueryPersonsAsync(
                personType, employStatus, orgId, positionId, keyword?.Trim(), skip, safeSize, ct)
            .ConfigureAwait(false);

        return new PagedResult<PersonRow>(
            [.. result.Items.Select(Map)], result.Total, safePage, safeSize);
    }

    /// <inheritdoc />
    public async Task<PersonDetailRow> GetAsync(long id, CancellationToken ct)
    {
        PersonRow master = await GetMasterAsync(id, ct).ConfigureAwait(false);

        IReadOnlyList<PersonAssignment> assignments = await repo
            .ListPersonAssignmentsAsync(id, ct).ConfigureAwait(false);
        IReadOnlyList<PersonCert> certs = await repo
            .ListPersonCertsAsync(id, ct).ConfigureAwait(false);

        List<PersonAssignmentRow> assignmentRows = [.. assignments.Select(Map)];
        return new PersonDetailRow(
            master,
            assignmentRows.Find(static a => a.IsCurrent),
            assignmentRows,
            [.. certs.Select(Map)]);
    }

    // ============ 写路径 ============

    /// <inheritdoc />
    public async Task<PersonRow> CreateAsync(SavePersonPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        PersonRow row = await repo.ExecuteInTransactionAsync(
            token => CreateCoreAsync(payload, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync("INSERT", row, oldJson: null, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return row;
    }

    /// <inheritdoc />
    public async Task<PersonRow> UpdateAsync(
        long id, UpdatePersonPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        (PersonRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(
            token => UpdateCoreAsync(id, payload, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task<PersonRow> ResignAsync(
        long id, ResignPersonPayload payload, CancellationToken ct)
    {
        payload ??= new ResignPersonPayload(null, null);

        (PersonRow Row, string OldJson, long? PositionId) result = await repo.ExecuteInTransactionAsync(
            token => ResignCoreAsync(id, payload, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);

        await eventBus.PublishDurableAsync(
            new PersonLeftEvent
            {
                PersonId = result.Row.Id,
                PersonCode = result.Row.Code,
                OrgId = result.Row.OrgId,
                PositionId = result.PositionId,
                LeftAt = DateTimeOffset.UtcNow,
                TenantCode = tenantCtx.TenantCode
                    ?? TenantId.ToString(CultureInfo.InvariantCulture),
            },
            ct).ConfigureAwait(false);

        return result.Row;
    }

    /// <inheritdoc />
    public async Task<PersonRow> RehireAsync(
        long id, RehirePersonPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        (PersonRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(
            token => RehireCoreAsync(id, payload, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task<PersonPlaintextRow> GetPlaintextAsync(long id, CancellationToken ct)
    {
        Person person = await FindPersonOrThrowAsync(id, ct).ConfigureAwait(false);

        string idNo = _protector.Unprotect(person.IdNoCipher);
        string mobile = _protector.Unprotect(person.MobileCipher);
        string? bankCardNo = string.IsNullOrEmpty(person.BankCardNoCipher)
            ? null
            : _protector.Unprotect(person.BankCardNoCipher);

        // 敏感字段访问审计：只记录访问者与被访问字段名，严禁记录明文值（C-7-3）
        await audit.CaptureChangeAsync(
            new AuditLogEntry(
                snowflake.NextId(),
                nameof(Person),
                person.Id,
                "VIEW_PLAINTEXT",
                OldValues: null,
                NewValues: JsonSerializer.Serialize(
                    new
                    {
                        Viewer = ChangedBy,
                        person.Code,
                        Fields = new[] { "idNo", "mobile", "bankCardNo" },
                    },
                    QlxJsonDefaults.PreserveNull),
                ChangedBy,
                DateTimeOffset.UtcNow,
                TenantId),
            ct).ConfigureAwait(false);

        return new PersonPlaintextRow(person.Id, person.Code, idNo, mobile, bankCardNo);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersonCertRow>> ListCertsAsync(long id, CancellationToken ct)
    {
        _ = await FindPersonOrThrowAsync(id, ct).ConfigureAwait(false);
        IReadOnlyList<PersonCert> certs = await repo.ListPersonCertsAsync(id, ct).ConfigureAwait(false);
        return [.. certs.Select(Map)];
    }

    /// <inheritdoc />
    public async Task<PersonCertRow> SaveCertAsync(
        long id, SavePersonCertPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        _ = await FindPersonOrThrowAsync(id, ct).ConfigureAwait(false);
        ValidateCertPayload(payload);

        PersonCert cert = new(
            TenantId,
            id,
            payload.CertType.Trim(),
            NullIfBlank(payload.CertNo),
            payload.IssuedOn,
            payload.ExpiresOn,
            NullIfBlank(payload.Remark));

        await repo.AddPersonCertAsync(cert, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        PersonCertRow row = Map(cert);
        await CaptureCertAuditAsync("INSERT", row, oldJson: null, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return row;
    }

    /// <inheritdoc />
    public async Task<PersonCertRow> UpdateCertAsync(
        long id, long certId, SavePersonCertPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidateCertPayload(payload);

        PersonCert cert = await FindPersonCertOrThrowAsync(id, certId, ct).ConfigureAwait(false);
        PersonCertRow oldRow = Map(cert);

        cert.UpdateDetails(
            payload.CertType.Trim(),
            NullIfBlank(payload.CertNo),
            payload.IssuedOn,
            payload.ExpiresOn,
            NullIfBlank(payload.Remark));

        await repo.UpdatePersonCertAsync(cert, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        PersonCertRow row = Map(cert);
        await CaptureCertAuditAsync("UPDATE", row, Serialize(oldRow), ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return row;
    }

    /// <inheritdoc />
    public async Task DeleteCertAsync(long id, long certId, CancellationToken ct)
    {
        PersonCert cert = await FindPersonCertOrThrowAsync(id, certId, ct).ConfigureAwait(false);
        PersonCertRow row = Map(cert);

        await repo.RemovePersonCertAsync(cert, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await CaptureCertAuditAsync("DELETE", row, Serialize(row), ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
    }

    // ============ 内部实现 ============

    private async Task<PersonRow> GetMasterAsync(long id, CancellationToken ct)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId, QlxModuleCodes.Bas, $"person:detail:{id.ToString(CultureInfo.InvariantCulture)}");

        PersonRow? row = await cache.GetOrCreateAsync<PersonRow?>(
            cacheKey,
            factory: async token =>
            {
                Person? person = await repo.FindPersonAsync(id, token).ConfigureAwait(false);
                return person is null ? null : Map(person);
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);

        return row ?? throw new NotFoundException("人员", id.ToString(CultureInfo.InvariantCulture));
    }

    private async Task<PersonRow> CreateCoreAsync(SavePersonPayload payload, CancellationToken ct)
    {
        string name = RequireName(payload.Name);
        PersonType personType = ParsePersonType(payload.PersonType);
        PersonIdType idType = ParseIdType(payload.IdType);
        EmployStatus employStatus = ParseEmployStatus(payload.EmployStatus, allowLeft: false);
        string idNo = NormalizeIdNo(payload.IdNo, idType);
        string mobile = NormalizeMobile(payload.Mobile);
        string? bankCardNo = NullIfBlank(NormalizeBankCardNo(payload.BankCardNo));

        await EnsureReferencesAsync(payload.OrgId, payload.PositionId, personType, ct).ConfigureAwait(false);
        await EnsureNotOverstaffedAsync(payload.PositionId, ct).ConfigureAwait(false);
        await EnsureIdNoNotDuplicatedAsync(idNo, excludeId: null, ct).ConfigureAwait(false);

        DateOnly entryDate = payload.EntryDate ?? Today();
        string code = await codeNumberGenerator.NextAsync(
            personType == PersonType.Employee ? EmployeeCodeBizType : LabourCodeBizType,
            TenantId,
            parentCode: null,
            typeCode: null,
            isConflict: (candidate, token) => repo.IsPersonCodeExistsAsync(candidate, token),
            ct: ct).ConfigureAwait(false);

        Person person = new(
            TenantId,
            code,
            name,
            personType,
            idType,
            _protector.Protect(idNo),
            ComputeIdNoHash(TenantId, idNo),
            _protector.Protect(mobile),
            entryDate,
            bankCardNo is null ? null : _protector.Protect(bankCardNo));

        person.Assign(payload.OrgId, payload.PositionId);
        if (employStatus != EmployStatus.Probation)
        {
            person.ChangeEmployStatus(employStatus);
        }

        await repo.AddPersonAsync(person, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await repo.AddPersonAssignmentAsync(
            new PersonAssignment(TenantId, person.Id, payload.OrgId, payload.PositionId, entryDate),
            ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await RefreshPositionCountsAsync([payload.PositionId], ct).ConfigureAwait(false);
        return Map(person);
    }

    private async Task<(PersonRow Row, string OldJson)> UpdateCoreAsync(
        long id, UpdatePersonPayload payload, CancellationToken ct)
    {
        Person person = await FindPersonOrThrowAsync(id, ct).ConfigureAwait(false);
        string name = RequireName(payload.Name);
        PersonType personType = ParsePersonType(payload.PersonType);
        string oldJson = Serialize(Map(person));

        long? oldPositionId = person.PositionId;
        bool assignmentChanged = person.OrgId != payload.OrgId || person.PositionId != payload.PositionId;

        await EnsureReferencesAsync(payload.OrgId, payload.PositionId, personType, ct).ConfigureAwait(false);

        if (assignmentChanged)
        {
            if (person.EmployStatus == EmployStatus.Left)
            {
                throw new ConflictException("离职人员不可调岗，请走再入职流程", "PERSON_LEFT_ASSIGN_DENIED");
            }

            await EnsureNotOverstaffedAsync(payload.PositionId, ct).ConfigureAwait(false);

            DateOnly effectiveDate = payload.EffectiveDate ?? Today();
            IReadOnlyList<PersonAssignment> assignments = await repo
                .ListPersonAssignmentsAsync(id, ct).ConfigureAwait(false);

            foreach (PersonAssignment assignment in assignments.Where(static a => a.IsCurrent))
            {
                assignment.Close(effectiveDate, NullIfBlank(payload.ChangeReason));
                await repo.UpdatePersonAssignmentAsync(assignment, ct).ConfigureAwait(false);
            }

            await repo.AddPersonAssignmentAsync(
                new PersonAssignment(
                    TenantId,
                    person.Id,
                    payload.OrgId,
                    payload.PositionId,
                    effectiveDate,
                    NullIfBlank(payload.ChangeReason)),
                ct).ConfigureAwait(false);
        }

        person.UpdateProfile(
            name, personType, NullIfBlank(payload.EmergencyContact), NullIfBlank(payload.EmergencyPhone));
        person.Assign(payload.OrgId, payload.PositionId);

        await repo.UpdatePersonAsync(person, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await RefreshPositionCountsAsync([oldPositionId, payload.PositionId], ct).ConfigureAwait(false);
        return (Map(person), oldJson);
    }

    private async Task<(PersonRow Row, string OldJson, long? PositionId)> ResignCoreAsync(
        long id, ResignPersonPayload payload, CancellationToken ct)
    {
        Person person = await FindPersonOrThrowAsync(id, ct).ConfigureAwait(false);
        string oldJson = Serialize(Map(person));
        long? positionId = person.PositionId;
        DateOnly leaveDate = payload.LeaveDate ?? Today();

        person.Resign(leaveDate);

        IReadOnlyList<PersonAssignment> assignments = await repo
            .ListPersonAssignmentsAsync(id, ct).ConfigureAwait(false);
        foreach (PersonAssignment assignment in assignments.Where(static a => a.IsCurrent))
        {
            assignment.Close(leaveDate, NullIfBlank(payload.Reason));
            await repo.UpdatePersonAssignmentAsync(assignment, ct).ConfigureAwait(false);
        }

        await repo.UpdatePersonAsync(person, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await RefreshPositionCountsAsync([positionId], ct).ConfigureAwait(false);
        return (Map(person), oldJson, positionId);
    }

    private async Task<(PersonRow Row, string OldJson)> RehireCoreAsync(
        long id, RehirePersonPayload payload, CancellationToken ct)
    {
        Person person = await FindPersonOrThrowAsync(id, ct).ConfigureAwait(false);
        string oldJson = Serialize(Map(person));
        long? oldPositionId = person.PositionId;
        DateOnly entryDate = payload.EntryDate ?? Today();
        EmployStatus employStatus = ParseEmployStatus(payload.EmployStatus ?? (int)EmployStatus.Probation, allowLeft: false);

        await EnsureReferencesAsync(payload.OrgId, payload.PositionId, person.PersonType, ct).ConfigureAwait(false);
        await EnsureNotOverstaffedAsync(payload.PositionId, ct).ConfigureAwait(false);

        person.Rehire(entryDate, payload.OrgId, payload.PositionId, employStatus);

        await repo.AddPersonAssignmentAsync(
            new PersonAssignment(
                TenantId,
                person.Id,
                payload.OrgId,
                payload.PositionId,
                entryDate,
                "再入职"),
            ct).ConfigureAwait(false);

        await repo.UpdatePersonAsync(person, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await RefreshPositionCountsAsync([oldPositionId, payload.PositionId], ct).ConfigureAwait(false);
        return (Map(person), oldJson);
    }

    private async Task EnsureReferencesAsync(
        long? orgId, long? positionId, PersonType personType, CancellationToken ct)
    {
        if (personType == PersonType.Employee && (orgId is null || positionId is null))
        {
            throw new ValidationException("orgId", "员工必须指定所属组织与岗位");
        }

        if (orgId.HasValue)
        {
            _ = await repo.FindOrgAsync(orgId.Value, ct).ConfigureAwait(false)
                ?? throw new NotFoundException(
                    "组织", orgId.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (positionId.HasValue)
        {
            _ = await repo.FindPositionAsync(positionId.Value, ct).ConfigureAwait(false)
                ?? throw new NotFoundException(
                    "岗位", positionId.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>超编守卫：入职/调岗目标岗位在岗数 +1 超过编制时阻断（审批走岗位超编申请端点）。</summary>
    private async Task EnsureNotOverstaffedAsync(long? positionId, CancellationToken ct)
    {
        if (!positionId.HasValue)
        {
            return;
        }

        Position position = await repo.FindPositionAsync(positionId.Value, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(
                "岗位", positionId.Value.ToString(CultureInfo.InvariantCulture));

        if (position.CurrentCount + 1 > position.Headcount)
        {
            throw new ConflictException(
                "岗位已超编，请先提交超编审批申请",
                "POSITION_OVERSTAFF",
                new
                {
                    PositionId = position.Id,
                    PositionName = position.Name,
                    position.CurrentCount,
                    position.Headcount,
                });
        }
    }

    /// <summary>按当前任职记录 + 非离职状态重算受影响岗位在岗数（批量查询，禁 N+1）。</summary>
    private async Task RefreshPositionCountsAsync(
        IReadOnlyCollection<long?> positionIds, CancellationToken ct)
    {
        long[] ids =
        [
            .. positionIds
                .Where(static id => id.HasValue)
                .Select(static id => id!.Value)
                .Distinct(),
        ];

        if (ids.Length == 0)
        {
            return;
        }

        IReadOnlyDictionary<long, int> counts = await repo
            .CountActivePersonsByPositionsAsync(ids, ct).ConfigureAwait(false);
        IReadOnlyList<Position> positions = await repo
            .ListPositionsByIdsAsync(ids, ct).ConfigureAwait(false);

        foreach (Position position in positions)
        {
            position.SetCurrentCount(counts.TryGetValue(position.Id, out int count) ? count : 0);
            await repo.UpdatePositionAsync(position, ct).ConfigureAwait(false);
        }

        if (positions.Count > 0)
        {
            _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task EnsureIdNoNotDuplicatedAsync(string idNo, long? excludeId, CancellationToken ct)
    {
        string hash = ComputeIdNoHash(TenantId, idNo);
        if (await repo.IsPersonIdNoExistsAsync(hash, excludeId, ct).ConfigureAwait(false))
        {
            throw new ConflictException(
                "证件号已存在（同一证件号仅允许一条有效记录）", "PERSON_ID_NO_DUPLICATE");
        }
    }

    private async Task<Person> FindPersonOrThrowAsync(long id, CancellationToken ct)
        => await repo.FindPersonAsync(id, ct).ConfigureAwait(false)
           ?? throw new NotFoundException("人员", id.ToString(CultureInfo.InvariantCulture));

    private async Task<PersonCert> FindPersonCertOrThrowAsync(
        long personId, long certId, CancellationToken ct)
    {
        _ = await FindPersonOrThrowAsync(personId, ct).ConfigureAwait(false);

        PersonCert? cert = await repo.FindPersonCertAsync(certId, ct).ConfigureAwait(false);
        if (cert is null || cert.PersonId != personId)
        {
            throw new NotFoundException("人员证件", certId.ToString(CultureInfo.InvariantCulture));
        }

        return cert;
    }

    // ============ 审计与缓存 ============

    private Task CaptureAuditAsync(
        string operation, PersonRow row, string? oldJson, CancellationToken ct)
        => audit.CaptureChangeAsync(
            new AuditLogEntry(
                snowflake.NextId(),
                nameof(Person),
                row.Id,
                operation,
                oldJson,
                Serialize(row),
                ChangedBy,
                DateTimeOffset.UtcNow,
                TenantId),
            ct);

    private Task CaptureCertAuditAsync(
        string operation, PersonCertRow row, string? oldJson, CancellationToken ct)
        => audit.CaptureChangeAsync(
            new AuditLogEntry(
                snowflake.NextId(),
                nameof(PersonCert),
                row.Id,
                operation,
                oldJson,
                Serialize(row),
                ChangedBy,
                DateTimeOffset.UtcNow,
                TenantId),
            ct);

    private Task<long> InvalidateCacheAsync(CancellationToken ct)
        => cache.RemoveByPrefixAsync(
            $"{QlxCacheKey.TenantKeyPrefix}:{TenantId.ToString(CultureInfo.InvariantCulture)}:{QlxModuleCodes.Bas}:",
            ct);

    // ============ 映射、校验与脱敏 ============

    private PersonRow Map(Person person)
    {
        string? idNo = SafeUnprotect(person.IdNoCipher);
        string? mobile = SafeUnprotect(person.MobileCipher);
        string? bankCardNo = SafeUnprotect(person.BankCardNoCipher);

        return new PersonRow(
            person.Id,
            person.Code,
            person.Name,
            (int)person.PersonType,
            (int)person.IdType,
            MaskIdNo(idNo),
            MaskMobile(mobile),
            person.EmergencyContact,
            person.EmergencyPhone,
            person.OrgId,
            person.PositionId,
            (int)person.EmployStatus,
            person.EntryDate,
            person.LeaveDate,
            MaskBankCardNo(bankCardNo),
            person.UpdatedAt);
    }

    private static PersonAssignmentRow Map(PersonAssignment assignment)
        => new(
            assignment.Id,
            assignment.PersonId,
            assignment.OrgId,
            assignment.PositionId,
            assignment.StartDate,
            assignment.EndDate,
            assignment.ChangeReason,
            assignment.IsCurrent);

    private static PersonCertRow Map(PersonCert cert)
        => new(
            cert.Id,
            cert.PersonId,
            cert.CertType,
            cert.CertNo,
            cert.IssuedOn,
            cert.ExpiresOn,
            cert.Remark);

    private static string Serialize(object value)
        => JsonSerializer.Serialize(value, QlxJsonDefaults.PreserveNull);

    private string? SafeUnprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(cipher);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static string RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "人员姓名不能为空");
        }

        return name.Trim();
    }

    private static string NormalizeIdNo(string? idNo, PersonIdType idType)
    {
        if (string.IsNullOrWhiteSpace(idNo))
        {
            throw new ValidationException("idNo", "证件号不能为空");
        }

        string normalized = idNo.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (idType == PersonIdType.IdCard && !CommonValidationPatterns.IsCnIdCard(normalized))
        {
            throw new ValidationException("idNo", "身份证号格式不正确");
        }

        return normalized;
    }

    private static string NormalizeMobile(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile))
        {
            throw new ValidationException("mobile", "手机号不能为空");
        }

        string normalized = mobile.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!CommonValidationPatterns.IsCnMobilePhone(normalized))
        {
            throw new ValidationException("mobile", "手机号格式不正确");
        }

        return normalized;
    }

    private static string? NormalizeBankCardNo(string? bankCardNo)
        => string.IsNullOrWhiteSpace(bankCardNo)
            ? null
            : bankCardNo.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string ComputeIdNoHash(long tenantId, string normalizedIdNo)
        => Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Concat(tenantId.ToString(CultureInfo.InvariantCulture), ":", normalizedIdNo))));

    private static PersonType ParsePersonType(int value)
        => Enum.IsDefined(typeof(PersonType), value)
            ? (PersonType)value
            : throw new ValidationException(
                "personType", $"未知的人员类型：{value.ToString(CultureInfo.InvariantCulture)}");

    private static PersonIdType ParseIdType(int value)
        => Enum.IsDefined(typeof(PersonIdType), value)
            ? (PersonIdType)value
            : throw new ValidationException(
                "idType", $"未知的证件类型：{value.ToString(CultureInfo.InvariantCulture)}");

    private static EmployStatus ParseEmployStatus(int value, bool allowLeft)
        => !Enum.IsDefined(typeof(EmployStatus), value)
            ? throw new ValidationException(
                "employStatus", $"未知的在职状态：{value.ToString(CultureInfo.InvariantCulture)}")
            : value == (int)EmployStatus.Left && !allowLeft
                ? throw new ValidationException("employStatus", "新建/再入职不得直接指定离职状态")
                : (EmployStatus)value;

    private static void ValidateCertPayload(SavePersonCertPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.CertType))
        {
            throw new ValidationException("certType", "证书类型不能为空");
        }

        if (payload.IssuedOn.HasValue && payload.ExpiresOn.HasValue
            && payload.ExpiresOn.Value < payload.IssuedOn.Value)
        {
            throw new ValidationException("expiresOn", "到期日期不能早于发证日期");
        }
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>证件号脱敏：前 6 后 4；长度不足时前 2 后 2；极短值全掩码。</summary>
    private static string? MaskIdNo(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.Length >= 10)
        {
            return string.Concat(value.AsSpan(0, 6), "****", value.AsSpan(value.Length - 4));
        }

        return value.Length >= 4
            ? string.Concat(value.AsSpan(0, 2), "****", value.AsSpan(value.Length - 2))
            : "****";
    }

    /// <summary>手机号脱敏：前 3 后 4；长度不足时全掩码。</summary>
    private static string? MaskMobile(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length >= 7
            ? string.Concat(value.AsSpan(0, 3), "****", value.AsSpan(value.Length - 4))
            : "****";
    }

    /// <summary>银行卡号脱敏：仅保留后 4 位。</summary>
    private static string? MaskBankCardNo(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= 4
            ? "****"
            : string.Concat("****", value.AsSpan(value.Length - 4));
    }
}
