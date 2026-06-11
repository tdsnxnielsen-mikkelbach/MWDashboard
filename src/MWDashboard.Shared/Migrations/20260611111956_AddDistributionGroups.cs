using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddDistributionGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DistributionGroups",
                table: "GroupSnapshots",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DistributionGroups",
                table: "GroupSnapshots");
        }
    }
}
