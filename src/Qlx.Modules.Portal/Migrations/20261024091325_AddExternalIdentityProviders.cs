using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qlx.Modules.Portal.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalIdentityProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "por_external_identity_providers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderType = table.Column<int>(type: "int", nullable: false),
                    Authority = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ClientSecretCipher = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Scopes = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_por_external_identity_providers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_por_external_identity_providers_IsEnabled_SortOrder",
                table: "por_external_identity_providers",
                columns: new[] { "IsEnabled", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_por_external_identity_providers_ProviderCode",
                table: "por_external_identity_providers",
                column: "ProviderCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "por_external_identity_providers");
        }
    }
}
