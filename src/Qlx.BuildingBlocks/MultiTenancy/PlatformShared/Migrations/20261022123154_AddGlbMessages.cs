using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.BuildingBlocks.MultiTenancy.PlatformShared.Migrations
{
    /// <inheritdoc />
    public partial class AddGlbMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "glb_messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    msg_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    priority = table.Column<int>(type: "int", nullable: false),
                    force_accept = table.Column<bool>(type: "bit", nullable: false),
                    revoked = table.Column<bool>(type: "bit", nullable: false),
                    published_by = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    effective_from = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    effective_to = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_glb_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_glb_messages_msg_type_published_at",
                table: "glb_messages",
                columns: new[] { "msg_type", "published_at" });

            migrationBuilder.CreateIndex(
                name: "IX_glb_messages_published_at",
                table: "glb_messages",
                column: "published_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "glb_messages");
        }
    }
}
