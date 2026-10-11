using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaMan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaFileAddedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AddedAt",
                table: "MediaFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            // Files found before this column have no record of when; their mtime is the closest guess.
            migrationBuilder.Sql("UPDATE \"MediaFiles\" SET \"AddedAt\" = \"Mtime\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddedAt",
                table: "MediaFiles");
        }
    }
}
