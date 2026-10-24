using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Portal.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissionAndMenuTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeprecated",
                table: "por_permission_definitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "por_module_menus",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    ModuleCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MenuCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ParentCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    GroupName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Route = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RequiredPermissionCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsVisible = table.Column<bool>(type: "bit", nullable: false),
                    OpenMode = table.Column<byte>(type: "tinyint", nullable: false),
                    IsDeprecated = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_por_module_menus", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "por_navigation_groups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    GroupCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ModuleCodes = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsDeprecated = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_por_navigation_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "por_role_permissions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    PermissionCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_por_role_permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "por_roles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    RoleCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RoleName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_por_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "por_user_roles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_por_user_roles", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "por_permission_definitions",
                keyColumn: "Id",
                keyValue: 1L,
                column: "IsDeprecated",
                value: false);

            migrationBuilder.UpdateData(
                table: "por_permission_definitions",
                keyColumn: "Id",
                keyValue: 2L,
                column: "IsDeprecated",
                value: false);

            migrationBuilder.UpdateData(
                table: "por_permission_definitions",
                keyColumn: "Id",
                keyValue: 3L,
                column: "IsDeprecated",
                value: false);

            migrationBuilder.UpdateData(
                table: "por_permission_definitions",
                keyColumn: "Id",
                keyValue: 4L,
                column: "IsDeprecated",
                value: false);

            migrationBuilder.UpdateData(
                table: "por_permission_definitions",
                keyColumn: "Id",
                keyValue: 5L,
                column: "IsDeprecated",
                value: false);

            migrationBuilder.UpdateData(
                table: "por_permission_definitions",
                keyColumn: "Id",
                keyValue: 6L,
                column: "IsDeprecated",
                value: false);

            migrationBuilder.UpdateData(
                table: "por_permission_definitions",
                keyColumn: "Id",
                keyValue: 7L,
                column: "IsDeprecated",
                value: false);

            migrationBuilder.UpdateData(
                table: "por_permission_definitions",
                keyColumn: "Id",
                keyValue: 8L,
                column: "IsDeprecated",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_por_module_menus_ModuleCode_MenuCode",
                table: "por_module_menus",
                columns: new[] { "ModuleCode", "MenuCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_por_module_menus_ModuleCode_ParentCode_SortOrder",
                table: "por_module_menus",
                columns: new[] { "ModuleCode", "ParentCode", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_por_module_menus_RequiredPermissionCode",
                table: "por_module_menus",
                column: "RequiredPermissionCode");

            migrationBuilder.CreateIndex(
                name: "IX_por_navigation_groups_GroupCode",
                table: "por_navigation_groups",
                column: "GroupCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_por_role_permissions_RoleId",
                table: "por_role_permissions",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_por_role_permissions_RoleId_PermissionCode",
                table: "por_role_permissions",
                columns: new[] { "RoleId", "PermissionCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_por_roles_RoleCode",
                table: "por_roles",
                column: "RoleCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_por_user_roles_RoleId",
                table: "por_user_roles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_por_user_roles_UserId_RoleId",
                table: "por_user_roles",
                columns: new[] { "UserId", "RoleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "por_module_menus");

            migrationBuilder.DropTable(
                name: "por_navigation_groups");

            migrationBuilder.DropTable(
                name: "por_role_permissions");

            migrationBuilder.DropTable(
                name: "por_roles");

            migrationBuilder.DropTable(
                name: "por_user_roles");

            migrationBuilder.DropColumn(
                name: "IsDeprecated",
                table: "por_permission_definitions");
        }
    }
}
