using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Wms.Migrations
{
    /// <inheritdoc />
    public partial class WmsOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wms_approval_requests",
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
                    table.PrimaryKey("PK_wms_approval_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wms_in_transit_stocks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    transfer_id = table.Column<long>(type: "bigint", nullable: false),
                    transfer_line_id = table.Column<long>(type: "bigint", nullable: false),
                    transfer_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    from_warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    to_warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_no = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    unit_cost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    shipped_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expect_arrive = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_in_transit_stocks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wms_inventory_adjust_docs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    adjust_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    take_id = table.Column<long>(type: "bigint", nullable: true),
                    type = table.Column<int>(type: "int", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    total_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    remark = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_inventory_adjust_docs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wms_inventory_alerts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    alert_type = table.Column<int>(type: "int", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_no = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    threshold = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    actual_value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    alert_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    handled_by = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    handled_note = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    handled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_inventory_alerts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wms_stock_command_outbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    state = table.Column<int>(type: "int", nullable: false),
                    retry_count = table.Column<int>(type: "int", nullable: false),
                    next_retry_at = table.Column<long>(type: "bigint", nullable: true),
                    last_error = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    dispatched_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_stock_command_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wms_stock_in_tasks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    task_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_type = table.Column<int>(type: "int", nullable: false),
                    source_doc_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    remark = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_stock_in_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wms_stock_out_tasks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    task_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_type = table.Column<int>(type: "int", nullable: false),
                    source_doc_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    picker_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    checker_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    handover_to = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    total_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    remark = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_stock_out_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wms_stock_take_plans",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    plan_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    plan_date = table.Column<DateOnly>(type: "date", nullable: false),
                    scope_type = table.Column<int>(type: "int", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    category_id = table.Column<long>(type: "bigint", nullable: true),
                    location_id = table.Column<long>(type: "bigint", nullable: true),
                    is_blind = table.Column<bool>(type: "bit", nullable: false),
                    remark = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    take_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_stock_take_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wms_stock_takes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    take_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    round = table.Column<int>(type: "int", nullable: false),
                    is_blind = table.Column<bool>(type: "bit", nullable: false),
                    source_take_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    remark = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_stock_takes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wms_stock_transfers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    transfer_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    from_warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    to_warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    from_org_id = table.Column<long>(type: "bigint", nullable: true),
                    to_org_id = table.Column<long>(type: "bigint", nullable: true),
                    in_transit_days = table.Column<int>(type: "int", nullable: false),
                    remark = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_stock_transfers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wms_inventory_adjust_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    doc_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_no = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    unit_cost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    remark = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_inventory_adjust_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_wms_inventory_adjust_lines_wms_inventory_adjust_docs_doc_id",
                        column: x => x.doc_id,
                        principalTable: "wms_inventory_adjust_docs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wms_stock_in_task_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    task_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    expect_qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    count_qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    confirm_qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: true),
                    batch_no = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    unit_cost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_stock_in_task_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_wms_stock_in_task_lines_wms_stock_in_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "wms_stock_in_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wms_stock_out_task_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    task_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    apply_qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    pick_qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    check_qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: true),
                    batch_no = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_stock_out_task_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_wms_stock_out_task_lines_wms_stock_out_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "wms_stock_out_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wms_stock_take_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    take_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_no = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    book_qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    actual_qty = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    diff_qty = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    diff_reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    frozen = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_stock_take_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_wms_stock_take_lines_wms_stock_takes_take_id",
                        column: x => x.take_id,
                        principalTable: "wms_stock_takes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wms_stock_transfer_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    transfer_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "int", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ship_qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    receive_qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    batch_no = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    unit_cost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    diff_note = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_stock_transfer_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_wms_stock_transfer_lines_wms_stock_transfers_transfer_id",
                        column: x => x.transfer_id,
                        principalTable: "wms_stock_transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wms_approval_requests_tenant_id_approval_instance_id",
                table: "wms_approval_requests",
                columns: new[] { "tenant_id", "approval_instance_id" },
                unique: true,
                filter: "approval_instance_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_wms_approval_requests_tenant_id_biz_type",
                table: "wms_approval_requests",
                columns: new[] { "tenant_id", "biz_type" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_in_transit_stocks_tenant_id_item_id",
                table: "wms_in_transit_stocks",
                columns: new[] { "tenant_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_in_transit_stocks_tenant_id_status",
                table: "wms_in_transit_stocks",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_in_transit_stocks_tenant_id_transfer_line_id",
                table: "wms_in_transit_stocks",
                columns: new[] { "tenant_id", "transfer_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wms_inventory_adjust_docs_tenant_id_adjust_no",
                table: "wms_inventory_adjust_docs",
                columns: new[] { "tenant_id", "adjust_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wms_inventory_adjust_docs_tenant_id_status",
                table: "wms_inventory_adjust_docs",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_inventory_adjust_docs_tenant_id_take_id",
                table: "wms_inventory_adjust_docs",
                columns: new[] { "tenant_id", "take_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_inventory_adjust_docs_tenant_id_warehouse_id",
                table: "wms_inventory_adjust_docs",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_inventory_adjust_lines_doc_id",
                table: "wms_inventory_adjust_lines",
                column: "doc_id");

            migrationBuilder.CreateIndex(
                name: "IX_wms_inventory_adjust_lines_tenant_id_doc_id_line_no",
                table: "wms_inventory_adjust_lines",
                columns: new[] { "tenant_id", "doc_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wms_inventory_adjust_lines_tenant_id_item_id",
                table: "wms_inventory_adjust_lines",
                columns: new[] { "tenant_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_inventory_alerts_tenant_id_alert_date_status",
                table: "wms_inventory_alerts",
                columns: new[] { "tenant_id", "alert_date", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_inventory_alerts_tenant_id_alert_type_item_id_warehouse_id_batch_no_alert_date",
                table: "wms_inventory_alerts",
                columns: new[] { "tenant_id", "alert_type", "item_id", "warehouse_id", "batch_no", "alert_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wms_inventory_alerts_tenant_id_warehouse_id",
                table: "wms_inventory_alerts",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_command_outbox_tenant_id_idempotency_key",
                table: "wms_stock_command_outbox",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_command_outbox_tenant_id_state_next_retry_at",
                table: "wms_stock_command_outbox",
                columns: new[] { "tenant_id", "state", "next_retry_at" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_in_task_lines_task_id",
                table: "wms_stock_in_task_lines",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_in_task_lines_tenant_id_item_id",
                table: "wms_stock_in_task_lines",
                columns: new[] { "tenant_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_in_task_lines_tenant_id_task_id_line_no",
                table: "wms_stock_in_task_lines",
                columns: new[] { "tenant_id", "task_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_in_tasks_tenant_id_status",
                table: "wms_stock_in_tasks",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_in_tasks_tenant_id_task_no",
                table: "wms_stock_in_tasks",
                columns: new[] { "tenant_id", "task_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_in_tasks_tenant_id_warehouse_id",
                table: "wms_stock_in_tasks",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_out_task_lines_task_id",
                table: "wms_stock_out_task_lines",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_out_task_lines_tenant_id_item_id",
                table: "wms_stock_out_task_lines",
                columns: new[] { "tenant_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_out_task_lines_tenant_id_task_id_line_no",
                table: "wms_stock_out_task_lines",
                columns: new[] { "tenant_id", "task_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_out_tasks_tenant_id_status",
                table: "wms_stock_out_tasks",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_out_tasks_tenant_id_task_no",
                table: "wms_stock_out_tasks",
                columns: new[] { "tenant_id", "task_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_out_tasks_tenant_id_warehouse_id",
                table: "wms_stock_out_tasks",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_take_lines_take_id",
                table: "wms_stock_take_lines",
                column: "take_id");

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_take_lines_tenant_id_item_id",
                table: "wms_stock_take_lines",
                columns: new[] { "tenant_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_take_lines_tenant_id_take_id_line_no",
                table: "wms_stock_take_lines",
                columns: new[] { "tenant_id", "take_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_take_plans_tenant_id_plan_no",
                table: "wms_stock_take_plans",
                columns: new[] { "tenant_id", "plan_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_take_plans_tenant_id_warehouse_id",
                table: "wms_stock_take_plans",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_takes_tenant_id_plan_id",
                table: "wms_stock_takes",
                columns: new[] { "tenant_id", "plan_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_takes_tenant_id_status",
                table: "wms_stock_takes",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_takes_tenant_id_take_no",
                table: "wms_stock_takes",
                columns: new[] { "tenant_id", "take_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_takes_tenant_id_warehouse_id",
                table: "wms_stock_takes",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_transfer_lines_tenant_id_item_id",
                table: "wms_stock_transfer_lines",
                columns: new[] { "tenant_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_transfer_lines_tenant_id_transfer_id_line_no",
                table: "wms_stock_transfer_lines",
                columns: new[] { "tenant_id", "transfer_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_transfer_lines_transfer_id",
                table: "wms_stock_transfer_lines",
                column: "transfer_id");

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_transfers_tenant_id_from_warehouse_id",
                table: "wms_stock_transfers",
                columns: new[] { "tenant_id", "from_warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_transfers_tenant_id_status",
                table: "wms_stock_transfers",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_transfers_tenant_id_to_warehouse_id",
                table: "wms_stock_transfers",
                columns: new[] { "tenant_id", "to_warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "IX_wms_stock_transfers_tenant_id_transfer_no",
                table: "wms_stock_transfers",
                columns: new[] { "tenant_id", "transfer_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wms_approval_requests");

            migrationBuilder.DropTable(
                name: "wms_in_transit_stocks");

            migrationBuilder.DropTable(
                name: "wms_inventory_adjust_lines");

            migrationBuilder.DropTable(
                name: "wms_inventory_alerts");

            migrationBuilder.DropTable(
                name: "wms_stock_command_outbox");

            migrationBuilder.DropTable(
                name: "wms_stock_in_task_lines");

            migrationBuilder.DropTable(
                name: "wms_stock_out_task_lines");

            migrationBuilder.DropTable(
                name: "wms_stock_take_lines");

            migrationBuilder.DropTable(
                name: "wms_stock_take_plans");

            migrationBuilder.DropTable(
                name: "wms_stock_transfer_lines");

            migrationBuilder.DropTable(
                name: "wms_inventory_adjust_docs");

            migrationBuilder.DropTable(
                name: "wms_stock_in_tasks");

            migrationBuilder.DropTable(
                name: "wms_stock_out_tasks");

            migrationBuilder.DropTable(
                name: "wms_stock_takes");

            migrationBuilder.DropTable(
                name: "wms_stock_transfers");
        }
    }
}
