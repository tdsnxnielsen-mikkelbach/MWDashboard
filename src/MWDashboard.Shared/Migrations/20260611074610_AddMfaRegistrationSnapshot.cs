using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaRegistrationSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MfaRegistrationSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalUsers = table.Column<int>(type: "int", nullable: false),
                    MfaRegistered = table.Column<int>(type: "int", nullable: false),
                    MfaCapable = table.Column<int>(type: "int", nullable: false),
                    PasswordlessCapable = table.Column<int>(type: "int", nullable: false),
                    SsprRegistered = table.Column<int>(type: "int", nullable: false),
                    SsprCapable = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaRegistrationSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MfaRegistrationSnapshots_TenantId_ReportDate",
                table: "MfaRegistrationSnapshots",
                columns: new[] { "TenantId", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MfaRegistrationSnapshots");
        }
    }
}
