using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageAndConsumption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsumptionSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StorageUsedBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageAllocatedBytes = table.Column<long>(type: "bigint", nullable: false),
                    TotalActivityCount = table.Column<long>(type: "bigint", nullable: false),
                    ActiveUserCount = table.Column<int>(type: "int", nullable: false),
                    LicensedUserCount = table.Column<int>(type: "int", nullable: false),
                    AvgWorkloadsPerUser = table.Column<double>(type: "float", nullable: false),
                    ConsumptionScore = table.Column<double>(type: "float", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsumptionSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StorageSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ServiceName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UsedBytes = table.Column<long>(type: "bigint", nullable: false),
                    AllocatedBytes = table.Column<long>(type: "bigint", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsumptionSnapshots_TenantId_ReportDate",
                table: "ConsumptionSnapshots",
                columns: new[] { "TenantId", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorageSnapshots_TenantId_ServiceName_ReportDate",
                table: "StorageSnapshots",
                columns: new[] { "TenantId", "ServiceName", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsumptionSnapshots");

            migrationBuilder.DropTable(
                name: "StorageSnapshots");
        }
    }
}
