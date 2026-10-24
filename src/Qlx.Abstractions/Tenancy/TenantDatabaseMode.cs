/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 22:10:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 22:10:00 +08:00 中国标准时间 星期三
 * 描述：租户数据库模式枚举，区分租户共享库与独立库两种落库形态
 */

using System.ComponentModel;

namespace Qlx.Abstractions.Tenancy;

/// <summary>
/// 租户数据库模式。定义在 Abstractions 层，供多租户底座与 Portal 模块共同引用，
/// 避免 BuildingBlocks 反向依赖业务模块。
/// </summary>
public enum TenantDatabaseMode
{
    /// <summary>租户共享库：多个租户共用一套业务表，按 TenantId 隔离。</summary>
    [Description("共享库")]
    Shared = 1,

    /// <summary>独立库：每个租户一套独立数据库，物理隔离。</summary>
    [Description("独立库")]
    Isolated = 2,
}
