/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 23:50:00 +08:00 中国标准时间 星期日
 * 描述：Bas 变更日志 DTO——复用审计库快照的扁平行，敏感字段读取时已脱敏
 */

namespace Qlx.Modules.Bas.Dtos;

/// <summary>
/// 变更日志扁平行（原始字段）。<c>OldValues</c>/<c>NewValues</c> 为已脱敏的快照 JSON 文本。
/// </summary>
/// <param name="EventId">审计事件 Id。</param>
/// <param name="EntityType">对象类型（实体名）。</param>
/// <param name="EntityId">对象 Id。</param>
/// <param name="Operation">操作（INSERT / UPDATE / SOFT_DELETE / DELETE / VIEW_PLAINTEXT）。</param>
/// <param name="OldValues">变更前快照 JSON（脱敏，新增为空）。</param>
/// <param name="NewValues">变更后快照 JSON（脱敏，删除为空）。</param>
/// <param name="ChangedBy">操作人。</param>
/// <param name="ChangedAt">操作时间。</param>
public sealed record ChangeLogRow(
    long EventId,
    string EntityType,
    long EntityId,
    string Operation,
    string? OldValues,
    string? NewValues,
    string ChangedBy,
    DateTimeOffset ChangedAt);
