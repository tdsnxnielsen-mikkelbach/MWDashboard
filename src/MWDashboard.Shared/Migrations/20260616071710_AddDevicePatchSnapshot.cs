using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddDevicePatchSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DevicePatchSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OsPlatform = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OsVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DeviceCount = table.Column<int>(type: "int", nullable: false),
                    StaleCount = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevicePatchSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DevicePatchSnapshots_TenantId_OsPlatform_OsVersion_ReportDate",
                table: "DevicePatchSnapshots",
                columns: new[] { "TenantId", "OsPlatform", "OsVersion", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DevicePatchSnapshots");
        }
    }
}
