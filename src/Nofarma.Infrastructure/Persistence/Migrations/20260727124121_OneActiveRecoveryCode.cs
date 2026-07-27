using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nofarma.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneActiveRecoveryCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecoveryCodes_UserId_UsedAtUtc",
                table: "RecoveryCodes");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryCodes_OneActivePerUser",
                table: "RecoveryCodes",
                column: "UserId",
                unique: true,
                filter: "\"UsedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecoveryCodes_OneActivePerUser",
                table: "RecoveryCodes");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryCodes_UserId_UsedAtUtc",
                table: "RecoveryCodes",
                columns: new[] { "UserId", "UsedAtUtc" });
        }
    }
}
