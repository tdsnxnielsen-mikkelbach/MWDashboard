using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddTier3Snapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalGroups = table.Column<int>(type: "int", nullable: false),
                    M365Groups = table.Column<int>(type: "int", nullable: false),
                    SecurityGroups = table.Column<int>(type: "int", nullable: false),
                    TeamsConnectedGroups = table.Column<int>(type: "int", nullable: false),
                    OwnerlessGroups = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MailboxUsageSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalMailboxes = table.Column<int>(type: "int", nullable: false),
                    ActiveMailboxes = table.Column<int>(type: "int", nullable: false),
                    InactiveMailboxes = table.Column<int>(type: "int", nullable: false),
                    TotalStorageUsedBytes = table.Column<long>(type: "bigint", nullable: false),
                    UnderLimitCount = table.Column<int>(type: "int", nullable: false),
                    WarningCount = table.Column<int>(type: "int", nullable: false),
                    SendProhibitedCount = table.Column<int>(type: "int", nullable: false),
                    SendReceiveProhibitedCount = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailboxUsageSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteUsageDetailSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Workload = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    StorageUsedBytes = table.Column<long>(type: "bigint", nullable: false),
                    FileCount = table.Column<long>(type: "bigint", nullable: false),
                    ActiveFileCount = table.Column<long>(type: "bigint", nullable: false),
                    LastActivityDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteUsageDetailSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteUsageSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Workload = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TotalSites = table.Column<int>(type: "int", nullable: false),
                    ActiveSites = table.Column<int>(type: "int", nullable: false),
                    TotalStorageUsedBytes = table.Column<long>(type: "bigint", nullable: false),
                    TotalFileCount = table.Column<long>(type: "bigint", nullable: false),
                    ActiveFileCount = table.Column<long>(type: "bigint", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteUsageSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamsDeviceUsageSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WindowsCount = table.Column<int>(type: "int", nullable: false),
                    MacCount = table.Column<int>(type: "int", nullable: false),
                    WebCount = table.Column<int>(type: "int", nullable: false),
                    IosCount = table.Column<int>(type: "int", nullable: false),
                    AndroidPhoneCount = table.Column<int>(type: "int", nullable: false),
                    WindowsPhoneCount = table.Column<int>(type: "int", nullable: false),
                    ChromeOsCount = table.Column<int>(type: "int", nullable: false),
                    LinuxCount = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamsDeviceUsageSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TopMailboxSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    StorageUsedBytes = table.Column<long>(type: "bigint", nullable: false),
                    ItemCount = table.Column<long>(type: "bigint", nullable: false),
                    LastActivityDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopMailboxSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YammerActivitySnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PostedCount = table.Column<int>(type: "int", nullable: false),
                    ReadCount = table.Column<int>(type: "int", nullable: false),
                    LikedCount = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YammerActivitySnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupSnapshots_TenantId_ReportDate",
                table: "GroupSnapshots",
                columns: new[] { "TenantId", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MailboxUsageSnapshots_TenantId_ReportDate",
                table: "MailboxUsageSnapshots",
                columns: new[] { "TenantId", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteUsageDetailSnapshots_TenantId_Workload_ReportDate_Rank",
                table: "SiteUsageDetailSnapshots",
                columns: new[] { "TenantId", "Workload", "ReportDate", "Rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteUsageSnapshots_TenantId_Workload_ReportDate",
                table: "SiteUsageSnapshots",
                columns: new[] { "TenantId", "Workload", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamsDeviceUsageSnapshots_TenantId_ReportDate",
                table: "TeamsDeviceUsageSnapshots",
                columns: new[] { "TenantId", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TopMailboxSnapshots_TenantId_ReportDate_Rank",
                table: "TopMailboxSnapshots",
                columns: new[] { "TenantId", "ReportDate", "Rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YammerActivitySnapshots_TenantId_ReportDate",
                table: "YammerActivitySnapshots",
                columns: new[] { "TenantId", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupSnapshots");

            migrationBuilder.DropTable(
                name: "MailboxUsageSnapshots");

            migrationBuilder.DropTable(
                name: "SiteUsageDetailSnapshots");

            migrationBuilder.DropTable(
                name: "SiteUsageSnapshots");

            migrationBuilder.DropTable(
                name: "TeamsDeviceUsageSnapshots");

            migrationBuilder.DropTable(
                name: "TopMailboxSnapshots");

            migrationBuilder.DropTable(
                name: "YammerActivitySnapshots");
        }
    }
}
