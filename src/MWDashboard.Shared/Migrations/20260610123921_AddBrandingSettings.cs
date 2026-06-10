using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MWDashboard.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrandingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LogoBase64 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LogoContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FaviconBase64 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FaviconContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LightPrimary = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LightSecondary = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LightAppbar = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DarkPrimary = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DarkSecondary = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DarkAppbar = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AppTitle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandingSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrandingSettings");
        }
    }
}
