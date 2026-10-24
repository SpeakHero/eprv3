/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:55:00 +08:00 中国标准时间 星期四
 * 描述：文件域任务编码常量 + 业务域编码常量（集中定义，禁止散落硬编码）
 */

namespace Qlx.Modules.Support;

/// <summary>
/// 文件域任务编码常量。S18/D22 红线：文件服务所有对外方法内部只经 IJobScheduler.SubmitAsync。
/// 前 7 个为方案 §4.8 定义；后 6 个为 2B 为满足"全部方法任务化"补充的同步操作任务。
/// </summary>
public static class FileTaskCodes
{
    /// <summary>上传（SYNC：hash + 秒传 + 归类 + 落盘 + ACL + 落库）。</summary>
    public const string Upload = "file.upload";

    /// <summary>大文件分片组装（ASYNC，2B 桩，2C 实现）。</summary>
    public const string ChunkAssemble = "file.chunk.assemble";

    /// <summary>下载（SYNC：权限校验 + OpenRead）。</summary>
    public const string Download = "file.download";

    /// <summary>预览（SYNC 流式）。</summary>
    public const string Preview = "file.preview";

    /// <summary>格式转换（ASYNC，2B 桩，2C 实现）。</summary>
    public const string FormatConvert = "file.format.convert";

    /// <summary>回收站到期清理（ASYNC，2B 桩，2C 实现）。</summary>
    public const string RecycleCleanup = "file.recycle.cleanup";

    /// <summary>临时区到期清理（ASYNC，2B 桩，2C 实现）。</summary>
    public const string TempPurge = "file.temp.purge";

    /// <summary>ACL 条目整体设置（SYNC，ACL 变更任务化）。</summary>
    public const string AclSet = "file.acl.set";

    /// <summary>元数据查询（SYNC）。</summary>
    public const string Metadata = "file.metadata";

    /// <summary>目录/关键字文件列表（SYNC）。</summary>
    public const string List = "file.list";

    /// <summary>软删进回收站（SYNC）。</summary>
    public const string Delete = "file.delete";

    /// <summary>回收站还原（SYNC）。</summary>
    public const string Restore = "file.restore";

    /// <summary>回收站列表（SYNC）。</summary>
    public const string RecycleList = "file.recycle.list";
}

/// <summary>文件任务参数键常量（Parameters 字典统一入口）。</summary>
public static class FileJobParams
{
    /// <summary>上传内容流。</summary>
    public const string Content = "content";

    /// <summary>文件名（含扩展名）。</summary>
    public const string FileName = "fileName";

    /// <summary>MIME 类型。</summary>
    public const string ContentType = "contentType";

    /// <summary>大小（字节）。</summary>
    public const string SizeBytes = "sizeBytes";

    /// <summary>业务模块编码（归类匹配 biz_module）。</summary>
    public const string BizModule = "bizModule";

    /// <summary>单据类型编码（归类匹配 doc_type）。</summary>
    public const string DocType = "docType";

    /// <summary>业务单据 Id（可空）。</summary>
    public const string BizId = "bizId";

    /// <summary>操作人用户标识。</summary>
    public const string OperatorId = "operatorId";

    /// <summary>存储对象 Id。</summary>
    public const string FileId = "fileId";

    /// <summary>存储键（按存储键定位场景）。</summary>
    public const string StorageKey = "storageKey";

    /// <summary>内容 SHA-256 哈希（分片组装完整性校验）。</summary>
    public const string Hash = "hash";

    /// <summary>分片存储键集合（按上传顺序；大文件分片组装入参）。</summary>
    public const string ChunkKeys = "chunkKeys";

    /// <summary>业务域编码（分片组装落库时使用，缺省未归类域）。</summary>
    public const string BizDomain = "bizDomain";

    /// <summary>目标物化路径（缺省 <c>/{bizDomain}/{fileName}</c>）。</summary>
    public const string TargetPath = "targetPath";

    /// <summary>目标格式（格式转换，如 png/jpg/webp）。</summary>
    public const string TargetFormat = "targetFormat";

    /// <summary>待检查权限位（int）。</summary>
    public const string Permission = "permission";

    /// <summary>ACL 条目集合（AclEntryPayload 列表）。</summary>
    public const string AclEntries = "aclEntries";

    /// <summary>关键字（可空）。</summary>
    public const string Keyword = "keyword";

    /// <summary>ACL 操作类型（grant / revoke）。</summary>
    public const string Op = "op";

    /// <summary>ACL 主体类型（int，FileSubjectType）。</summary>
    public const string SubjectType = "subjectType";

    /// <summary>ACL 主体 Id。</summary>
    public const string SubjectId = "subjectId";

    /// <summary>ACL 权限位掩码（int，FilePermission）。</summary>
    public const string PermissionMask = "permissionMask";

    /// <summary>父目录 Id / 目录列表父级（可空）。</summary>
    public const string ParentId = "parentId";
}

/// <summary>业务域目录编码（种子骨架 + 归类默认域）。</summary>
public static class SupportFileDomains
{
    /// <summary>采购域。</summary>
    public const string Purchase = "purchase";

    /// <summary>销售域。</summary>
    public const string Sales = "sales";

    /// <summary>人力域。</summary>
    public const string Hr = "hr";

    /// <summary>财务域。</summary>
    public const string Finance = "finance";

    /// <summary>票据域（数电票保护目录）。</summary>
    public const string Invoice = "invoice";

    /// <summary>临时域。</summary>
    public const string Temp = "temp";

    /// <summary>未归类域（规则 R2：无命中规则落此域并告警）。</summary>
    public const string Unclassified = "unclassified";
}
