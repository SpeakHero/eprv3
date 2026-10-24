/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 06:35:00 +08:00 中国标准时间 星期五
 * 描述：PlatformSharedDbContext 的 DbSet 集合
 */

using Microsoft.EntityFrameworkCore;

namespace Qlx.BuildingBlocks.MultiTenancy.PlatformShared;

public sealed partial class PlatformSharedDbContext
{
    /// <summary>全局消息（glb_messages：平台公告/全局通知，只存一份）。</summary>
    public DbSet<GlobalMessageEntity> Messages => Set<GlobalMessageEntity>();
}
