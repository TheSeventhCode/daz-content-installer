using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DazContentInstaller.Migrations
{
    /// <inheritdoc />
    public partial class AddArchiveThumbnails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArchiveThumbnailPath",
                table: "AssetLibraries",
                type: "TEXT",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailFileExtension",
                table: "Archives",
                type: "TEXT",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchiveThumbnailPath",
                table: "AssetLibraries");

            migrationBuilder.DropColumn(
                name: "ThumbnailFileExtension",
                table: "Archives");
        }
    }
}
