using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityCopilotSegmentDepartment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CopilotUsageSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AppName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActiveUsers = table.Column<int>(type: "int", nullable: false),
                    TotalAssignedLicenses = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopilotUsageSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DepartmentUsageSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Department = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ActiveUsers = table.Column<int>(type: "int", nullable: false),
                    TotalUsers = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentUsageSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSegmentSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HeavyUsers = table.Column<int>(type: "int", nullable: false),
                    LightUsers = table.Column<int>(type: "int", nullable: false),
                    InactiveUsers = table.Column<int>(type: "int", nullable: false),
                    TotalUsers = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSegmentSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkloadActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Workload = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActivityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkloadActivities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CopilotUsageSnapshots_TenantId_AppName_ReportDate",
                table: "CopilotUsageSnapshots",
                columns: new[] { "TenantId", "AppName", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentUsageSnapshots_TenantId_Department_ReportDate",
                table: "DepartmentUsageSnapshots",
                columns: new[] { "TenantId", "Department", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSegmentSnapshots_TenantId_ReportDate",
                table: "UserSegmentSnapshots",
                columns: new[] { "TenantId", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadActivities_TenantId_Workload_ActivityType_ReportDate",
                table: "WorkloadActivities",
                columns: new[] { "TenantId", "Workload", "ActivityType", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CopilotUsageSnapshots");

            migrationBuilder.DropTable(
                name: "DepartmentUsageSnapshots");

            migrationBuilder.DropTable(
                name: "UserSegmentSnapshots");

            migrationBuilder.DropTable(
                name: "WorkloadActivities");
        }
    }
}
