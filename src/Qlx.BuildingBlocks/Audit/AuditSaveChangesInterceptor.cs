/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-22 12:00:00 +08:00 中国标准时间 星期四
 * 描述：审计 SaveChanges 拦截器（B4 后仅保留审计字段填充 + 软删改写，变更快照由显式审计路径写入）
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Qlx.Abstractions.Domain;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.BuildingBlocks.Audit;

/// <summary>
/// EF Core SaveChanges 拦截器（B4 后仅保留审计字段填充 + 软删改写）。
/// 遍历 ChangeTracker Entries 自动：
/// ① 新增实体：填充 Id（雪花）、CreatedAt/CreatedBy；
/// ② 修改实体：填充 UpdatedAt/UpdatedBy；
/// ③ 软删实体（<c>FullAuditableEntity.IsDeleted=true</c>）：改写为 Update 而非 Delete。
/// <para>B4 起变更快照不再由本拦截器自动写入，改由显式审计路径（业务层/服务层）按需写入
/// <c>plog_audit</c>（平台管理动作）或 <c>log_audit</c>（租户内部，随 B4-04 租户日志体系落地）。</para>
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="snowflake">雪花 ID 生成器，用于新增实体 Id 赋值。</param>
/// <param name="userContext">当前用户上下文，用于填充 CreatedBy/UpdatedBy/DeletedBy；可为 null（未登录场景）。</param>
public sealed partial class AuditSaveChangesInterceptor(
    ISnowflakeIdGenerator snowflake,
    ICurrentUserContext? userContext = null) : SaveChangesInterceptor
{
    private readonly ISnowflakeIdGenerator _snowflake = snowflake;
    private readonly ICurrentUserContext? _userContext = userContext;


    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is null)
        {
            return result;
        }

        ApplyPreSaveMutations(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken)
    {
        if (eventData.Context is null)
        {
            return result;
        }

        ApplyPreSaveMutations(eventData.Context);
        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    private void ApplyPreSaveMutations(DbContext context)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var user = _userContext?.UserName ?? _userContext?.UserId;

        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    FillForAdd(entry, now, user);
                    break;
                case EntityState.Modified:
                    FillForModify(entry, now, user);
                    break;
                case EntityState.Deleted:
                    FillForSoftDelete(entry, now, user);
                    break;
            }
        }
    }

    private void FillForAdd(EntityEntry entry, DateTimeOffset now, string? user)
    {
        if (entry.Entity is Entity { Id: 0L })
        {
            // 元数据门控：应用侧生成（挂雪花值生成器 / 显式 Never 配置）的实体兜底赋值；
            // 数据库自增（IDENTITY 且无显式生成器，如 Bas 模块自增模型）实体跳过，交由数据库生成。
            // 常态下雪花值生成器已在 Add 时赋值（Id 非 0 不进本分支），此处仅覆盖残余手工置 0 路径
            PropertyEntry idProperty = entry.Property(nameof(Entity.Id));
            if (idProperty.Metadata.GetValueGeneratorFactory() is not null
                || idProperty.Metadata.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never)
            {
                idProperty.CurrentValue = _snowflake.NextId();
            }
        }

        if (entry.Entity is AuditableEntity)
        {
            SetProperty(entry, nameof(AuditableEntity.CreatedAt), now);
            SetProperty(entry, nameof(AuditableEntity.UpdatedAt), now);
            if (!string.IsNullOrWhiteSpace(user))
            {
                SetProperty(entry, nameof(AuditableEntity.CreatedBy), user);
                SetProperty(entry, nameof(AuditableEntity.UpdatedBy), user);
            }
        }
    }

    private static void FillForModify(EntityEntry entry, DateTimeOffset now, string? user)
    {
        if (entry.Entity is AuditableEntity)
        {
            SetProperty(entry, nameof(AuditableEntity.UpdatedAt), now);
            if (!string.IsNullOrWhiteSpace(user))
            {
                SetProperty(entry, nameof(AuditableEntity.UpdatedBy), user);
            }
        }
    }

    private static void FillForSoftDelete(EntityEntry entry, DateTimeOffset now, string? user)
    {
        // FullAuditableEntity 走软删：改写为 Modified + IsDeleted=true，不发 DELETE SQL
        if (entry.Entity is FullAuditableEntity)
        {
            entry.State = EntityState.Modified;
            SetProperty(entry, nameof(FullAuditableEntity.IsDeleted), true);
            SetProperty(entry, nameof(FullAuditableEntity.DeletedAt), now);
            if (!string.IsNullOrWhiteSpace(user))
            {
                SetProperty(entry, nameof(FullAuditableEntity.DeletedBy), user);
            }

            if (entry.Entity is AuditableEntity)
            {
                SetProperty(entry, nameof(AuditableEntity.UpdatedAt), now);
                if (!string.IsNullOrWhiteSpace(user))
                {
                    SetProperty(entry, nameof(AuditableEntity.UpdatedBy), user);
                }
            }
        }
    }

    private static void SetProperty(EntityEntry entry, string propertyName, object value)
    {
        try
        {
            PropertyEntry prop = entry.Property(propertyName);
            // 可空属性（如 DateTimeOffset? DeletedAt）按底层类型匹配
            Type targetType = Nullable.GetUnderlyingType(prop.Metadata.ClrType) ?? prop.Metadata.ClrType;
            if (targetType == value.GetType())
            {
                prop.CurrentValue = value;
            }
        }
        catch (InvalidOperationException)
        {
            // 属性不存在（非审计实体）：静默跳过
        }
    }
}
