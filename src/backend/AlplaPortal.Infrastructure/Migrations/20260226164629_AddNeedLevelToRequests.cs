using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNeedLevelToRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NeedLevelId",
                table: "Requests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_NeedLevelId",
                table: "Requests",
                column: "NeedLevelId");

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_NeedLevels_NeedLevelId",
                table: "Requests",
                column: "NeedLevelId",
                principalTable: "NeedLevels",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Requests_NeedLevels_NeedLevelId",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Requests_NeedLevelId",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "NeedLevelId",
                table: "Requests");
        }
    }
}
