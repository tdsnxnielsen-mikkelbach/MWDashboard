using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddIonPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "IonAccountId",
                table: "Tenants",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PricingOnly",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ResellerName",
                table: "Tenants",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Tenants",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "IonResellers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    PrimaryContactEmail = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    DelegatedAdminEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CustomerCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IonResellers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LicensePricingSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    SubscriptionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OfferName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SkuTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ProductId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SkuId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CatalogItemId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SkuPartNumber = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UnitListPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ListPriceCurrency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IonUnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    IonUnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    IonMsrp = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    IonUnitMargin = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    IonMarginPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    IonLineMargin = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    IonCurrency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ResellerName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    IonAccountId = table.Column<long>(type: "bigint", nullable: true),
                    ResellerMatched = table.Column<bool>(type: "bit", nullable: false),
                    PricingSource = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MatchedOn = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicensePricingSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResellerPricingSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IonAccountId = table.Column<long>(type: "bigint", nullable: false),
                    ResellerName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    IonCustomerId = table.Column<long>(type: "bigint", nullable: false),
                    MfgPartNumber = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SkuName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Msrp = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    LineMargin = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    MarginPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LastOrderId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LastOrderDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResellerPricingSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IonResellers_AccountId",
                table: "IonResellers",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicensePricingSnapshots_TenantId_ReportDate",
                table: "LicensePricingSnapshots",
                columns: new[] { "TenantId", "ReportDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LicensePricingSnapshots_TenantId_SubscriptionId_ReportDate",
                table: "LicensePricingSnapshots",
                columns: new[] { "TenantId", "SubscriptionId", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResellerPricingSnapshots_IonAccountId_IonCustomerId_MfgPartNumber_ReportDate",
                table: "ResellerPricingSnapshots",
                columns: new[] { "IonAccountId", "IonCustomerId", "MfgPartNumber", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IonResellers");

            migrationBuilder.DropTable(
                name: "LicensePricingSnapshots");

            migrationBuilder.DropTable(
                name: "ResellerPricingSnapshots");

            migrationBuilder.DropColumn(
                name: "IonAccountId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PricingOnly",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ResellerName",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Tenants");
        }
    }
}
