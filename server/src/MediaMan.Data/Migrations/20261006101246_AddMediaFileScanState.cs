using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaMan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaFileScanState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttentionReason",
                table: "MediaFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MissingSince",
                table: "MediaFiles",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttentionReason",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "MissingSince",
                table: "MediaFiles");
        }
    }
}
