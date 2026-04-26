using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyPix.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaContentHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Media",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Media_EventId_ContentHash",
                table: "Media",
                columns: new[] { "EventId", "ContentHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Media_EventId_ContentHash",
                table: "Media");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Media");
        }
    }
}
