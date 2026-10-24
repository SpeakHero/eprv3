using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.BuildingBlocks.Audit.Migrations
{
    /// <inheritdoc />
    public partial class RenameAuditTablesToPlogPrefix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_operation_logs",
                table: "operation_logs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_audit_logs",
                table: "audit_logs");

            migrationBuilder.RenameTable(
                name: "operation_logs",
                newName: "plog_operation");

            migrationBuilder.RenameTable(
                name: "audit_logs",
                newName: "plog_audit");

            migrationBuilder.RenameIndex(
                name: "IX_operation_logs_tenant_id_module_code_operated_at",
                table: "plog_operation",
                newName: "IX_plog_operation_tenant_id_module_code_operated_at");

            migrationBuilder.RenameIndex(
                name: "IX_operation_logs_operated_by_operated_at",
                table: "plog_operation",
                newName: "IX_plog_operation_operated_by_operated_at");

            migrationBuilder.RenameIndex(
                name: "IX_audit_logs_tenant_id_entity_type_entity_id",
                table: "plog_audit",
                newName: "IX_plog_audit_tenant_id_entity_type_entity_id");

            migrationBuilder.RenameIndex(
                name: "IX_audit_logs_tenant_id_changed_at",
                table: "plog_audit",
                newName: "IX_plog_audit_tenant_id_changed_at");

            migrationBuilder.AddPrimaryKey(
                name: "PK_plog_operation",
                table: "plog_operation",
                column: "event_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_plog_audit",
                table: "plog_audit",
                column: "event_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_plog_operation",
                table: "plog_operation");

            migrationBuilder.DropPrimaryKey(
                name: "PK_plog_audit",
                table: "plog_audit");

            migrationBuilder.RenameTable(
                name: "plog_operation",
                newName: "operation_logs");

            migrationBuilder.RenameTable(
                name: "plog_audit",
                newName: "audit_logs");

            migrationBuilder.RenameIndex(
                name: "IX_plog_operation_tenant_id_module_code_operated_at",
                table: "operation_logs",
                newName: "IX_operation_logs_tenant_id_module_code_operated_at");

            migrationBuilder.RenameIndex(
                name: "IX_plog_operation_operated_by_operated_at",
                table: "operation_logs",
                newName: "IX_operation_logs_operated_by_operated_at");

            migrationBuilder.RenameIndex(
                name: "IX_plog_audit_tenant_id_entity_type_entity_id",
                table: "audit_logs",
                newName: "IX_audit_logs_tenant_id_entity_type_entity_id");

            migrationBuilder.RenameIndex(
                name: "IX_plog_audit_tenant_id_changed_at",
                table: "audit_logs",
                newName: "IX_audit_logs_tenant_id_changed_at");

            migrationBuilder.AddPrimaryKey(
                name: "PK_operation_logs",
                table: "operation_logs",
                column: "event_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_audit_logs",
                table: "audit_logs",
                column: "event_id");
        }
    }
}
