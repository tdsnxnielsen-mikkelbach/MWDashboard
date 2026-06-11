using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddInactiveAccountSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InactiveAccountSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalLicensedUsers = table.Column<int>(type: "int", nullable: false),
                    Inactive30 = table.Column<int>(type: "int", nullable: false),
                    Inactive60 = table.Column<int>(type: "int", nullable: false),
                    Inactive90 = table.Column<int>(type: "int", nullable: false),
                    NeverSignedIn = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InactiveAccountSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InactiveAccountSnapshots_TenantId_ReportDate",
                table: "InactiveAccountSnapshots",
                columns: new[] { "TenantId", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InactiveAccountSnapshots");
        }
    }
}
