/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 14:20:00 +08:00 中国标准时间 星期五
 * 描述：导入导出域任务编码常量 + 参数键常量（集中定义，禁止散落硬编码）
 */

namespace Qlx.Modules.Support;

/// <summary>
/// 导入导出域任务编码常量（2F）。大数据量导出 / 批量导入走 ASYNC 任务模式：
/// 导出产出文件经文件中心存储（临时域，TTL 到期由 file.temp.purge 清理），
/// 导入源文件为已上传文件中心的 Excel；完成 / 结果经信息中心站内通知操作人。
/// </summary>
public static class ImportExportTaskCodes
{
    /// <summary>大数据量导出（ASYNC：投影 → 落盘文件中心 → 站内通知）。</summary>
    public const string Export = "file.export";

    /// <summary>批量导入（ASYNC：按已上传 Excel 执行导入 → 结果站内通知）。</summary>
    public const string BatchImport = "file.import.batch";
}

/// <summary>导入导出任务参数键常量（Parameters 字典统一入口）。</summary>
public static class ImportExportJobParams
{
    /// <summary>导入 / 导出模板编码。</summary>
    public const string TemplateCode = "templateCode";

    /// <summary>导出条件（IReadOnlyDictionary&lt;string, object?&gt;，业务模块自定义键值）。</summary>
    public const string Criteria = "criteria";

    /// <summary>已上传的导入 Excel 文件 Id（StorageObject.Id）。</summary>
    public const string FileId = "fileId";

    /// <summary>操作人用户标识（通知接收人）。</summary>
    public const string OperatorId = "operatorId";
}
