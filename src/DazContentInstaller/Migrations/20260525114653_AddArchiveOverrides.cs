using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DazContentInstaller.Migrations
{
    /// <inheritdoc />
    public partial class AddArchiveOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArchiveOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RootArchiveId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetLibraryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstalledRelativeDirectory = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ManagedFilePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    OriginalFileBackupPath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchiveOverrides_Archives_RootArchiveId",
                        column: x => x.RootArchiveId,
                        principalTable: "Archives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArchiveOverrides_AssetLibraries_AssetLibraryId",
                        column: x => x.AssetLibraryId,
                        principalTable: "AssetLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveOverrides_AssetLibraryId",
                table: "ArchiveOverrides",
                column: "AssetLibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveOverrides_RootArchiveId_InstalledRelativeDirectory_FileName",
                table: "ArchiveOverrides",
                columns: new[] { "RootArchiveId", "InstalledRelativeDirectory", "FileName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchiveOverrides");
        }
    }
}
