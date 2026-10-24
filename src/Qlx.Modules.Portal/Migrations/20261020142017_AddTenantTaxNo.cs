using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Portal.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantTaxNo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TaxNo",
                table: "por_tenants",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxNo",
                table: "por_tenants");
        }
    }
}
