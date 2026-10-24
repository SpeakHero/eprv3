/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:40:00 +08:00 中国标准时间 星期四
 * 描述：PortalDbContext 种子数据（模块定义 / 权限点固定 ID，随首次 Migration 写入）
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Constants;
using PermissionKind = Qlx.Abstractions.Modules.PermissionKind;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal;

public sealed partial class PortalDbContext
{
    private static readonly DateTimeOffset SeedCreatedAt = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

    /// <summary>预置 13 个模块定义（固定 ID 1-13），后期动态添加。</summary>
    private static void SeedModuleDefinitions(ModelBuilder mb)
    {
        // HasData 要求所有 required 属性提供值（包括 AuditableEntity 的审计字段）
        var now = SeedCreatedAt;
        var seeds = new (long Id, string Code, string DisplayName, ModuleCategory Category, bool Licensable, bool Mandatory, string Description)[]
        {
            // ID=1: 门户管理（必备，不可授权）
            (1L, QlxModuleCodes.Portal, "门户管理", ModuleCategory.Portal, false, true, "租户注册、许可、身份认证"),
            // ID=2: 基础信息中心（必备）
            (2L, QlxModuleCodes.Bas, "基础信息中心", ModuleCategory.Support, false, true, "组织架构、供应商、客户、人员、商品分类等"),
            // ID=3-12: 业务模块（可授权，按需启停）
            (3L, QlxModuleCodes.Pur, "采购管理", ModuleCategory.Business, true, false, "采购需求/申请/订单/收货/对账"),
            (4L, QlxModuleCodes.Sal, "销售管理", ModuleCategory.Business, true, false, "销售报价/订单/出库/回款"),
            (5L, QlxModuleCodes.Wms, "物流管理", ModuleCategory.Business, true, false, "仓库/收货/发货/调拨/盘点"),
            (6L, QlxModuleCodes.Stk, "商品管理", ModuleCategory.Business, true, false, "商品档案/库存台账/出入库流水"),
            (7L, QlxModuleCodes.Hr, "人员管理", ModuleCategory.Business, true, false, "入职/合同/考勤/薪资/异动"),
            (8L, QlxModuleCodes.Fin, "财务管理", ModuleCategory.Business, true, false, "凭证/报销/资金/往来/成本/预算"),
            (9L, QlxModuleCodes.Inv, "票据管理", ModuleCategory.Business, true, false, "进项/销项/费用/劳务发票核验与匹配"),
            (10L, QlxModuleCodes.Ctr, "合同管理", ModuleCategory.Business, true, false, "采购/销售/劳务/设备租赁合同档案"),
            (11L, QlxModuleCodes.Prj, "项目管理", ModuleCategory.Business, true, false, "项目档案/预算/产值/成本归集"),
            (12L, QlxModuleCodes.Eqp, "设备与租赁管理", ModuleCategory.Business, true, false, "设备资产台账/租赁全流程/折旧/收租付租"),
            // ID=13: 支撑服务中心（必备：文件存储中心等支撑能力）
            (13L, QlxModuleCodes.Support, "支撑服务中心", ModuleCategory.Support, false, true, "文件存储中心（上传/下载/回收站/ACL）等支撑能力"),
        };

        foreach (var (Id, Code, DisplayName, Category, Licensable, Mandatory, Description) in seeds)
        {
            mb.Entity<ModuleDefinition>().HasData(new
            {
                Id = Id,
                ModuleCode = Code,
                DisplayName,
                Category,
                CurrentVersion = PortalConstants.ModuleDefinitionVersion,
                TablePrefix = QlxModuleCodes.TablePrefix(Code),
                AssemblyName = QlxModuleCodes.AssemblyName(Code),
                Licensable,
                IsMandatory = Mandatory,
                Description,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = QlxAuditConstants.SystemOperator,
                UpdatedBy = QlxAuditConstants.SystemOperator,
            });
        }
    }

    /// <summary>预置 Portal 核心权限点占位（固定 ID 1-8）。</summary>
    private static void SeedPermissionDefinitions(ModelBuilder mb)
    {
        mb.Entity<PermissionDefinition>().HasData(
            new { Id = 1L, PermissionCode = "portal.tenant.menu", ModuleCode = QlxModuleCodes.Portal, Kind = PermissionKind.Menu, DisplayName = "租户管理", GroupName = PortalConstants.Labels.PortalAdminGroup, SortOrder = 1, Description = "租户注册/启停/注销菜单入口", IsDeprecated = false },
            new { Id = 2L, PermissionCode = "portal.tenant.action", ModuleCode = QlxModuleCodes.Portal, Kind = PermissionKind.Action, DisplayName = "租户管理操作", GroupName = PortalConstants.Labels.PortalAdminGroup, SortOrder = 2, Description = "租户 CRUD 操作权限", IsDeprecated = false },
            new { Id = 3L, PermissionCode = "portal.license.action", ModuleCode = QlxModuleCodes.Portal, Kind = PermissionKind.Action, DisplayName = "模块许可管理", GroupName = PortalConstants.Labels.PortalAdminGroup, SortOrder = 3, Description = "模块启停/套餐配置", IsDeprecated = false },
            new { Id = 4L, PermissionCode = "portal.module.data", ModuleCode = QlxModuleCodes.Portal, Kind = PermissionKind.Data, DisplayName = "模块定义查看", GroupName = PortalConstants.Labels.PortalAdminGroup, SortOrder = 4, Description = "模块定义数据范围", IsDeprecated = false },
            new { Id = 5L, PermissionCode = "portal.permission.data", ModuleCode = QlxModuleCodes.Portal, Kind = PermissionKind.Data, DisplayName = "权限点查看", GroupName = PortalConstants.Labels.PortalAdminGroup, SortOrder = 5, Description = "权限点数据范围", IsDeprecated = false },
            new { Id = 6L, PermissionCode = "portal.role.menu", ModuleCode = QlxModuleCodes.Portal, Kind = PermissionKind.Menu, DisplayName = "角色管理", GroupName = PortalConstants.Labels.IdentityGroup, SortOrder = 6, Description = "", IsDeprecated = false },
            new { Id = 7L, PermissionCode = "portal.role.action", ModuleCode = QlxModuleCodes.Portal, Kind = PermissionKind.Action, DisplayName = "角色分配", GroupName = PortalConstants.Labels.IdentityGroup, SortOrder = 7, Description = "将用户加入角色并授权", IsDeprecated = false },
            new { Id = 8L, PermissionCode = "portal.user.action", ModuleCode = QlxModuleCodes.Portal, Kind = PermissionKind.Action, DisplayName = "用户管理", GroupName = PortalConstants.Labels.IdentityGroup, SortOrder = 8, Description = "用户 CRUD 操作", IsDeprecated = false }
        );
    }
}
