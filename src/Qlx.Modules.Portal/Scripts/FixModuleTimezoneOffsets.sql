/*
 * 数据修复：将 por_tenant_modules 中非 UTC 偏移的历史时间字段归一为 +00:00。
 *
 * 背景：早期种子代码使用 DateTimeOffset.Now（本地 +08:00）写入 LicensedAt / CreatedAt，
 *       导致 6 行 support 模块记录偏移为 +08:00，其余记录为 +00:00，时区不一致。
 *       当前 TenantModule 初始化器已改用 DateTimeOffset.UtcNow，新数据不再产生此问题。
 *
 * 策略：保留墙上时间（wall-clock）不变，仅将偏移修正为 +00:00。
 *       即 2026-10-23T01:43:39.0885981+08:00 → 2026-10-23T01:43:39.0885981+00:00。
 *       （该时间本就按 UTC 写入，仅偏移被误置为 +08:00。）
 *
 * 执行范围：仅开发库。生产环境须经正式变更工单 + 备份后执行。
 */

UPDATE por_tenant_modules
SET
    LicensedAt = TODATETIMEOFFSET(CAST(LicensedAt AS datetime2), '+00:00'),
    CreatedAt  = TODATETIMEOFFSET(CAST(CreatedAt  AS datetime2), '+00:00')
WHERE DATENAME(tz, LicensedAt) <> '+00:00';
