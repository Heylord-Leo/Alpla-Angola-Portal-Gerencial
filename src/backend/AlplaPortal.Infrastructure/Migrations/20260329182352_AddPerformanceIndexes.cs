using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Requests_CreatedAtUtc",
                table: "Requests",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLineItems_RequestId_IsDeleted",
                table: "RequestLineItems",
                columns: new[] { "RequestId", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Requests_CreatedAtUtc",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_RequestLineItems_RequestId_IsDeleted",
                table: "RequestLineItems");
        }
    }
}
