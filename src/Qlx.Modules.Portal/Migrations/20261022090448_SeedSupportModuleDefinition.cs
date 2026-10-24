using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Portal.Migrations
{
    /// <inheritdoc />
    public partial class SeedSupportModuleDefinition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "por_module_definitions",
                columns: new[] { "Id", "AssemblyName", "Category", "CreatedAt", "CreatedBy", "CurrentVersion", "Description", "DisplayName", "IsMandatory", "Licensable", "ModuleCode", "TablePrefix", "UpdatedAt", "UpdatedBy" },
                values: new object[] { 13L, "Qlx.Modules.Support", 1, new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "1.0.0", "文件存储中心（上传/下载/回收站/ACL）等支撑能力", "支撑服务中心", true, false, "support", "fil_", new DateTimeOffset(new DateTime(2026, 10, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "por_module_definitions",
                keyColumn: "Id",
                keyValue: 13L);
        }
    }
}
