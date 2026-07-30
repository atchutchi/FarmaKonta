using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nofarma.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashShifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Devices_PharmacyId",
                table: "Devices");

            migrationBuilder.CreateTable(
                name: "CashShifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PharmacyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    OpeningCashXof = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpectedCashXof = table.Column<long>(type: "INTEGER", nullable: false),
                    CountedCashXof = table.Column<long>(type: "INTEGER", nullable: true),
                    DifferenceXof = table.Column<long>(type: "INTEGER", nullable: true),
                    OpenedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashShifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashShifts_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashShifts_LocalUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "LocalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashShifts_Pharmacies_PharmacyId",
                        column: x => x.PharmacyId,
                        principalTable: "Pharmacies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutboxEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PharmacyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    AggregateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutboxEvents_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboxEvents_Pharmacies_PharmacyId",
                        column: x => x.PharmacyId,
                        principalTable: "Pharmacies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PharmacyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    OperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CashShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashCommands_CashShifts_CashShiftId",
                        column: x => x.CashShiftId,
                        principalTable: "CashShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashCommands_Pharmacies_PharmacyId",
                        column: x => x.PharmacyId,
                        principalTable: "Pharmacies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PharmacyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CashShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    AmountXof = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceSaleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashMovements_CashShifts_CashShiftId",
                        column: x => x.CashShiftId,
                        principalTable: "CashShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_Pharmacies_PharmacyId",
                        column: x => x.PharmacyId,
                        principalTable: "Pharmacies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_PharmacyId",
                table: "Devices",
                column: "PharmacyId");

            migrationBuilder.CreateIndex(
                name: "IX_CashCommands_CashShiftId",
                table: "CashCommands",
                column: "CashShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_CashCommands_PharmacyId_IdempotencyKey",
                table: "CashCommands",
                columns: new[] { "PharmacyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashShiftId_Sequence",
                table: "CashMovements",
                columns: new[] { "CashShiftId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_PharmacyId_OccurredAtUtc",
                table: "CashMovements",
                columns: new[] { "PharmacyId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_DeviceId",
                table: "CashShifts",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_PharmacyId_DeviceId",
                table: "CashShifts",
                columns: new[] { "PharmacyId", "DeviceId" },
                unique: true,
                filter: "Status = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_PharmacyId_OpenedAtUtc",
                table: "CashShifts",
                columns: new[] { "PharmacyId", "OpenedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_UserId",
                table: "CashShifts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEvents_DeviceId",
                table: "OutboxEvents",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEvents_PharmacyId_OccurredAtUtc",
                table: "OutboxEvents",
                columns: new[] { "PharmacyId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashCommands");

            migrationBuilder.DropTable(
                name: "CashMovements");

            migrationBuilder.DropTable(
                name: "OutboxEvents");

            migrationBuilder.DropTable(
                name: "CashShifts");

            migrationBuilder.DropIndex(
                name: "IX_Devices_PharmacyId",
                table: "Devices");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_PharmacyId",
                table: "Devices",
                column: "PharmacyId",
                unique: true);
        }
    }
}
