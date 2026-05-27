using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DazContentInstaller.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallFileOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InstallFileOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArchiveId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstallRecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstalledFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstalledRelativePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    BackupFilePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    BackupFileHash = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    NewFileHash = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallFileOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstallFileOperations_Archives_ArchiveId",
                        column: x => x.ArchiveId,
                        principalTable: "Archives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InstallFileOperations_InstallRecords_InstallRecordId",
                        column: x => x.InstallRecordId,
                        principalTable: "InstallRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InstallFileOperations_InstalledFiles_InstalledFileId",
                        column: x => x.InstalledFileId,
                        principalTable: "InstalledFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstallFileOperations_ArchiveId",
                table: "InstallFileOperations",
                column: "ArchiveId");

            migrationBuilder.CreateIndex(
                name: "IX_InstallFileOperations_InstalledFileId",
                table: "InstallFileOperations",
                column: "InstalledFileId");

            migrationBuilder.CreateIndex(
                name: "IX_InstallFileOperations_InstallRecordId",
                table: "InstallFileOperations",
                column: "InstallRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_InstallFileOperations_Status",
                table: "InstallFileOperations",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstallFileOperations");
        }
    }
}
