using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable CA1861 // EF Core 自动生成，静态数组常量规则不适用

namespace Qlx.Modules.Portal.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "por_module_definitions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    ModuleCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    CurrentVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TablePrefix = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AssemblyName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Licensable = table.Column<bool>(type: "bit", nullable: false),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_por_module_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "por_permission_definitions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    PermissionCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ModuleCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_por_permission_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "por_tenants",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    TenantCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ShortName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Industry = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ContactName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ContactEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PlanCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SubscribedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SubscriptionExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    GracePeriodUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_por_tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "por_tenant_databases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    DbHost = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DbPort = table.Column<int>(type: "int", nullable: false),
                    DbName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EncryptedDbUser = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    EncryptedDbPassword = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    EncryptionSalt = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ConnectTimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    MultipleActiveResultSets = table.Column<bool>(type: "bit", nullable: false),
                    MinPoolSize = table.Column<int>(type: "int", nullable: false),
                    MaxPoolSize = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_por_tenant_databases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_por_tenant_databases_por_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "por_tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "por_tenant_modules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    ModuleCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LicensedVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    LicensedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CustomLicenseData = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    LicensedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_por_tenant_modules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_por_tenant_modules_por_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "por_tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "por_module_definitions",
                columns: new[] { "Id", "AssemblyName", "Category", "CreatedAt", "CreatedBy", "CurrentVersion", "Description", "DisplayName", "IsMandatory", "Licensable", "ModuleCode", "TablePrefix", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { 1L, "Qlx.Modules.Portal", 2, new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "1.0.0", "租户注册、许可、身份认证", "门户管理", true, false, "portal", "por_", new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system" },
                    { 2L, "Qlx.Modules.Bas", 1, new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "1.0.0", "组织架构、供应商、客户、人员、商品分类等", "基础信息中心", true, false, "bas", "bas_", new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system" },
                    { 3L, "Qlx.Modules.Pur", 0, new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "1.0.0", "采购需求/申请/订单/收货/对账", "采购管理", false, true, "pur", "pur_", new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system" },
                    { 4L, "Qlx.Modules.Sal", 0, new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "1.0.0", "销售报价/订单/出库/回款", "销售管理", false, true, "sal", "sal_", new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system" },
                    { 5L, "Qlx.Modules.Wms", 0, new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "1.0.0", "仓库/收货/发货/调拨/盘点", "物流管理", false, true, "wms", "wms_", new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system" },
                    { 6L, "Qlx.Modules.Stk", 0, new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "1.0.0", "商品档案/库存台账/出入库流水", "商品管理", false, true, "stk", "stk_", new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system" },
                    { 7L, "Qlx.Modules.Hr", 0, new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "1.0.0", "入职/合同/考勤/薪资/异动", "人员管理", false, true, "hr", "hr_", new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system" },
                    { 8L, "Qlx.Modules.Fin", 0, new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "1.0.0", "凭证/报销/资金/往来/成本/预算", "财务管理", false, true, "fin", "fin_", new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system" },
                    { 9L, "Qlx.Modules.Inv", 0, new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "1.0.0", "进项/销项/费用/劳务发票核验与匹配", "票据管理", false, true, "inv", "inv_", new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system" },
                    { 10L, "Qlx.Modules.Ctr", 0, new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "1.0.0", "采购/销售/劳务/设备租赁合同档案", "合同管理", false, true, "ctr", "ctr_", new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system" },
                    { 11L, "Qlx.Modules.Prj", 0, new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "1.0.0", "项目档案/预算/产值/成本归集", "项目管理", false, true, "prj", "prj_", new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system" },
                    { 12L, "Qlx.Modules.Eqp", 0, new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "1.0.0", "设备资产台账/租赁全流程/折旧/收租付租", "设备与租赁管理", false, true, "eqp", "eqp_", new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system" }
                });

            migrationBuilder.InsertData(
                table: "por_permission_definitions",
                columns: new[] { "Id", "Description", "DisplayName", "GroupName", "Kind", "ModuleCode", "PermissionCode", "SortOrder" },
                values: new object[,]
                {
                    { 1L, "租户注册/启停/注销菜单入口", "租户管理", "门户管理", 0, "portal", "portal.tenant.menu", 1 },
                    { 2L, "租户 CRUD 操作权限", "租户管理操作", "门户管理", 1, "portal", "portal.tenant.action", 2 },
                    { 3L, "模块启停/套餐配置", "模块许可管理", "门户管理", 1, "portal", "portal.license.action", 3 },
                    { 4L, "模块定义数据范围", "模块定义查看", "门户管理", 2, "portal", "portal.module.data", 4 },
                    { 5L, "权限点数据范围", "权限点查看", "门户管理", 2, "portal", "portal.permission.data", 5 },
                    { 6L, "", "角色管理", "身份权限", 0, "portal", "portal.role.menu", 6 },
                    { 7L, "将用户加入角色并授权", "角色分配", "身份权限", 1, "portal", "portal.role.action", 7 },
                    { 8L, "用户 CRUD 操作", "用户管理", "身份权限", 1, "portal", "portal.user.action", 8 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_por_module_definitions_ModuleCode",
                table: "por_module_definitions",
                column: "ModuleCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_por_permission_definitions_ModuleCode",
                table: "por_permission_definitions",
                column: "ModuleCode");

            migrationBuilder.CreateIndex(
                name: "IX_por_permission_definitions_ModuleCode_Kind",
                table: "por_permission_definitions",
                columns: new[] { "ModuleCode", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_por_permission_definitions_PermissionCode",
                table: "por_permission_definitions",
                column: "PermissionCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_por_tenant_databases_DbName",
                table: "por_tenant_databases",
                column: "DbName");

            migrationBuilder.CreateIndex(
                name: "IX_por_tenant_databases_Status",
                table: "por_tenant_databases",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_por_tenant_databases_TenantId",
                table: "por_tenant_databases",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_por_tenant_modules_ModuleCode",
                table: "por_tenant_modules",
                column: "ModuleCode");

            migrationBuilder.CreateIndex(
                name: "IX_por_tenant_modules_Status",
                table: "por_tenant_modules",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_por_tenant_modules_TenantId_ModuleCode",
                table: "por_tenant_modules",
                columns: new[] { "TenantId", "ModuleCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_por_tenants_Status",
                table: "por_tenants",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_por_tenants_TenantCode",
                table: "por_tenants",
                column: "TenantCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "por_module_definitions");

            migrationBuilder.DropTable(
                name: "por_permission_definitions");

            migrationBuilder.DropTable(
                name: "por_tenant_databases");

            migrationBuilder.DropTable(
                name: "por_tenant_modules");

            migrationBuilder.DropTable(
                name: "por_tenants");
        }
    }
}
