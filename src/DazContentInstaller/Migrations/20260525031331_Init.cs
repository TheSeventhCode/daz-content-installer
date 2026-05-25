using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DazContentInstaller.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetLibraries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    ArchiveBackupPath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsed = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetLibraries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Archives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArchiveName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ArchiveSize = table.Column<ulong>(type: "INTEGER", nullable: false),
                    ContentFingerprint = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentRoot = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    AssetTypes = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Categories = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    InstallStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    InstallCompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    AssetLibraryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentArchiveId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Archives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Archives_Archives_ParentArchiveId",
                        column: x => x.ParentArchiveId,
                        principalTable: "Archives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Archives_AssetLibraries_AssetLibraryId",
                        column: x => x.AssetLibraryId,
                        principalTable: "AssetLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstalledFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    InstalledPath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    AssetLibraryId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstalledFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstalledFiles_AssetLibraries_AssetLibraryId",
                        column: x => x.AssetLibraryId,
                        principalTable: "AssetLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileSize = table.Column<ulong>(type: "INTEGER", nullable: false),
                    ArchiveRelativePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    InstalledRelativePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    FileHash = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ArchiveId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetFiles_Archives_ArchiveId",
                        column: x => x.ArchiveId,
                        principalTable: "Archives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstallRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstalledFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstalledAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    HasBeenOverriden = table.Column<bool>(type: "INTEGER", nullable: false),
                    InstallRecordStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    AssetFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArchiveId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstallRecords_Archives_ArchiveId",
                        column: x => x.ArchiveId,
                        principalTable: "Archives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InstallRecords_AssetFiles_AssetFileId",
                        column: x => x.AssetFileId,
                        principalTable: "AssetFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InstallRecords_InstalledFiles_InstalledFileId",
                        column: x => x.InstalledFileId,
                        principalTable: "InstalledFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Archives_AssetLibraryId_ArchiveName_ParentArchiveId",
                table: "Archives",
                columns: new[] { "AssetLibraryId", "ArchiveName", "ParentArchiveId" });

            migrationBuilder.CreateIndex(
                name: "IX_Archives_AssetLibraryId_ParentArchiveId_ContentFingerprint",
                table: "Archives",
                columns: new[] { "AssetLibraryId", "ParentArchiveId", "ContentFingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_Archives_ParentArchiveId",
                table: "Archives",
                column: "ParentArchiveId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetFiles_ArchiveId_ArchiveRelativePath",
                table: "AssetFiles",
                columns: new[] { "ArchiveId", "ArchiveRelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstalledFiles_AssetLibraryId_InstalledPath_FileName",
                table: "InstalledFiles",
                columns: new[] { "AssetLibraryId", "InstalledPath", "FileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstallRecords_ArchiveId_InstalledFileId_HasBeenOverriden",
                table: "InstallRecords",
                columns: new[] { "ArchiveId", "InstalledFileId", "HasBeenOverriden" });

            migrationBuilder.CreateIndex(
                name: "IX_InstallRecords_AssetFileId",
                table: "InstallRecords",
                column: "AssetFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstallRecords_InstalledFileId",
                table: "InstallRecords",
                column: "InstalledFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstallRecords");

            migrationBuilder.DropTable(
                name: "AssetFiles");

            migrationBuilder.DropTable(
                name: "InstalledFiles");

            migrationBuilder.DropTable(
                name: "Archives");

            migrationBuilder.DropTable(
                name: "AssetLibraries");
        }
    }
}
