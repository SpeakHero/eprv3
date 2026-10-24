using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // EF Core 自动生成，静态数组常量规则不适用

namespace Qlx.BuildingBlocks.Audit.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationLogMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "metadata",
                table: "operation_logs",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_operation_logs_operated_by_operated_at",
                table: "operation_logs",
                columns: new[] { "operated_by", "operated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_operation_logs_operated_by_operated_at",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "metadata",
                table: "operation_logs");
        }
    }
}
