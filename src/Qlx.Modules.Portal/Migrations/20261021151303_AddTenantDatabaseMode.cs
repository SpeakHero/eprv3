using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Portal.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantDatabaseMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 存量租户全部按“独立库（Isolated = 2）”回填，保证升级后行为与升级前一致；
            // 新建租户由业务层（预配默认模式）决定写入 Shared 还是 Isolated。
            migrationBuilder.AddColumn<int>(
                name: "DatabaseMode",
                table: "por_tenants",
                type: "int",
                nullable: false,
                defaultValue: 2,
                comment: "租户数据库模式：1=Shared（租户共享库），2=Isolated（独立库）");

            migrationBuilder.AddColumn<int>(
                name: "DatabaseMode",
                table: "por_tenant_databases",
                type: "int",
                nullable: false,
                defaultValue: 2,
                comment: "数据库模式：1=Shared（租户共享库，凭据为空），2=Isolated（独立库）");

            migrationBuilder.AddColumn<string>(
                name: "PoolCode",
                table: "por_tenant_databases",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "default",
                comment: "连接池/实例编码，独立库默认 default");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DatabaseMode",
                table: "por_tenants");

            migrationBuilder.DropColumn(
                name: "DatabaseMode",
                table: "por_tenant_databases");

            migrationBuilder.DropColumn(
                name: "PoolCode",
                table: "por_tenant_databases");
        }
    }
}
