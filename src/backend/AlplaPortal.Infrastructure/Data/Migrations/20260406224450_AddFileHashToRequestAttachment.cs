using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFileHashToRequestAttachment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileHash",
                table: "RequestAttachments",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestAttachments_FileHash",
                table: "RequestAttachments",
                column: "FileHash",
                filter: "[FileHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RequestAttachments_FileHash",
                table: "RequestAttachments");

            migrationBuilder.DropColumn(
                name: "FileHash",
                table: "RequestAttachments");
        }
    }
}
