using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddTier2Snapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConditionalAccessSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalPolicies = table.Column<int>(type: "int", nullable: false),
                    EnabledPolicies = table.Column<int>(type: "int", nullable: false),
                    ReportOnlyPolicies = table.Column<int>(type: "int", nullable: false),
                    DisabledPolicies = table.Column<int>(type: "int", nullable: false),
                    BlocksLegacyAuth = table.Column<bool>(type: "bit", nullable: false),
                    RequiresMfa = table.Column<bool>(type: "bit", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConditionalAccessSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceComplianceSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalDevices = table.Column<int>(type: "int", nullable: false),
                    CompliantCount = table.Column<int>(type: "int", nullable: false),
                    NonCompliantCount = table.Column<int>(type: "int", nullable: false),
                    InGracePeriodCount = table.Column<int>(type: "int", nullable: false),
                    ErrorCount = table.Column<int>(type: "int", nullable: false),
                    UnknownCount = table.Column<int>(type: "int", nullable: false),
                    WindowsCount = table.Column<int>(type: "int", nullable: false),
                    IosCount = table.Column<int>(type: "int", nullable: false),
                    AndroidCount = table.Column<int>(type: "int", nullable: false),
                    MacOsCount = table.Column<int>(type: "int", nullable: false),
                    OtherOsCount = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceComplianceSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GuestUserSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalGuests = table.Column<int>(type: "int", nullable: false),
                    AcceptedGuests = table.Column<int>(type: "int", nullable: false),
                    PendingAcceptanceGuests = table.Column<int>(type: "int", nullable: false),
                    RecentlyAddedGuests = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuestUserSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiskyUserSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalAtRisk = table.Column<int>(type: "int", nullable: false),
                    HighRisk = table.Column<int>(type: "int", nullable: false),
                    MediumRisk = table.Column<int>(type: "int", nullable: false),
                    LowRisk = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskyUserSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConditionalAccessSnapshots_TenantId_ReportDate",
                table: "ConditionalAccessSnapshots",
                columns: new[] { "TenantId", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceComplianceSnapshots_TenantId_ReportDate",
                table: "DeviceComplianceSnapshots",
                columns: new[] { "TenantId", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuestUserSnapshots_TenantId_ReportDate",
                table: "GuestUserSnapshots",
                columns: new[] { "TenantId", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskyUserSnapshots_TenantId_ReportDate",
                table: "RiskyUserSnapshots",
                columns: new[] { "TenantId", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConditionalAccessSnapshots");

            migrationBuilder.DropTable(
                name: "DeviceComplianceSnapshots");

            migrationBuilder.DropTable(
                name: "GuestUserSnapshots");

            migrationBuilder.DropTable(
                name: "RiskyUserSnapshots");
        }
    }
}
