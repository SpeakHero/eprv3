using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Bas.Migrations
{
    /// <inheritdoc />
    public partial class Phase3SuppliersAndCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bas_customer_banks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    account_name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    account_no = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    is_default = table.Column<bool>(type: "bit", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_customer_banks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bas_customer_credit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    old_credit_grade = table.Column<int>(type: "int", nullable: false),
                    new_credit_grade = table.Column<int>(type: "int", nullable: false),
                    old_limit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    new_limit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    approved_by = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_customer_credit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bas_customer_invoices",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    tax_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    bank_info = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    addr_phone = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    invoice_type = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_customer_invoices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bas_customers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    uscc = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    customer_type = table.Column<int>(type: "int", nullable: false),
                    contact = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    credit_grade = table.Column<int>(type: "int", nullable: false),
                    credit_limit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    credit_used = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_terms = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bas_supplier_banks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    account_name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    account_no = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    is_default = table.Column<bool>(type: "bit", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_supplier_banks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bas_supplier_certs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    cert_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    cert_no = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    issue_org = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    effect_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    file_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    warned_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_supplier_certs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bas_supplier_grade_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    old_grade = table.Column<int>(type: "int", nullable: false),
                    new_grade = table.Column<int>(type: "int", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    approved_by = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_supplier_grade_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bas_supplier_invoices",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    tax_no = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    bank_info = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    addr_phone = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    invoice_type = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_supplier_invoices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bas_suppliers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    uscc = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    supplier_type = table.Column<int>(type: "int", nullable: false),
                    contact = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    grade = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_suppliers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bas_customer_banks_tenant_id_customer_id",
                table: "bas_customer_banks",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_customer_credit_logs_tenant_id_customer_id",
                table: "bas_customer_credit_logs",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_customer_invoices_tenant_id_customer_id",
                table: "bas_customer_invoices",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_customers_tenant_id_code",
                table: "bas_customers",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bas_customers_tenant_id_credit_grade",
                table: "bas_customers",
                columns: new[] { "tenant_id", "credit_grade" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_customers_tenant_id_status",
                table: "bas_customers",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_customers_tenant_id_uscc",
                table: "bas_customers",
                columns: new[] { "tenant_id", "uscc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bas_supplier_banks_tenant_id_supplier_id",
                table: "bas_supplier_banks",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_supplier_certs_tenant_id_expiry_date",
                table: "bas_supplier_certs",
                columns: new[] { "tenant_id", "expiry_date" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_supplier_certs_tenant_id_supplier_id",
                table: "bas_supplier_certs",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_supplier_grade_logs_tenant_id_supplier_id",
                table: "bas_supplier_grade_logs",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_supplier_invoices_tenant_id_supplier_id",
                table: "bas_supplier_invoices",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_suppliers_tenant_id_code",
                table: "bas_suppliers",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bas_suppliers_tenant_id_grade",
                table: "bas_suppliers",
                columns: new[] { "tenant_id", "grade" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_suppliers_tenant_id_status",
                table: "bas_suppliers",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_suppliers_tenant_id_uscc",
                table: "bas_suppliers",
                columns: new[] { "tenant_id", "uscc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bas_customer_banks");

            migrationBuilder.DropTable(
                name: "bas_customer_credit_logs");

            migrationBuilder.DropTable(
                name: "bas_customer_invoices");

            migrationBuilder.DropTable(
                name: "bas_customers");

            migrationBuilder.DropTable(
                name: "bas_supplier_banks");

            migrationBuilder.DropTable(
                name: "bas_supplier_certs");

            migrationBuilder.DropTable(
                name: "bas_supplier_grade_logs");

            migrationBuilder.DropTable(
                name: "bas_supplier_invoices");

            migrationBuilder.DropTable(
                name: "bas_suppliers");
        }
    }
}
