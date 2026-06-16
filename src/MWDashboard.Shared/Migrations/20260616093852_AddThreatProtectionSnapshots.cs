using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddThreatProtectionSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttackSimSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CampaignName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    AttackType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TargetedUsers = table.Column<int>(type: "int", nullable: false),
                    ClickedCount = table.Column<int>(type: "int", nullable: false),
                    ReportedCount = table.Column<int>(type: "int", nullable: false),
                    CompromisedRate = table.Column<double>(type: "float", nullable: false),
                    LaunchDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttackSimSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailThreatSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ThreatType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BlockedCount = table.Column<int>(type: "int", nullable: false),
                    DeliveredCount = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailThreatSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaleDeviceSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OsPlatform = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TotalDevices = table.Column<int>(type: "int", nullable: false),
                    Stale90Plus = table.Column<int>(type: "int", nullable: false),
                    DisabledDevices = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaleDeviceSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttackSimSnapshots_TenantId_CampaignName_ReportDate",
                table: "AttackSimSnapshots",
                columns: new[] { "TenantId", "CampaignName", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailThreatSnapshots_TenantId_ThreatType_ReportDate",
                table: "EmailThreatSnapshots",
                columns: new[] { "TenantId", "ThreatType", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaleDeviceSnapshots_TenantId_OsPlatform_ReportDate",
                table: "StaleDeviceSnapshots",
                columns: new[] { "TenantId", "OsPlatform", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttackSimSnapshots");

            migrationBuilder.DropTable(
                name: "EmailThreatSnapshots");

            migrationBuilder.DropTable(
                name: "StaleDeviceSnapshots");
        }
    }
}
