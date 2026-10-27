/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：人员离职生效集成事件契约，Bas 离职事务提交后经 Outbox 持久发布，登录体系订阅停用账号。
 */

namespace Qlx.Abstractions.Events.Bas;

/// <summary>
/// 人员离职生效集成事件。
/// <para>Bas 人员离职生效、任职记录停用与岗位在岗数回写事务提交后，
/// 经 <c>IEventBus.PublishDurableAsync</c> 投递（Outbox，at-least-once），消费方必须按
/// <see cref="IntegrationEvent.EventId"/> 幂等处理。</para>
/// <para><see cref="IntegrationEvent.TenantCode"/> 由发布方以对象初始化器赋值。</para>
/// </summary>
public sealed record PersonLeftEvent : IntegrationEvent
{
    /// <summary>人员 Id（bas_persons.id）。</summary>
    public long PersonId { get; init; }

    /// <summary>人员编码。</summary>
    public required string PersonCode { get; init; }

    /// <summary>离职前所属组织 Id；未分配为 <see langword="null"/>。</summary>
    public long? OrgId { get; init; }

    /// <summary>离职前所属岗位 Id；未分配为 <see langword="null"/>。</summary>
    public long? PositionId { get; init; }

    /// <summary>离职生效时间（UTC）。</summary>
    public DateTimeOffset LeftAt { get; init; }
}
