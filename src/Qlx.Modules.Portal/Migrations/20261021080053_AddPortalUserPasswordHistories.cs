using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Portal.Migrations
{
    /// <inheritdoc />
    public partial class AddPortalUserPasswordHistories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "por_user_password_histories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    PortalUserId = table.Column<long>(type: "bigint", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_por_user_password_histories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_por_user_password_histories_por_users_PortalUserId",
                        column: x => x.PortalUserId,
                        principalTable: "por_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_por_user_password_histories_PortalUserId",
                table: "por_user_password_histories",
                column: "PortalUserId");

            migrationBuilder.CreateIndex(
                name: "IX_por_user_password_histories_PortalUserId_CreatedAt",
                table: "por_user_password_histories",
                columns: new[] { "PortalUserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "por_user_password_histories");
        }
    }
}
