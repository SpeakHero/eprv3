/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：仓库域启停状态数值枚举（1 启用 / 2 停用），跨模块共享枚举不跨程序集引用
 */

namespace Qlx.Modules.Wms.Domain.Warehouse;

/// <summary>仓库域启停状态（与前端 1↔Enabled / 2↔Disabled 映射）。</summary>
public enum WarehouseStatus
{
    /// <summary>启用。</summary>
    Enabled = 1,

    /// <summary>停用。</summary>
    Disabled = 2,
}
