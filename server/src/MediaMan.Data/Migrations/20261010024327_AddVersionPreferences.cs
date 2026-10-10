using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaMan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VersionPreferences",
                columns: table => new
                {
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TitleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaFileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Resolution = table.Column<string>(type: "TEXT", nullable: true),
                    HdrFormat = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VersionPreferences", x => new { x.ProfileId, x.TitleId });
                    table.ForeignKey(
                        name: "FK_VersionPreferences_MediaFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VersionPreferences_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VersionPreferences_Titles_TitleId",
                        column: x => x.TitleId,
                        principalTable: "Titles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VersionPreferences_MediaFileId",
                table: "VersionPreferences",
                column: "MediaFileId");

            migrationBuilder.CreateIndex(
                name: "IX_VersionPreferences_TitleId",
                table: "VersionPreferences",
                column: "TitleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VersionPreferences");
        }
    }
}
