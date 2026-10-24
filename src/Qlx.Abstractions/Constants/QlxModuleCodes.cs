/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 描述：模块编码与表前缀常量，全局唯一入口，禁止模块内重复声明
 */

namespace Qlx.Abstractions.Constants;

/// <summary>
/// 全局统一的模块编码与数据库表前缀常量。
/// <para>
/// 模块编码用于 API 路由段、Token modules claim、许可判定、模块定义种子；
/// 表前缀用于各模块 <c>TenantDbContext</c> 的表名拼接。两者必须成对维护。
/// </para>
/// </summary>
public static class QlxModuleCodes
{
    /// <summary>门户管理模块（必备，不可授权）。</summary>
    public const string Portal = "portal";
    /// <summary>基础信息中心模块（必备）。</summary>
    public const string Bas = "bas";
    /// <summary>采购管理模块。</summary>
    public const string Pur = "pur";
    /// <summary>销售管理模块。</summary>
    public const string Sal = "sal";
    /// <summary>物流管理模块。</summary>
    public const string Wms = "wms";
    /// <summary>商品管理模块。</summary>
    public const string Stk = "stk";
    /// <summary>人员管理模块。</summary>
    public const string Hr = "hr";
    /// <summary>财务管理模块。</summary>
    public const string Fin = "fin";
    /// <summary>票据管理模块。</summary>
    public const string Inv = "inv";
    /// <summary>合同管理模块。</summary>
    public const string Ctr = "ctr";
    /// <summary>项目管理模块。</summary>
    public const string Prj = "prj";
    /// <summary>设备与租赁管理模块。</summary>
    public const string Eqp = "eqp";
    /// <summary>支撑服务中心模块（文件/任务/消息/审批/打印，2B 起承载 fil_ 等支撑表）。</summary>
    public const string Support = "support";

    /// <summary>
    /// 租户自管理域路由段。所有租户均具备该域能力，<b>不参与</b>模块许可校验。
    /// </summary>
    public const string TenantSelfService = "tenant";

    /// <summary>门户管理库表前缀。</summary>
    public const string PortalTablePrefix = "por_";
    /// <summary>基础信息中心表前缀。</summary>
    public const string BasTablePrefix = "bas_";
    /// <summary>支撑服务文件中心表前缀（Support 模块 2B 落地的文件域）。</summary>
    public const string SupportFileTablePrefix = "fil_";

    /// <summary>
    /// 必备模块编码集合（新建租户无条件授权；与模块定义 <c>IsMandatory</c> 保持一致）。
    /// </summary>
    public static readonly IReadOnlyList<string> Mandatory = [Portal, Bas];

    /// <summary>模块程序集命名空间根（<c>Qlx.Modules.{PascalCode}</c>）。</summary>
    public const string AssemblyNamespaceRoot = "Qlx.Modules.";

    /// <summary>
    /// 按模块编码解析数据库表前缀：门户/基础信息使用固定前缀，其余模块统一为 <c>{编码}_</c>。
    /// </summary>
    /// <param name="moduleCode">模块编码。</param>
    /// <returns>表前缀。</returns>
    public static string TablePrefix(string moduleCode) => moduleCode switch
    {
        Portal => PortalTablePrefix,
        Bas => BasTablePrefix,
        Support => SupportFileTablePrefix,
        _ => moduleCode + "_",
    };

    /// <summary>按模块编码解析程序集名（<c>Qlx.Modules.{PascalCode}</c>）。</summary>
    /// <param name="moduleCode">模块编码。</param>
    /// <returns>程序集名。</returns>
    public static string AssemblyName(string moduleCode)
        => AssemblyNamespaceRoot + char.ToUpperInvariant(moduleCode[0]) + moduleCode[1..];
}
