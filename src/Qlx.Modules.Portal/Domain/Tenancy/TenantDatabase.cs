/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户数据库连接信息实体，凭据经 Data Protection 加密存储；共享行凭据为空
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Tenancy;

namespace Qlx.Modules.Portal.Domain.Tenancy;

/// <summary>
/// 数据库状态。
/// </summary>
public enum TenantDatabaseStatus
{
    /// <summary>正常可用。</summary>
    Active = 1,

    /// <summary>正在执行迁移。</summary>
    Migrating = 2,

    /// <summary>已归档，只读访问。</summary>
    Archived = 3,
}

/// <summary>
/// 租户数据库连接信息（表前缀 por_）。门户库存本实体，
/// 运行时由租户数据库路由服务读取并组装连接串。用户名与密码字段**必须**经 ASP.NET Core Data Protection 加密存储。
/// 继承 <see cref="Qlx.Abstractions.Domain.AuditableEntity"/>：雪花 Id + 审计字段由拦截器自动填充。
/// </summary>
[Table("por_tenant_databases")]
[Index(nameof(DbName))]
[Index(nameof(Status))]
public sealed class TenantDatabase : Qlx.Abstractions.Domain.AuditableEntity
{
    /// <summary>关联租户 ID（外键 → por_tenants，一对一唯一）。</summary>
    public long TenantId { get; internal set; }

    /// <summary>数据库主机。</summary>
    [MaxLength(256)]
    public string DbHost { get; internal set; } = string.Empty;

    /// <summary>数据库端口（SQL Server 默认 1433）。</summary>
    public int DbPort { get; internal set; } = PortalConstants.ProvisioningDefaults.DbPort;

    /// <summary>数据库名称。</summary>
    [MaxLength(128)]
    public string DbName { get; internal set; } = string.Empty;

    /// <summary>加密后的数据库用户名。</summary>
    [MaxLength(512)]
    public string EncryptedDbUser { get; private set; } = string.Empty;

    /// <summary>加密后的数据库密码。</summary>
    [MaxLength(512)]
    public string EncryptedDbPassword { get; private set; } = string.Empty;

    /// <summary>加密盐（Data Protection 每次生成的 salt，用于解密时校验密钥）。</summary>
    [MaxLength(256)]
    public string? EncryptionSalt { get; private set; }

    /// <summary>数据库状态。</summary>
    public TenantDatabaseStatus Status { get; private set; } = TenantDatabaseStatus.Active;

    /// <summary>数据库模式（共享行凭据为空，统一走租户共享库连接）。</summary>
    public TenantDatabaseMode DatabaseMode { get; private set; } = TenantDatabaseMode.Isolated;

    /// <summary>共享池编码（未来多共享池预留，本期固定 default）。</summary>
    [MaxLength(32)]
    public string PoolCode { get; private set; } = PortalConstants.ProvisioningDefaults.DefaultPoolCode;

    /// <summary>连接超时（秒），默认 30。</summary>
    public int ConnectTimeoutSeconds { get; private set; } = PortalConstants.ProvisioningDefaults.ConnectTimeoutSeconds;

    /// <summary>启用多个活动结果集（MARS），默认 true。</summary>
    public bool MultipleActiveResultSets { get; private set; } = true;

    /// <summary>连接池最小大小，默认 0。</summary>
    public int MinPoolSize { get; private set; }

    /// <summary>连接池最大大小，默认 50。</summary>
    public int MaxPoolSize { get; private set; } = 50;

    /// <summary>关联租户（一对一导航）。</summary>
    public Tenant? Tenant { get; internal set; }

    /// <summary>
    /// EF Core 无参构造。
    /// </summary>
    public TenantDatabase() { }

    /// <summary>
    /// 创建独立租户库连接信息（凭据后续由预配流程写入）。
    /// </summary>
    public TenantDatabase(Tenant tenant, string dbHost, int dbPort, string dbName)
        : this(tenant, TenantDatabaseMode.Isolated, dbHost, dbPort, dbName)
    {
    }

    /// <summary>
    /// 按数据库模式创建连接信息。共享模式不写凭据（凭据留空表示使用共享库统一连接），
    /// 独立模式由后续流程写入加密凭据。
    /// </summary>
    /// <param name="tenant">所属租户。</param>
    /// <param name="databaseMode">数据库模式。</param>
    /// <param name="dbHost">数据库主机。</param>
    /// <param name="dbPort">数据库端口。</param>
    /// <param name="dbName">数据库名称（共享模式为租户共享库名）。</param>
    /// <param name="poolCode">共享池编码（为空时取 default）。</param>
    public TenantDatabase(
        Tenant tenant, TenantDatabaseMode databaseMode, string dbHost, int dbPort, string dbName, string? poolCode = null)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(dbHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(dbName);

        Tenant = tenant;
        DatabaseMode = databaseMode;
        DbHost = dbHost;
        DbPort = dbPort;
        DbName = dbName;
        PoolCode = string.IsNullOrWhiteSpace(poolCode) ? PortalConstants.ProvisioningDefaults.DefaultPoolCode : poolCode;
    }

    /// <summary>
    /// 设置加密后的凭据（门户侧经 Data Protection 加密后调用）。
    /// </summary>
    public void SetEncryptedCredentials(string encryptedUser, string encryptedPassword, string? salt = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(encryptedUser);
        ArgumentException.ThrowIfNullOrEmpty(encryptedPassword);

        EncryptedDbUser = encryptedUser;
        EncryptedDbPassword = encryptedPassword;
        EncryptionSalt = salt;
    }

    /// <summary>
    /// 设置连接池参数。
    /// </summary>
    public void ConfigurePool(int minSize, int maxSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSize, 1);
        if (minSize > maxSize) throw new ArgumentException("MinPoolSize 不能大于 MaxPoolSize");

        MinPoolSize = minSize;
        MaxPoolSize = maxSize;
    }

    /// <summary>
    /// 标记迁移状态。
    /// </summary>
    public void MarkMigrating()
    {
        Status = TenantDatabaseStatus.Migrating;
    }

    /// <summary>
    /// 标记迁移完成，恢复 Active。
    /// </summary>
    public void MarkActive()
    {
        Status = TenantDatabaseStatus.Active;
    }

    /// <summary>
    /// 重定位路由行到新的数据库位置并切换为独立库模式（升级独立库 Cutover 时由门户仓储调用）。
    /// 模式必须同步落库：路由解析器按本行 <see cref="DatabaseMode"/> 判定共享/独立分支，
    /// 仅改主机/库名会造成「租户已独立、路由仍走共享库」的错切（L-02 / e2e-6882）。
    /// </summary>
    /// <param name="dbHost">目标主机。</param>
    /// <param name="dbPort">目标端口。</param>
    /// <param name="dbName">目标库名。</param>
    internal void RelocateTo(string dbHost, int dbPort, string dbName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(dbName);

        DbHost = dbHost;
        DbPort = dbPort;
        DbName = dbName;
        DatabaseMode = TenantDatabaseMode.Isolated;
    }
}
