using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // EF Core 自动生成，静态数组常量规则不适用

namespace Qlx.BuildingBlocks.Audit.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    entity_id = table.Column<long>(type: "bigint", nullable: false),
                    operation = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    old_values = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    new_values = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    changed_by = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "operation_logs",
                columns: table => new
                {
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    module_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    operation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    operated_by = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    operated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    target_type = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    target_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_logs", x => x.event_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_tenant_id_changed_at",
                table: "audit_logs",
                columns: new[] { "tenant_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_tenant_id_entity_type_entity_id",
                table: "audit_logs",
                columns: new[] { "tenant_id", "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_operation_logs_tenant_id_module_code_operated_at",
                table: "operation_logs",
                columns: new[] { "tenant_id", "module_code", "operated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "operation_logs");
        }
    }
}
