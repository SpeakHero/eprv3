/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 01:50:00 +08:00 中国标准时间 星期一
 * 描述：人员导出数据源——单次批量查询 + 证件号/手机号解密后脱敏投影（禁止明文外泄）
 */

using System.Security.Cryptography;

using Microsoft.AspNetCore.DataProtection;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.ImportExport;
using Qlx.Modules.Bas.Repositories;

namespace Qlx.Modules.Bas.Services.ImportExport;

/// <summary>
/// 人员导出数据源（模板 <c>bas.person.export</c>）。证件号与手机号解密后仅输出脱敏值
/// （前若干位 + 后 4 位），导出结果不含敏感明文。
/// </summary>
/// <param name="tenantCtx">当前租户上下文（交叉校验导出租户）。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="dataProtection">数据保护提供程序（敏感字段解密）。</param>
internal sealed class PersonExportProvider(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    IDataProtectionProvider dataProtection) : IExportDataProvider
{
    private readonly IDataProtector _protector =
        dataProtection.CreateProtector(QlxDataProtectionPurposes.BasPersonSensitive);

    /// <inheritdoc />
    public string TemplateCode => BasImportTemplateDefinitionProvider.PersonExport;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(
        IReadOnlyDictionary<string, object?> criteria,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        long currentTenantId = tenantCtx.TenantId
            ?? throw new ForbiddenException("未识别到租户上下文");
        long restoredTenantId = ImportRowValues.ResolveTenantId(tenantId, currentTenantId);

        IReadOnlyList<Domain.Person.Person> persons = await repo.ListPersonsForExportAsync(
            ImportRowValues.CriteriaInt32(criteria, "personType"),
            ImportRowValues.CriteriaInt32(criteria, "employStatus"),
            ImportRowValues.CriteriaString(criteria, "keyword"),
            cancellationToken).ConfigureAwait(false);

        return
        [
            .. persons
                .Where(p => p.TenantId == restoredTenantId)
                .Select(person => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["code"] = person.Code,
                    ["name"] = person.Name,
                    ["personType"] = (int)person.PersonType,
                    ["idType"] = (int)person.IdType,
                    ["idNoMasked"] = MaskIdNo(SafeUnprotect(person.IdNoCipher)),
                    ["mobileMasked"] = MaskMobile(SafeUnprotect(person.MobileCipher)),
                    ["orgId"] = person.OrgId,
                    ["positionId"] = person.PositionId,
                    ["employStatus"] = (int)person.EmployStatus,
                    ["entryDate"] = person.EntryDate,
                    ["leaveDate"] = person.LeaveDate,
                }),
        ];
    }

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
}
