using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaMan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryScanTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastScanFinishedAt",
                table: "Libraries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ScanStartedAt",
                table: "Libraries",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastScanFinishedAt",
                table: "Libraries");

            migrationBuilder.DropColumn(
                name: "ScanStartedAt",
                table: "Libraries");
        }
    }
}
