/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 描述：宿主角色枚举，决定四级配置目录中门户级与租户级的加载分支
 */

namespace Qlx.BuildingBlocks.Configuration;

/// <summary>
/// 宿主部署角色。AllInOne 同时加载门户级与租户级配置（单体部署），
/// Portal / Tenant 分别对应门户服务与租户服务的独立部署形态。
/// </summary>
public enum QlxHostRole
{
    /// <summary>单体一体化部署，门户与租户配置全部加载。</summary>
    AllInOne = 0,

    /// <summary>门户服务，仅加载门户级配置。</summary>
    Portal = 1,

    /// <summary>租户服务，仅加载租户级配置。</summary>
    Tenant = 2,
}
