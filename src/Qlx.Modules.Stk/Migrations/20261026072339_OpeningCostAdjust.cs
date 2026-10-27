using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Stk.Migrations
{
    /// <inheritdoc />
    public partial class OpeningCostAdjust : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stk_cost_adjust_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    cost_adjust_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    old_avg_cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    new_avg_cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    diff_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_cost_adjust_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stk_cost_adjusts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    adj_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<int>(type: "int", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: true),
                    total_diff_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    remark = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_cost_adjusts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stk_opening_batches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    remark = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_opening_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stk_opening_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    opening_batch_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    qty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_opening_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stk_cost_adjust_lines_tenant_id_cost_adjust_id",
                table: "stk_cost_adjust_lines",
                columns: new[] { "tenant_id", "cost_adjust_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stk_cost_adjusts_tenant_id_adj_no",
                table: "stk_cost_adjusts",
                columns: new[] { "tenant_id", "adj_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stk_opening_batches_tenant_id_batch_no",
                table: "stk_opening_batches",
                columns: new[] { "tenant_id", "batch_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stk_opening_batches_tenant_id_warehouse_id",
                table: "stk_opening_batches",
                columns: new[] { "tenant_id", "warehouse_id" },
                unique: true,
                filter: "status = 3");

            migrationBuilder.CreateIndex(
                name: "IX_stk_opening_items_tenant_id_opening_batch_id",
                table: "stk_opening_items",
                columns: new[] { "tenant_id", "opening_batch_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stk_cost_adjust_lines");

            migrationBuilder.DropTable(
                name: "stk_cost_adjusts");

            migrationBuilder.DropTable(
                name: "stk_opening_batches");

            migrationBuilder.DropTable(
                name: "stk_opening_items");
        }
    }
}
