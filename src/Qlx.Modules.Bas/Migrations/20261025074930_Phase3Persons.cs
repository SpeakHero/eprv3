using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Bas.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Persons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bas_person_assignments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    person_id = table.Column<long>(type: "bigint", nullable: false),
                    org_id = table.Column<long>(type: "bigint", nullable: true),
                    position_id = table.Column<long>(type: "bigint", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    change_reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    is_current = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_person_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bas_person_certs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    person_id = table.Column<long>(type: "bigint", nullable: false),
                    cert_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    cert_no = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    remark = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_person_certs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bas_persons",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    person_type = table.Column<int>(type: "int", nullable: false),
                    id_type = table.Column<int>(type: "int", nullable: false),
                    id_no_cipher = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    id_no_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    mobile_cipher = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    emergency_contact = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    emergency_phone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    org_id = table.Column<long>(type: "bigint", nullable: true),
                    position_id = table.Column<long>(type: "bigint", nullable: true),
                    employ_status = table.Column<int>(type: "int", nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    leave_date = table.Column<DateOnly>(type: "date", nullable: true),
                    bank_card_no_cipher = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bas_persons", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bas_person_assignments_tenant_id_person_id_is_current",
                table: "bas_person_assignments",
                columns: new[] { "tenant_id", "person_id", "is_current" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_person_certs_tenant_id_person_id",
                table: "bas_person_certs",
                columns: new[] { "tenant_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_persons_tenant_id_code",
                table: "bas_persons",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bas_persons_tenant_id_employ_status",
                table: "bas_persons",
                columns: new[] { "tenant_id", "employ_status" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_persons_tenant_id_id_no_hash",
                table: "bas_persons",
                columns: new[] { "tenant_id", "id_no_hash" },
                unique: true,
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_bas_persons_tenant_id_org_id",
                table: "bas_persons",
                columns: new[] { "tenant_id", "org_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bas_persons_tenant_id_position_id",
                table: "bas_persons",
                columns: new[] { "tenant_id", "position_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bas_person_assignments");

            migrationBuilder.DropTable(
                name: "bas_person_certs");

            migrationBuilder.DropTable(
                name: "bas_persons");
        }
    }
}
