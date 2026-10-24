using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Portal.Migrations
{
    /// <inheritdoc />
    public partial class AddProvisioningErrorTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastErrorMessage",
                table: "por_tenants",
                type: "nvarchar(max)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastProvisioningAttemptAt",
                table: "por_tenants",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProvisioningAttemptCount",
                table: "por_tenants",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastErrorMessage",
                table: "por_tenants");

            migrationBuilder.DropColumn(
                name: "LastProvisioningAttemptAt",
                table: "por_tenants");

            migrationBuilder.DropColumn(
                name: "ProvisioningAttemptCount",
                table: "por_tenants");
        }
    }
}
