/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：雪花 ID 生成器契约，全平台业务主键唯一来源
 */

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// 雪花 ID 生成器。所有业务主键（bigint）统一由本生成器产出，进程内单例。
/// </summary>
public interface ISnowflakeIdGenerator
{
    /// <summary>
    /// 生成下一个全局唯一 ID。
    /// </summary>
    /// <returns>正数长整型 ID。</returns>
    /// <exception cref="InvalidOperationException">生成器尚未完成 workerId 租约初始化或时钟回拨超限时抛出。</exception>
    long NextId();
}
