/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 00:15:00 +08:00 中国标准时间 星期五
 * 描述：文件上传任务处理器（SYNC）：SHA-256 + 秒传检测 + 配额校验 + 归类规则匹配 + 落盘 + ACL 默认授予 + 落库
 */

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Files;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Serialization;
using Qlx.BuildingBlocks.Files;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Options;
using Qlx.Modules.Support.Services.Files;

namespace Qlx.Modules.Support.Jobs.Files;

/// <summary>
/// 文件上传处理器（file.upload，SYNC）。业务规则 R2（归类）/ R5（保护目录）/ R6（秒传引用计数）/ R7（配额）。
/// </summary>
public sealed partial class FileUploadHandler(
    SupportTenantDbContext db,
    IFileStorageBackend storageBackend,
    SupportSeedService seedService,
    IOptions<SupportFilesOptions> filesOptions,
    ILogger<FileUploadHandler> logger) : IJobHandler
{
    /// <summary>数电票保护目录触发扩展名（规则 R5）。</summary>
    private static readonly string[] s_invoiceProtectedExtensions = [".xml", ".ofd"];

    private readonly SupportFilesOptions _options = filesOptions.Value;
    private readonly ILogger<FileUploadHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => FileTaskCodes.Upload;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var content = GetRequiredParam<Stream>(context, FileJobParams.Content);
        var fileName = GetRequiredParam<string>(context, FileJobParams.FileName);
        var contentType = GetRequiredParam<string>(context, FileJobParams.ContentType);
        var sizeBytes = GetRequiredParam<long>(context, FileJobParams.SizeBytes);
        var operatorId = GetRequiredParam<string>(context, FileJobParams.OperatorId);
        var bizModule = context.Parameters.GetValueOrDefault(FileJobParams.BizModule) as string;
        var docType = context.Parameters.GetValueOrDefault(FileJobParams.DocType) as string;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // 1. 计算内容 SHA-256（非可 seek 流先缓冲）
        if (!content.CanSeek)
        {
            var buffered = new MemoryStream();
            await content.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
            content = buffered;
        }

        content.Position = 0;
        var hash = await FileJobHelpers.ComputeSha256HexAsync(content, cancellationToken).ConfigureAwait(false);
        content.Position = 0;

        var tenantId = db.CurrentTenantId;

        // 2. 配额校验（规则 R7）
        var usedBytes = await db.StorageObjects
            .Where(o => o.Status == StorageObjectStatus.Normal)
            .SumAsync(o => (long?)o.Size, cancellationToken)
            .ConfigureAwait(false) ?? 0;

        var blockThreshold = _options.QuotaTotalBytes * _options.QuotaBlockPercent / 100;
        if (usedBytes + sizeBytes > blockThreshold)
        {
            throw new ConflictException(
                $"存储配额已满（已用 {usedBytes} / 上限 {blockThreshold} 字节），上传被阻断。", "QUOTA_EXCEEDED");
        }

        var warnThreshold = _options.QuotaTotalBytes * _options.QuotaPercent / 100;
        string? warning = usedBytes + sizeBytes >= warnThreshold
            ? $"租户存储已接近配额（{usedBytes + sizeBytes}/{_options.QuotaTotalBytes} 字节）"
            : null;

        // 3. 租户种子（幂等：目录骨架 + ACL 模板 + 归类规则）
        await seedService.EnsureSeededAsync(tenantId, operatorId, cancellationToken).ConfigureAwait(false);

        // 4. 归类规则匹配（规则 R2：优先级大者先命中；无规则落 /unclassified）
        var rule = await MatchClassifyRuleAsync(tenantId, bizModule, docType, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var targetPath = ExpandTargetPath(rule?.TargetPath ?? $"/{SupportFileDomains.Unclassified}", now);
        var bizDomain = targetPath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? SupportFileDomains.Unclassified;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var protectSource = rule is { ProtectSource: true }
            || (string.Equals(bizDomain, SupportFileDomains.Invoice, StringComparison.Ordinal)
                && s_invoiceProtectedExtensions.Contains(extension));
        if (rule is null)
        {
            warning = $"{warning}未命中归类规则（bizModule={bizModule}，docType={docType}），文件落入未归类目录。".Trim();
            LogUnclassifiedUpload(bizModule ?? string.Empty, docType ?? string.Empty);
        }

        // 5. 秒传检测（规则 R6：同租户同 hash 复用物理文件，引用计数归零才物理删除）
        var existingVersion = await db.FileVersions
            .AsNoTracking()
            .Where(v => v.Hash == hash)
            .OrderBy(v => v.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existingVersion is { } hit)
        {
            var owner = await db.StorageObjects
                .SingleAsync(o => o.Id == hit.ObjectId, cancellationToken)
                .ConfigureAwait(false);

            var dedupObject = new StorageObject(tenantId, null, fileName, bizDomain, $"{targetPath.TrimEnd('/')}/{fileName}",
                hash, sizeBytes, contentType, operatorId);
            db.StorageObjects.Add(dedupObject);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var version = new FileVersion(tenantId, dedupObject.Id, 1, hash, sizeBytes, hit.StorageKey, operatorId, now);
            db.FileVersions.Add(version);
            owner.RefCount++;
            db.FileOpLogs.Add(new FileOpLog(tenantId, dedupObject.Id, "UPLOAD_DEDUPLICATED", operatorId, null,
                $"秒传命中：复用 {hit.StorageKey}（RefCount={owner.RefCount}）", now));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Ok(stopwatch, new FileUploadJobPayload(
                dedupObject.Id, hit.StorageKey, hash, sizeBytes, dedupObject.Path, bizDomain,
                Deduplicated: true, owner.RefCount, warning));
        }

        // 6. 首传落盘（保护目录物理只读，规则 R5）
        var storageKey = $"{hash[..2]}/{hash[2..4]}/{hash}.bin";
        await storageBackend.SaveAsync(storageKey, content, protectSource, cancellationToken).ConfigureAwait(false);

        var storageObject = new StorageObject(tenantId, null, fileName, bizDomain, $"{targetPath.TrimEnd('/')}/{fileName}",
            hash, sizeBytes, contentType, operatorId);
        db.StorageObjects.Add(storageObject);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        db.FileVersions.Add(new FileVersion(tenantId, storageObject.Id, 1, hash, sizeBytes, storageKey, operatorId, now));
        db.FileOpLogs.Add(new FileOpLog(tenantId, storageObject.Id, "UPLOAD", operatorId, null,
            protectSource ? "上传（保护目录，物理只读）" : "上传", now));

        // 7. ACL 默认授予：命中模板 → 快照模板条目；保护目录 → 全主体 DENY 写/删
        if (rule?.AclTplId is { } tplId)
        {
            await ApplyTemplateAsync(tenantId, storageObject.Id, tplId, operatorId, now, cancellationToken)
                .ConfigureAwait(false);
        }

        if (protectSource)
        {
            db.FileAclEntries.Add(new FileAclEntry(tenantId, storageObject.Id, FileSubjectType.User,
                FileAclEngine.WildcardSubjectId, FilePermission.Write | FilePermission.Delete, AceType.Deny,
                propagate: true, inheritSrc: null, grantedBy: operatorId, grantedAt: now));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(stopwatch, new FileUploadJobPayload(
            storageObject.Id, storageKey, hash, sizeBytes, storageObject.Path, bizDomain,
            Deduplicated: false, RefCount: 1, warning));
    }

    private static CompletedResult Ok(System.Diagnostics.Stopwatch stopwatch, FileUploadJobPayload payload)
    {
        stopwatch.Stop();
        return new CompletedResult(payload, null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "上传未命中归类规则：{BizModule}/{DocType}")]
    private partial void LogUnclassifiedUpload(string bizModule, string docType);

    private async Task<ClassifyRule?> MatchClassifyRuleAsync(
        long tenantId, string? bizModule, string? docType, CancellationToken cancellationToken)
    {
        var rules = await db.ClassifyRules
            .AsNoTracking()
            .Where(r => r.Status == SupportConfigStatus.Enabled)
            .Where(r => r.BizModule == bizModule || r.BizModule == "*")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rules
            .Where(r => r.DocType == docType || r.DocType == "*")
            .OrderByDescending(static r => r.Priority)
            .FirstOrDefault();
    }

    private static string ExpandTargetPath(string template, DateTimeOffset now)
        => template
            .Replace("{yyyy}", now.ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{MM}", now.ToString("MM", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private async Task ApplyTemplateAsync(long tenantId, long objectId, long aclTplId, string operatorId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var template = await db.AclTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == aclTplId, cancellationToken)
            .ConfigureAwait(false);
        if (template is null)
        {
            return;
        }

        var payloadEntries = JsonSerializer.Deserialize<List<AclEntryPayload>>(template.EntriesJson, QlxJsonDefaults.Default);
        if (payloadEntries is null)
        {
            return;
        }

        var entries = payloadEntries
            .Select(p => new FileAclEntry(tenantId, objectId, (FileSubjectType)p.SubjectType, p.SubjectId,
                (FilePermission)p.Permission, (AceType)p.AceType, p.Propagate, inheritSrc: null, operatorId, now))
            .ToList();
        if (entries.Count > 0)
        {
            db.FileAclEntries.AddRange(entries);
        }
    }

    private static T GetRequiredParam<T>(JobContext context, string key)
        where T : notnull
    {
        if (!context.Parameters.TryGetValue(key, out var value) || value is not T typed)
        {
            throw new InvalidOperationException($"任务 {context.TaskCode} 缺少参数 {key}");
        }

        return typed;
    }
}
