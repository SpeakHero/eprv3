/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 01:20:00 +08:00 中国标准时间 星期一
 * 描述：人员导入落点——内存行级校验（含行号）+ 证件号哈希批量防重 + 组织/岗位批量引用校验 +
 *       统一发号器编码 + 敏感字段加密落库 + 任职记录与岗位在岗数回写
 */

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.DataProtection;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.ImportExport;
using Qlx.BuildingBlocks.Validation;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Domain.Person;
using Qlx.Modules.Bas.Repositories;
using Qlx.Modules.Bas.Services.Numbering;

namespace Qlx.Modules.Bas.Services.ImportExport;

/// <summary>
/// 人员导入行落点（模板 <c>bas.person.import</c>）。身份证号 / 手机号按现有人员服务同一规则规范化与校验，
/// 证件号仅存 Data Protection 密文与 SHA-256 哈希；组织 / 岗位经批量查询校验后单次保存。
/// </summary>
/// <param name="tenantCtx">当前租户上下文（交叉校验导入租户）。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="codeNumberGenerator">统一发号器（人员编码）。</param>
/// <param name="dataProtection">数据保护提供程序（敏感字段加密）。</param>
internal sealed class PersonImportSink(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    IBasCodeNumberGenerator codeNumberGenerator,
    IDataProtectionProvider dataProtection) : IImportRowSink
{
    private const string EmployeeCodeBizType = "employee";

    private const string LabourCodeBizType = "labour";

    private readonly IDataProtector _protector =
        dataProtection.CreateProtector(QlxDataProtectionPurposes.BasPersonSensitive);

    /// <inheritdoc />
    public string TemplateCode => BasImportTemplateDefinitionProvider.PersonImport;

    /// <inheritdoc />
    public async Task<int> WriteRowsAsync(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        long currentTenantId = tenantCtx.TenantId
            ?? throw new ForbiddenException("未识别到租户上下文");
        long restoredTenantId = ImportRowValues.ResolveTenantId(tenantId, currentTenantId);

        List<FieldError> errors = [];
        List<ParsedRow> parsed = ParseRows(restoredTenantId, rows, errors);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        IReadOnlyCollection<string> existingHashes = await repo
            .ListExistingPersonIdNoHashesAsync([.. parsed.Select(static r => r.IdNoHash)], cancellationToken)
            .ConfigureAwait(false);
        foreach (ParsedRow row in parsed.Where(r => existingHashes.Contains(r.IdNoHash)))
        {
            errors.Add(new FieldError(
                "idNo", $"第 {row.RowNumber.ToString(CultureInfo.InvariantCulture)} 行：证件号已存在（同一证件号仅允许一条有效记录）"));
        }

        await EnsureReferencesExistAsync(parsed, errors, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        List<(ParsedRow Row, Person Person)> created = [];
        foreach (ParsedRow row in parsed)
        {
            string bizType = row.PersonType == PersonType.Employee ? EmployeeCodeBizType : LabourCodeBizType;
            string code = await codeNumberGenerator.NextAsync(
                bizType,
                restoredTenantId,
                parentCode: null,
                typeCode: null,
                isConflict: (candidate, token) => repo.IsPersonCodeExistsAsync(candidate, token),
                ct: cancellationToken).ConfigureAwait(false);

            Person person = new(
                restoredTenantId,
                code,
                row.Name,
                row.PersonType,
                row.IdType,
                _protector.Protect(row.IdNo),
                row.IdNoHash,
                _protector.Protect(row.Mobile),
                row.EntryDate,
                row.BankCardNo is null ? null : _protector.Protect(row.BankCardNo));
            person.Assign(row.OrgId, row.PositionId);

            await repo.AddPersonAsync(person, cancellationToken).ConfigureAwait(false);
            created.Add((row, person));
        }

        _ = await repo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach ((ParsedRow row, Person person) in created.Where(static x => x.Row.OrgId.HasValue))
        {
            await repo.AddPersonAssignmentAsync(
                new PersonAssignment(
                    restoredTenantId, person.Id, row.OrgId, row.PositionId, row.EntryDate),
                cancellationToken).ConfigureAwait(false);
        }

        _ = await repo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await RefreshPositionCountsAsync(created, cancellationToken).ConfigureAwait(false);
        return created.Count;
    }

    private static List<ParsedRow> ParseRows(
        long tenantId,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        List<FieldError> errors)
    {
        List<ParsedRow> parsed = [];
        Dictionary<string, int> firstRowByHash = new(StringComparer.Ordinal);

        for (int index = 0; index < rows.Count; index++)
        {
            IReadOnlyDictionary<string, object?> row = rows[index];
            int rowNumber = ImportRowValues.RowNumber(index);

            string? name = ImportRowValues.ReadString(row, "name");
            if (name is null)
            {
                errors.Add(new FieldError("name", RowMessage(rowNumber, "列「姓名」不能为空")));
            }

            PersonType? personType = ParseEnum<PersonType>(ImportRowValues.ReadString(row, "personType"));
            if (personType is null)
            {
                errors.Add(new FieldError("personType", RowMessage(rowNumber, "列「人员类型」不是有效取值（1~2）")));
            }

            PersonIdType? idType = ParseEnum<PersonIdType>(ImportRowValues.ReadString(row, "idType"));
            if (idType is null)
            {
                errors.Add(new FieldError("idType", RowMessage(rowNumber, "列「证件类型」不是有效取值（1~3）")));
            }

            string? idNo = NormalizeIdNo(ImportRowValues.ReadString(row, "idNo"));
            if (idNo is null)
            {
                errors.Add(new FieldError("idNo", RowMessage(rowNumber, "列「证件号」不能为空")));
            }
            else if (idType == PersonIdType.IdCard && !CommonValidationPatterns.IsCnIdCard(idNo))
            {
                errors.Add(new FieldError("idNo", RowMessage(rowNumber, "身份证号格式不正确")));
            }

            string? mobile = NormalizeMobile(ImportRowValues.ReadString(row, "mobile"));
            if (mobile is null)
            {
                errors.Add(new FieldError("mobile", RowMessage(rowNumber, "列「手机号」不能为空")));
            }
            else if (!CommonValidationPatterns.IsCnMobilePhone(mobile))
            {
                errors.Add(new FieldError("mobile", RowMessage(rowNumber, "手机号格式不正确")));
            }

            long? orgId = ParseId(ImportRowValues.ReadString(row, "orgId"));
            long? positionId = ParseId(ImportRowValues.ReadString(row, "positionId"));
            if (ImportRowValues.ReadString(row, "orgId") is not null && orgId is null)
            {
                errors.Add(new FieldError("orgId", RowMessage(rowNumber, "列「所属组织Id」不是有效的正整数")));
            }

            if (ImportRowValues.ReadString(row, "positionId") is not null && positionId is null)
            {
                errors.Add(new FieldError("positionId", RowMessage(rowNumber, "列「所属岗位Id」不是有效的正整数")));
            }

            if (personType == PersonType.Employee && (orgId is null || positionId is null))
            {
                errors.Add(new FieldError("orgId", RowMessage(rowNumber, "员工必须指定所属组织与岗位")));
            }

            if (name is null || personType is null || idType is null || idNo is null || mobile is null)
            {
                continue;
            }

            string idNoHash = ComputeIdNoHash(tenantId, idNo);
            if (firstRowByHash.TryGetValue(idNoHash, out int firstRow))
            {
                errors.Add(new FieldError(
                    "idNo",
                    RowMessage(
                        rowNumber,
                        $"证件号与第 {firstRow.ToString(CultureInfo.InvariantCulture)} 行重复（{MaskIdNo(idNo)}）")));
                continue;
            }

            firstRowByHash[idNoHash] = rowNumber;
            parsed.Add(new ParsedRow(
                rowNumber,
                name,
                personType.Value,
                idType.Value,
                idNo,
                idNoHash,
                mobile,
                orgId,
                positionId,
                ImportRowValues.ReadDateOnly(row, "entryDate") ?? DateOnly.FromDateTime(DateTime.UtcNow),
                NormalizeBankCardNo(ImportRowValues.ReadString(row, "bankCardNo"))));
        }

        return parsed;
    }

    /// <summary>批量引用校验：组织一次全量查询 + 岗位一次 IN 查询（禁逐行往返）。</summary>
    private async Task EnsureReferencesExistAsync(
        List<ParsedRow> parsed, List<FieldError> errors, CancellationToken ct)
    {
        HashSet<long> referencedOrgIds = [.. parsed.Where(static r => r.OrgId.HasValue).Select(static r => r.OrgId!.Value)];
        if (referencedOrgIds.Count > 0)
        {
            HashSet<long> existingOrgIds = [.. (await repo.ListAllOrgsAsync(ct).ConfigureAwait(false))
                .Select(static o => o.Id)];
            foreach (ParsedRow row in parsed.Where(r => r.OrgId.HasValue && !existingOrgIds.Contains(r.OrgId.Value)))
            {
                errors.Add(new FieldError(
                    "orgId",
                    RowMessage(row.RowNumber, "所属组织不存在")));
            }
        }

        long[] positionIds =
        [
            .. parsed
                .Where(static r => r.PositionId.HasValue)
                .Select(static r => r.PositionId!.Value)
                .Distinct(),
        ];

        if (positionIds.Length > 0)
        {
            HashSet<long> existingPositionIds = [.. (await repo.ListPositionsByIdsAsync(positionIds, ct)
                .ConfigureAwait(false)).Select(static p => p.Id)];
            foreach (ParsedRow row in parsed.Where(r => r.PositionId.HasValue && !existingPositionIds.Contains(r.PositionId.Value)))
            {
                errors.Add(new FieldError(
                    "positionId",
                    RowMessage(row.RowNumber, "所属岗位不存在")));
            }
        }
    }

    /// <summary>按受影响岗位批量重算在岗数（单条 GROUP BY，禁 N+1）。</summary>
    private async Task RefreshPositionCountsAsync(
        List<(ParsedRow Row, Person Person)> created, CancellationToken ct)
    {
        long[] positionIds =
        [
            .. created
                .Where(static x => x.Row.PositionId.HasValue)
                .Select(static x => x.Row.PositionId!.Value)
                .Distinct(),
        ];

        if (positionIds.Length == 0)
        {
            return;
        }

        IReadOnlyDictionary<long, int> counts = await repo
            .CountActivePersonsByPositionsAsync(positionIds, ct).ConfigureAwait(false);
        IReadOnlyList<Position> positions = await repo.ListPositionsByIdsAsync(positionIds, ct).ConfigureAwait(false);

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

    private static TEnum? ParseEnum<TEnum>(string? text)
        where TEnum : struct, Enum
        => text is not null
           && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
           && Enum.IsDefined(typeof(TEnum), value)
            ? (TEnum)(object)value
            : null;

    private static long? ParseId(string? text)
        => text is not null
           && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
           && value > 0
            ? value
            : null;

    private static string? NormalizeIdNo(string? idNo)
        => string.IsNullOrWhiteSpace(idNo)
            ? null
            : idNo.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    private static string? NormalizeMobile(string? mobile)
        => string.IsNullOrWhiteSpace(mobile)
            ? null
            : mobile.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string? NormalizeBankCardNo(string? bankCardNo)
        => string.IsNullOrWhiteSpace(bankCardNo)
            ? null
            : bankCardNo.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string ComputeIdNoHash(long tenantId, string normalizedIdNo)
        => Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Concat(tenantId.ToString(CultureInfo.InvariantCulture), ":", normalizedIdNo))));

    private static string MaskIdNo(string value)
        => value.Length >= 10
            ? string.Concat(value.AsSpan(0, 6), "****", value.AsSpan(value.Length - 4))
            : "****";

    private static string RowMessage(int rowNumber, string message)
        => $"第 {rowNumber.ToString(CultureInfo.InvariantCulture)} 行：{message}";

    private sealed record ParsedRow(
        int RowNumber,
        string Name,
        PersonType PersonType,
        PersonIdType IdType,
        string IdNo,
        string IdNoHash,
        string Mobile,
        long? OrgId,
        long? PositionId,
        DateOnly EntryDate,
        string? BankCardNo);
}
