using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Stk.Migrations
{
    /// <inheritdoc />
    public partial class InventoryLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stk_stock_balances",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    qty_onhand = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    avg_cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    qty_locked = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    last_txn_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_stock_balances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stk_stock_batches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    produce_date = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    expiry_date = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    qty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    avg_cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_stock_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stk_stock_txns",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    txn_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_type = table.Column<int>(type: "int", nullable: false),
                    source_doc_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_line_no = table.Column<int>(type: "int", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    direction = table.Column<int>(type: "int", nullable: false),
                    qty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    balance_qty_after = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    balance_amount_after = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    operator_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_stock_txns", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stk_stock_balances_tenant_id_item_id_warehouse_id",
                table: "stk_stock_balances",
                columns: new[] { "tenant_id", "item_id", "warehouse_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stk_stock_batches_tenant_id_item_id_warehouse_id_batch_no",
                table: "stk_stock_batches",
                columns: new[] { "tenant_id", "item_id", "warehouse_id", "batch_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stk_stock_txns_tenant_id_source_type_source_doc_no_source_line_no_item_id_warehouse_id_batch_no_direction",
                table: "stk_stock_txns",
                columns: new[] { "tenant_id", "source_type", "source_doc_no", "source_line_no", "item_id", "warehouse_id", "batch_no", "direction" },
                unique: true);

            // 流水不可变（A-7/spec §6-8）：数据库侧兜底禁止 UPDATE/DELETE，红字冲正只能新增反向流水。
            // 注意 CREATE TRIGGER 必须是批首语句，故拆为两条 Sql 操作（EF 逐条按批执行）。
            migrationBuilder.Sql(
                "IF OBJECT_ID('trg_stk_stock_txns_no_update_delete','TR') IS NOT NULL DROP TRIGGER trg_stk_stock_txns_no_update_delete;");

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_stk_stock_txns_no_update_delete ON stk_stock_txns
                AFTER UPDATE, DELETE AS
                BEGIN
                    RAISERROR(N'库存流水只允许新增，禁止更新或删除', 16, 1);
                    ROLLBACK TRANSACTION;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "IF OBJECT_ID('trg_stk_stock_txns_no_update_delete','TR') IS NOT NULL DROP TRIGGER trg_stk_stock_txns_no_update_delete;");

            migrationBuilder.DropTable(
                name: "stk_stock_balances");

            migrationBuilder.DropTable(
                name: "stk_stock_batches");

            migrationBuilder.DropTable(
                name: "stk_stock_txns");
        }
    }
}
