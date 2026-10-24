/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:30:00 +08:00 中国标准时间 星期四
 * 描述：long ↔ Guid 互转映射器（2A 任务契约 TenantId/UserId 为 Guid，进程内实体主键为 long 雪花 Id 的适配约定）
 */

using System.Buffers.Binary;

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// long（雪花 Id）与 Guid 的确定性互转映射器。
/// <para>
/// 背景：2A 已冻结契约（TaskSubmitRequest.TenantId / ITenantSubjectQuery 的 tenantId、portalUserId）使用
/// <see cref="Guid"/>，而进程内实体主键为 long 雪花 Id。本映射器将 long 的小端字节写入
/// Guid 的第 9~16 字节（Variant/Version 字节位之外不改动前三组），双向可逆且稳定。
/// </para>
/// <para>
/// 2B 临时约定：仅用于进程内 Support 模块适配层；2C job_ 表落地后将原生承载 Guid，届时迁移删除。
/// </para>
/// </summary>
public static class GuidIdMapper
{
    /// <summary>
    /// 将非负 long 雪花 Id 映射为稳定 Guid。
    /// </summary>
    /// <param name="id">雪花 Id（必须 &gt; 0）。</param>
    /// <returns>映射后的 Guid。</returns>
    public static Guid ToGuid(long id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        Span<byte> tail = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(tail, id);
        return new Guid(0, 0, 0, tail[0], tail[1], tail[2], tail[3], tail[4], tail[5], tail[6], tail[7]);
    }

    /// <summary>
    /// 将经 <see cref="ToGuid"/> 映射的 Guid 还原为 long。
    /// </summary>
    /// <param name="value">映射 Guid。</param>
    /// <param name="id">还原结果。</param>
    /// <returns>可还原（前 8 字节为 0 且 long 值 &gt; 0）返回 true。</returns>
    public static bool TryToLong(Guid value, out long id)
    {
        id = 0;
        Span<byte> bytes = stackalloc byte[16];
        if (!value.TryWriteBytes(bytes) || bytes[..8].IndexOfAnyExcept((byte)0) >= 0)
        {
            return false;
        }

        id = BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]);
        return id > 0;
    }
}
