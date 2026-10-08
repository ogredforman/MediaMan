using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaMan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionActiveProfile : Migration
    {
        /// <inheritdoc />
        // Hand-written: EF would rebuild both tables (SQLite can't add a foreign key or change a collation in place), and
        // a rebuild can't run inside the migration's transaction. ADD COLUMN can carry the foreign key itself, and the
        // index carries the NOCASE collation, so profile names are unique per account ignoring case (ProfileService
        // compares them with the same collation).
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """ALTER TABLE "Sessions" ADD COLUMN "ActiveProfileId" TEXT NULL REFERENCES "Profiles" ("Id") ON DELETE SET NULL;""");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ActiveProfileId",
                table: "Sessions",
                column: "ActiveProfileId");

            migrationBuilder.DropIndex(
                name: "IX_Profiles_UserId_Name",
                table: "Profiles");

            migrationBuilder.Sql(
                """CREATE UNIQUE INDEX "IX_Profiles_UserId_Name" ON "Profiles" ("UserId", "Name" COLLATE NOCASE);""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Profiles_UserId_Name",
                table: "Profiles");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_UserId_Name",
                table: "Profiles",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_Sessions_ActiveProfileId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ActiveProfileId",
                table: "Sessions");
        }
    }
}
