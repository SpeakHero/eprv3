/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 描述：公共常量（模块编码、系统操作人、目标类型），全局唯一入口
 */

namespace Qlx.Abstractions.Constants;

/// <summary>
/// 全局统一的内容类型常量。
/// </summary>
public static class QlxContentTypesConstants
{
    /// <summary>
    /// JSON 内容类型。 
    /// </summary>
    public const string Json = "application/json; charset=utf-8";
    /// <summary>
    /// XML 内容类型。 
    /// </summary>
    public const string Xml = "application/xml; charset=utf-8";

    ///<summary>
    /// HTML 内容类型。 
    /// </summary>
    public const string Html = "text/html; charset=utf-8";
    /// <summary>
    /// TEXT 内容类型。 
    /// </summary>      
    public const string Text = "text/plain; charset=utf-8";

    /// <summary>
    /// 图片内容类型。 
    /// </summary>
    public const string Image = "image/*";

    /// <summary>
    /// 二进制内容类型。 
    /// </summary>
    public const string Binary = "application/octet-stream";
    /// <summary>
    /// 音频内容类型。 
    /// </summary>
    public const string Audio = "audio/*";
    /// <summary>
    /// 视频内容类型。 
    /// </summary>
    public const string Video = "video/*";

    /// <summary>
    /// 所有内容类型。 
    /// </summary>
    public static readonly IReadOnlyList<string> All = [Json, Xml, Html, Text, Image, Binary, Audio, Video];

}