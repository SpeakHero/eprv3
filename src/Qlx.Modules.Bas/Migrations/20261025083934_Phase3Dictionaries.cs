using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Bas.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Dictionaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bas_dict_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    type_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    item_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    item_name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    sort_no = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    is_system = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_dict_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bas_dict_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    type_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    type_name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    owner_level = table.Column<int>(type: "int", nullable: false),
                    org_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    is_system = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_dict_types", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bas_dict_items_tenant_id_type_code_item_code",
                table: "bas_dict_items",
                columns: new[] { "tenant_id", "type_code", "item_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bas_dict_items_tenant_id_type_code_sort_no",
                table: "bas_dict_items",
                columns: new[] { "tenant_id", "type_code", "sort_no" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_dict_types_tenant_id_org_id",
                table: "bas_dict_types",
                columns: new[] { "tenant_id", "org_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_dict_types_tenant_id_owner_level",
                table: "bas_dict_types",
                columns: new[] { "tenant_id", "owner_level" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_dict_types_tenant_id_type_code",
                table: "bas_dict_types",
                columns: new[] { "tenant_id", "type_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bas_dict_items");

            migrationBuilder.DropTable(
                name: "bas_dict_types");
        }
    }
}
