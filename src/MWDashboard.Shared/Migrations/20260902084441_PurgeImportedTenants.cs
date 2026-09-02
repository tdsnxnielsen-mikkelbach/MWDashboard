using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class PurgeImportedTenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One-time cleanup of the "pricing-only" tenant rows imported by the now-removed Ion
            // Gateway / Partner Center sync. Those rows were added inactive and were never Graph-
            // collected (permissions never probed); the removed PricingOnly discriminator column is
            // gone, so identify them by that signature. Real tenants are active and/or probed.
            migrationBuilder.Sql(
                "DELETE FROM [Tenants] WHERE [IsActive] = 0 AND [PermissionsCheckedAt] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
