using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Stk.Migrations
{
    /// <inheritdoc />
    public partial class InitialStk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stk_approval_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    approval_instance_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    biz_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    biz_id = table.Column<long>(type: "bigint", nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_approval_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stk_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    item_code = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    spec = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    barcode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    category_id = table.Column<long>(type: "bigint", nullable: false),
                    unit_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_enabled = table.Column<bool>(type: "bit", nullable: false),
                    expiry_enabled = table.Column<bool>(type: "bit", nullable: false),
                    tax_rate = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    ref_cost_price = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    guide_price = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stk_price_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    field = table.Column<int>(type: "int", nullable: false),
                    old_value = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    new_value = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    approved_by = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_price_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stk_approval_requests_tenant_id_approval_instance_id",
                table: "stk_approval_requests",
                columns: new[] { "tenant_id", "approval_instance_id" },
                unique: true,
                filter: "approval_instance_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_stk_approval_requests_tenant_id_biz_type",
                table: "stk_approval_requests",
                columns: new[] { "tenant_id", "biz_type" });

            migrationBuilder.CreateIndex(
                name: "IX_stk_items_tenant_id_barcode",
                table: "stk_items",
                columns: new[] { "tenant_id", "barcode" },
                unique: true,
                filter: "barcode IS NOT NULL AND is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_stk_items_tenant_id_category_id",
                table: "stk_items",
                columns: new[] { "tenant_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stk_items_tenant_id_item_code",
                table: "stk_items",
                columns: new[] { "tenant_id", "item_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stk_items_tenant_id_status",
                table: "stk_items",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_stk_price_logs_tenant_id_item_id",
                table: "stk_price_logs",
                columns: new[] { "tenant_id", "item_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stk_approval_requests");

            migrationBuilder.DropTable(
                name: "stk_items");

            migrationBuilder.DropTable(
                name: "stk_price_logs");
        }
    }
}
