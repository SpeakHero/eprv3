using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Bas.Migrations
{
    /// <inheritdoc />
    public partial class AddBasTenantTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bas_member_roles",
                columns: table => new
                {
                    tenant_member_id = table.Column<long>(type: "bigint", nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_member_roles", x => new { x.tenant_member_id, x.role_id, x.tenant_id });
                });

            migrationBuilder.CreateTable(
                name: "bas_organizations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    parent_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    type = table.Column<int>(type: "int", nullable: false),
                    manager_member_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_builtin = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_organizations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bas_positions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    org_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    headcount = table.Column<int>(type: "int", nullable: false),
                    current_count = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_positions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bas_role_permissions",
                columns: table => new
                {
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    permission_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_role_permissions", x => new { x.role_id, x.permission_code, x.tenant_id });
                });

            migrationBuilder.CreateTable(
                name: "bas_roles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    is_builtin = table.Column<bool>(type: "bit", nullable: false),
                    default_data_scope = table.Column<int>(type: "int", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_roles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bas_member_roles_role_id",
                table: "bas_member_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_bas_member_roles_tenant_member_id",
                table: "bas_member_roles",
                column: "tenant_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_bas_organizations_parent_id",
                table: "bas_organizations",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_bas_organizations_status",
                table: "bas_organizations",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_bas_positions_tenant_id",
                table: "bas_positions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_bas_positions_tenant_id_code",
                table: "bas_positions",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bas_role_permissions_tenant_id",
                table: "bas_role_permissions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_bas_roles_tenant_id_code",
                table: "bas_roles",
                columns: new[] { "tenant_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bas_member_roles");

            migrationBuilder.DropTable(
                name: "bas_organizations");

            migrationBuilder.DropTable(
                name: "bas_positions");

            migrationBuilder.DropTable(
                name: "bas_role_permissions");

            migrationBuilder.DropTable(
                name: "bas_roles");
        }
    }
}
