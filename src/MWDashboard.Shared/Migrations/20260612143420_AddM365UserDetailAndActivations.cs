using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddM365UserDetailAndActivations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "M365AppUserDetailSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LastActivityDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastActivationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OutlookWindows = table.Column<bool>(type: "bit", nullable: false),
                    WordWindows = table.Column<bool>(type: "bit", nullable: false),
                    ExcelWindows = table.Column<bool>(type: "bit", nullable: false),
                    PowerPointWindows = table.Column<bool>(type: "bit", nullable: false),
                    OneNoteWindows = table.Column<bool>(type: "bit", nullable: false),
                    TeamsWindows = table.Column<bool>(type: "bit", nullable: false),
                    OutlookMac = table.Column<bool>(type: "bit", nullable: false),
                    WordMac = table.Column<bool>(type: "bit", nullable: false),
                    ExcelMac = table.Column<bool>(type: "bit", nullable: false),
                    PowerPointMac = table.Column<bool>(type: "bit", nullable: false),
                    OneNoteMac = table.Column<bool>(type: "bit", nullable: false),
                    TeamsMac = table.Column<bool>(type: "bit", nullable: false),
                    OutlookMobile = table.Column<bool>(type: "bit", nullable: false),
                    WordMobile = table.Column<bool>(type: "bit", nullable: false),
                    ExcelMobile = table.Column<bool>(type: "bit", nullable: false),
                    PowerPointMobile = table.Column<bool>(type: "bit", nullable: false),
                    OneNoteMobile = table.Column<bool>(type: "bit", nullable: false),
                    TeamsMobile = table.Column<bool>(type: "bit", nullable: false),
                    OutlookWeb = table.Column<bool>(type: "bit", nullable: false),
                    WordWeb = table.Column<bool>(type: "bit", nullable: false),
                    ExcelWeb = table.Column<bool>(type: "bit", nullable: false),
                    PowerPointWeb = table.Column<bool>(type: "bit", nullable: false),
                    OneNoteWeb = table.Column<bool>(type: "bit", nullable: false),
                    TeamsWeb = table.Column<bool>(type: "bit", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_M365AppUserDetailSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Office365ActivationSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProductType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WindowsCount = table.Column<int>(type: "int", nullable: false),
                    MacCount = table.Column<int>(type: "int", nullable: false),
                    AndroidCount = table.Column<int>(type: "int", nullable: false),
                    IosCount = table.Column<int>(type: "int", nullable: false),
                    WindowsMobileCount = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Office365ActivationSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Office365ActivationUserSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProductType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastActivatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Windows = table.Column<bool>(type: "bit", nullable: false),
                    Mac = table.Column<bool>(type: "bit", nullable: false),
                    WindowsMobile = table.Column<bool>(type: "bit", nullable: false),
                    Ios = table.Column<bool>(type: "bit", nullable: false),
                    Android = table.Column<bool>(type: "bit", nullable: false),
                    SharedComputer = table.Column<bool>(type: "bit", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Office365ActivationUserSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_M365AppUserDetailSnapshots_TenantId_UserKey_ReportDate",
                table: "M365AppUserDetailSnapshots",
                columns: new[] { "TenantId", "UserKey", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Office365ActivationSnapshots_TenantId_ProductType_ReportDate",
                table: "Office365ActivationSnapshots",
                columns: new[] { "TenantId", "ProductType", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Office365ActivationUserSnapshots_TenantId_UserKey_ProductType_ReportDate",
                table: "Office365ActivationUserSnapshots",
                columns: new[] { "TenantId", "UserKey", "ProductType", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "M365AppUserDetailSnapshots");

            migrationBuilder.DropTable(
                name: "Office365ActivationSnapshots");

            migrationBuilder.DropTable(
                name: "Office365ActivationUserSnapshots");
        }
    }
}
