using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddMailRuleDlpSubscriptionTeamsActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DlpAuditCursorUtc",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExchangeAuditCursorUtc",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DlpEventSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PolicyName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MatchCount = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DlpEventSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MailRuleEventSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RuleType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EventCount = table.Column<int>(type: "int", nullable: false),
                    DistinctMailboxes = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailRuleEventSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SkuId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SkuPartNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsTrial = table.Column<bool>(type: "bit", nullable: false),
                    TotalLicenses = table.Column<int>(type: "int", nullable: false),
                    NextLifecycleDateTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamsTeamActivitySnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TeamId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TeamName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    TeamType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ActiveUsers = table.Column<int>(type: "int", nullable: false),
                    ActiveChannels = table.Column<int>(type: "int", nullable: false),
                    Guests = table.Column<int>(type: "int", nullable: false),
                    ChannelMessages = table.Column<int>(type: "int", nullable: false),
                    ReplyMessages = table.Column<int>(type: "int", nullable: false),
                    MeetingsOrganized = table.Column<int>(type: "int", nullable: false),
                    Reactions = table.Column<int>(type: "int", nullable: false),
                    LastActivityDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamsTeamActivitySnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DlpEventSnapshots_TenantId_PolicyName_ReportDate",
                table: "DlpEventSnapshots",
                columns: new[] { "TenantId", "PolicyName", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MailRuleEventSnapshots_TenantId_RuleType_ReportDate",
                table: "MailRuleEventSnapshots",
                columns: new[] { "TenantId", "RuleType", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSnapshots_TenantId_SkuId_ReportDate",
                table: "SubscriptionSnapshots",
                columns: new[] { "TenantId", "SkuId", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamsTeamActivitySnapshots_TenantId_TeamId_ReportDate",
                table: "TeamsTeamActivitySnapshots",
                columns: new[] { "TenantId", "TeamId", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DlpEventSnapshots");

            migrationBuilder.DropTable(
                name: "MailRuleEventSnapshots");

            migrationBuilder.DropTable(
                name: "SubscriptionSnapshots");

            migrationBuilder.DropTable(
                name: "TeamsTeamActivitySnapshots");

            migrationBuilder.DropColumn(
                name: "DlpAuditCursorUtc",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ExchangeAuditCursorUtc",
                table: "Tenants");
        }
    }
}
