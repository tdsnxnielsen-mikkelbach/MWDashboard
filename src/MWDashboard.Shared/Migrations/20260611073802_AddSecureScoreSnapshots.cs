using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddSecureScoreSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecureScoreControlSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ControlName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ControlCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Score = table.Column<double>(type: "float", nullable: false),
                    ScoreInPercentage = table.Column<double>(type: "float", nullable: false),
                    ImplementationStatus = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecureScoreControlSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecureScoreSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrentScore = table.Column<double>(type: "float", nullable: false),
                    MaxScore = table.Column<double>(type: "float", nullable: false),
                    ActiveUserCount = table.Column<int>(type: "int", nullable: false),
                    LicensedUserCount = table.Column<int>(type: "int", nullable: false),
                    ComparativeScoreAllTenants = table.Column<double>(type: "float", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecureScoreSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecureScoreControlSnapshots_TenantId_ControlName_ReportDate",
                table: "SecureScoreControlSnapshots",
                columns: new[] { "TenantId", "ControlName", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecureScoreSnapshots_TenantId_ReportDate",
                table: "SecureScoreSnapshots",
                columns: new[] { "TenantId", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecureScoreControlSnapshots");

            migrationBuilder.DropTable(
                name: "SecureScoreSnapshots");
        }
    }
}
