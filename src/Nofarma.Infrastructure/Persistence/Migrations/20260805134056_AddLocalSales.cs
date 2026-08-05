using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nofarma.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PharmacyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CashShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DailySequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    GrossSubtotalXof = table.Column<long>(type: "INTEGER", nullable: false),
                    LineDiscountXof = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalDiscountXof = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalDiscountAuthorizedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TotalXof = table.Column<long>(type: "INTEGER", nullable: false),
                    PaidXof = table.Column<long>(type: "INTEGER", nullable: false),
                    ChangeXof = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sales_CashShifts_CashShiftId",
                        column: x => x.CashShiftId,
                        principalTable: "CashShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sales_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sales_LocalUsers_TotalDiscountAuthorizedByUserId",
                        column: x => x.TotalDiscountAuthorizedByUserId,
                        principalTable: "LocalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sales_LocalUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "LocalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sales_Pharmacies_PharmacyId",
                        column: x => x.PharmacyId,
                        principalTable: "Pharmacies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SuspendedSales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PharmacyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    SuspendedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuspendedSales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SuspendedSales_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SuspendedSales_LocalUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "LocalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SuspendedSales_Pharmacies_PharmacyId",
                        column: x => x.PharmacyId,
                        principalTable: "Pharmacies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PharmacyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Receipts_Pharmacies_PharmacyId",
                        column: x => x.PharmacyId,
                        principalTable: "Pharmacies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipts_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PharmacyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleCommands_Pharmacies_PharmacyId",
                        column: x => x.PharmacyId,
                        principalTable: "Pharmacies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleCommands_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PackageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    UnitName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    PackageFactor = table.Column<long>(type: "INTEGER", nullable: false),
                    QuantityPackages = table.Column<long>(type: "INTEGER", nullable: false),
                    QuantityBase = table.Column<long>(type: "INTEGER", nullable: false),
                    UnitPriceXof = table.Column<long>(type: "INTEGER", nullable: false),
                    GrossXof = table.Column<long>(type: "INTEGER", nullable: false),
                    DiscountXof = table.Column<long>(type: "INTEGER", nullable: false),
                    NetXof = table.Column<long>(type: "INTEGER", nullable: false),
                    CapturedCostXof = table.Column<long>(type: "INTEGER", nullable: false),
                    DiscountAuthorizedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleLines_LocalUsers_DiscountAuthorizedByUserId",
                        column: x => x.DiscountAuthorizedByUserId,
                        principalTable: "LocalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleLines_ProductPackages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "ProductPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleLines_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Method = table.Column<int>(type: "INTEGER", nullable: false),
                    AmountXof = table.Column<long>(type: "INTEGER", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalePayments_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SuspendedSaleLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SuspendedSaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PackageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuantityPackages = table.Column<long>(type: "INTEGER", nullable: false),
                    DiscountXof = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuspendedSaleLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SuspendedSaleLines_ProductPackages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "ProductPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SuspendedSaleLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SuspendedSaleLines_SuspendedSales_SuspendedSaleId",
                        column: x => x.SuspendedSaleId,
                        principalTable: "SuspendedSales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleStockAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StockLotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StockMovementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuantityBase = table.Column<long>(type: "INTEGER", nullable: false),
                    OriginUnitCostXof = table.Column<long>(type: "INTEGER", nullable: false),
                    PreviousLotBalance = table.Column<long>(type: "INTEGER", nullable: false),
                    ResultingLotBalance = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleStockAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleStockAllocations_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleStockAllocations_SaleLines_SaleLineId",
                        column: x => x.SaleLineId,
                        principalTable: "SaleLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleStockAllocations_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleStockAllocations_StockLots_StockLotId",
                        column: x => x.StockLotId,
                        principalTable: "StockLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleStockAllocations_StockMovements_StockMovementId",
                        column: x => x.StockMovementId,
                        principalTable: "StockMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_PharmacyId_Number",
                table: "Receipts",
                columns: new[] { "PharmacyId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_SaleId",
                table: "Receipts",
                column: "SaleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleCommands_PharmacyId_IdempotencyKey",
                table: "SaleCommands",
                columns: new[] { "PharmacyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleCommands_SaleId",
                table: "SaleCommands",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_DiscountAuthorizedByUserId",
                table: "SaleLines",
                column: "DiscountAuthorizedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_PackageId",
                table: "SaleLines",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_ProductId",
                table: "SaleLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_SaleId_Sequence",
                table: "SaleLines",
                columns: new[] { "SaleId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalePayments_SaleId_Sequence",
                table: "SalePayments",
                columns: new[] { "SaleId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CashShiftId",
                table: "Sales",
                column: "CashShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_DeviceId",
                table: "Sales",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_PharmacyId_BusinessDate_DailySequence",
                table: "Sales",
                columns: new[] { "PharmacyId", "BusinessDate", "DailySequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_PharmacyId_CompletedAtUtc",
                table: "Sales",
                columns: new[] { "PharmacyId", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_PharmacyId_Number",
                table: "Sales",
                columns: new[] { "PharmacyId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_TotalDiscountAuthorizedByUserId",
                table: "Sales",
                column: "TotalDiscountAuthorizedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_UserId",
                table: "Sales",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleStockAllocations_ProductId",
                table: "SaleStockAllocations",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleStockAllocations_SaleId_SaleLineId_StockLotId",
                table: "SaleStockAllocations",
                columns: new[] { "SaleId", "SaleLineId", "StockLotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleStockAllocations_SaleLineId",
                table: "SaleStockAllocations",
                column: "SaleLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleStockAllocations_StockLotId",
                table: "SaleStockAllocations",
                column: "StockLotId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleStockAllocations_StockMovementId",
                table: "SaleStockAllocations",
                column: "StockMovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedSaleLines_PackageId",
                table: "SuspendedSaleLines",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedSaleLines_ProductId",
                table: "SuspendedSaleLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedSaleLines_SuspendedSaleId_Sequence",
                table: "SuspendedSaleLines",
                columns: new[] { "SuspendedSaleId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedSales_DeviceId",
                table: "SuspendedSales",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedSales_PharmacyId_DeviceId_SuspendedAtUtc",
                table: "SuspendedSales",
                columns: new[] { "PharmacyId", "DeviceId", "SuspendedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedSales_UserId",
                table: "SuspendedSales",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Receipts");

            migrationBuilder.DropTable(
                name: "SaleCommands");

            migrationBuilder.DropTable(
                name: "SalePayments");

            migrationBuilder.DropTable(
                name: "SaleStockAllocations");

            migrationBuilder.DropTable(
                name: "SuspendedSaleLines");

            migrationBuilder.DropTable(
                name: "SaleLines");

            migrationBuilder.DropTable(
                name: "SuspendedSales");

            migrationBuilder.DropTable(
                name: "Sales");
        }
    }
}
