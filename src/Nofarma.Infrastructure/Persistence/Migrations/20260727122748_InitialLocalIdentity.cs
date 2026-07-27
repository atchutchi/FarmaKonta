using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nofarma.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialLocalIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PharmacyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pharmacies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    TaxIdentifier = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    Contact = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pharmacies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Installations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SingletonKey = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PharmacyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrimaryAdministratorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReadyForActivationAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Installations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Installations_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Installations_Pharmacies_PharmacyId",
                        column: x => x.PharmacyId,
                        principalTable: "Pharmacies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LocalUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PharmacyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    LoginName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    NormalizedLoginName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    CredentialKind = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPrimaryAdministrator = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LockedUntilUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastSuccessfulLoginUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocalUsers_Pharmacies_PharmacyId",
                        column: x => x.PharmacyId,
                        principalTable: "Pharmacies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PharmacyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    ObjectType = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    ObjectId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    DiagnosticCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DetailsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEvents_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditEvents_LocalUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "LocalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditEvents_Pharmacies_PharmacyId",
                        column: x => x.PharmacyId,
                        principalTable: "Pharmacies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CredentialRecords",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Algorithm = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    WorkFactor = table.Column<int>(type: "INTEGER", nullable: false),
                    Salt = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Hash = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CredentialRecords", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_CredentialRecords_LocalUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "LocalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LocalSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastActivityAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocalSessions_LocalUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "LocalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Algorithm = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    WorkFactor = table.Column<int>(type: "INTEGER", nullable: false),
                    Salt = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Hash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UsedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryCodes_LocalUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "LocalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_DeviceId",
                table: "AuditEvents",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_PharmacyId_OccurredAtUtc",
                table: "AuditEvents",
                columns: new[] { "PharmacyId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_UserId",
                table: "AuditEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_PharmacyId",
                table: "Devices",
                column: "PharmacyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Installations_DeviceId",
                table: "Installations",
                column: "DeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Installations_PharmacyId",
                table: "Installations",
                column: "PharmacyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Installations_SingletonKey",
                table: "Installations",
                column: "SingletonKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocalSessions_UserId_RevokedAtUtc",
                table: "LocalSessions",
                columns: new[] { "UserId", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LocalUsers_PharmacyId_NormalizedLoginName",
                table: "LocalUsers",
                columns: new[] { "PharmacyId", "NormalizedLoginName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pharmacies_TaxIdentifier",
                table: "Pharmacies",
                column: "TaxIdentifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryCodes_UserId_UsedAtUtc",
                table: "RecoveryCodes",
                columns: new[] { "UserId", "UsedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "CredentialRecords");

            migrationBuilder.DropTable(
                name: "Installations");

            migrationBuilder.DropTable(
                name: "LocalSessions");

            migrationBuilder.DropTable(
                name: "RecoveryCodes");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "LocalUsers");

            migrationBuilder.DropTable(
                name: "Pharmacies");
        }
    }
}
