using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernanceSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SharePointAuditCursorUtc",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppCredentialSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AppId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AppObjectId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AppDisplayName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    CredentialType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    KeyId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    EndDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DaysToExpiry = table.Column<int>(type: "int", nullable: false),
                    IsExpired = table.Column<bool>(type: "bit", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCredentialSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DefenderAlertSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AlertCount = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefenderAlertSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalSharingSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ShareType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EventCount = table.Column<int>(type: "int", nullable: false),
                    DistinctUsers = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalSharingSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrivilegedRoleSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RoleName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RoleTemplateId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MemberCount = table.Column<int>(type: "int", nullable: false),
                    IsPrivileged = table.Column<bool>(type: "bit", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivilegedRoleSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppCredentialSnapshots_TenantId_ReportDate_AppObjectId_KeyId",
                table: "AppCredentialSnapshots",
                columns: new[] { "TenantId", "ReportDate", "AppObjectId", "KeyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DefenderAlertSnapshots_TenantId_Severity_Status_ReportDate",
                table: "DefenderAlertSnapshots",
                columns: new[] { "TenantId", "Severity", "Status", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalSharingSnapshots_TenantId_ShareType_ReportDate",
                table: "ExternalSharingSnapshots",
                columns: new[] { "TenantId", "ShareType", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivilegedRoleSnapshots_TenantId_RoleName_ReportDate",
                table: "PrivilegedRoleSnapshots",
                columns: new[] { "TenantId", "RoleName", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppCredentialSnapshots");

            migrationBuilder.DropTable(
                name: "DefenderAlertSnapshots");

            migrationBuilder.DropTable(
                name: "ExternalSharingSnapshots");

            migrationBuilder.DropTable(
                name: "PrivilegedRoleSnapshots");

            migrationBuilder.DropColumn(
                name: "SharePointAuditCursorUtc",
                table: "Tenants");
        }
    }
}
