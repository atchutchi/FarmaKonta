using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nofarma.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignedLicensing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Licenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstallationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LicenseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Plan = table.Column<int>(type: "INTEGER", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    KeyId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DocumentBytes = table.Column<byte[]>(type: "BLOB", nullable: false),
                    DocumentHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    GraceUntilUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Licenses", x => x.Id);
                    table.CheckConstraint("CK_Licenses_DocumentBytes_Length", "length(\"DocumentBytes\") <= 65536");
                    table.CheckConstraint("CK_Licenses_DocumentHash_Length", "length(\"DocumentHash\") = 32");
                    table.CheckConstraint("CK_Licenses_Sequence_Positive", "\"Sequence\" >= 1");
                    table.ForeignKey(
                        name: "FK_Licenses_Installations_InstallationId",
                        column: x => x.InstallationId,
                        principalTable: "Installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_InstallationId",
                table: "Licenses",
                column: "InstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_LicenseId_Sequence",
                table: "Licenses",
                columns: new[] { "LicenseId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Licenses");
        }
    }
}
