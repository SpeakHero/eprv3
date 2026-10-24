using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Portal.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantUpgradeAndAuditTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "por_isolation_audit_records",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    IsHealthy = table.Column<bool>(type: "bit", nullable: false),
                    HasCritical = table.Column<bool>(type: "bit", nullable: false),
                    FindingCount = table.Column<int>(type: "int", nullable: false),
                    FindingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReportText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_por_isolation_audit_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "por_tenant_upgrade_checkpoints",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    JobId = table.Column<long>(type: "bigint", nullable: false),
                    ModuleCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TableName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LastKey = table.Column<long>(type: "bigint", nullable: false),
                    RowsCopied = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_por_tenant_upgrade_checkpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "por_tenant_upgrade_jobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ModuleCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CurrentTable = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RowsCopied = table.Column<long>(type: "bigint", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    OperatorName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_por_tenant_upgrade_jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_por_isolation_audit_records_CreatedAt",
                table: "por_isolation_audit_records",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_por_isolation_audit_records_TenantId",
                table: "por_isolation_audit_records",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_por_tenant_upgrade_checkpoints_JobId_ModuleCode_TableName",
                table: "por_tenant_upgrade_checkpoints",
                columns: new[] { "JobId", "ModuleCode", "TableName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_por_tenant_upgrade_jobs_Status",
                table: "por_tenant_upgrade_jobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_por_tenant_upgrade_jobs_TenantId",
                table: "por_tenant_upgrade_jobs",
                column: "TenantId",
                unique: true,
                filter: "[Status] < 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "por_isolation_audit_records");

            migrationBuilder.DropTable(
                name: "por_tenant_upgrade_checkpoints");

            migrationBuilder.DropTable(
                name: "por_tenant_upgrade_jobs");
        }
    }
}
