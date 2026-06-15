using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectoryAuditLicenseIssuesOAuthMailboxAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DirectoryAuditCursorUtc",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DirectoryAuditSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Activity = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    EventCount = table.Column<int>(type: "int", nullable: false),
                    FailureCount = table.Column<int>(type: "int", nullable: false),
                    DistinctActors = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectoryAuditSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LicenseAssignmentIssueSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SkuPartNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SkuId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ErrorUsers = table.Column<int>(type: "int", nullable: false),
                    DisabledLicensedUsers = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseAssignmentIssueSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MailboxAccessSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AccessType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EventCount = table.Column<int>(type: "int", nullable: false),
                    DistinctMailboxes = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailboxAccessSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OAuthGrantSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AppDisplayName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    AppId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GrantType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HighRiskScopes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ScopeCount = table.Column<int>(type: "int", nullable: false),
                    IsAdminConsented = table.Column<bool>(type: "bit", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthGrantSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryAuditSnapshots_TenantId_Category_Activity_ReportDate",
                table: "DirectoryAuditSnapshots",
                columns: new[] { "TenantId", "Category", "Activity", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicenseAssignmentIssueSnapshots_TenantId_SkuPartNumber_ReportDate",
                table: "LicenseAssignmentIssueSnapshots",
                columns: new[] { "TenantId", "SkuPartNumber", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MailboxAccessSnapshots_TenantId_AccessType_ReportDate",
                table: "MailboxAccessSnapshots",
                columns: new[] { "TenantId", "AccessType", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OAuthGrantSnapshots_TenantId_AppId_GrantType_ReportDate",
                table: "OAuthGrantSnapshots",
                columns: new[] { "TenantId", "AppId", "GrantType", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DirectoryAuditSnapshots");

            migrationBuilder.DropTable(
                name: "LicenseAssignmentIssueSnapshots");

            migrationBuilder.DropTable(
                name: "MailboxAccessSnapshots");

            migrationBuilder.DropTable(
                name: "OAuthGrantSnapshots");

            migrationBuilder.DropColumn(
                name: "DirectoryAuditCursorUtc",
                table: "Tenants");
        }
    }
}
