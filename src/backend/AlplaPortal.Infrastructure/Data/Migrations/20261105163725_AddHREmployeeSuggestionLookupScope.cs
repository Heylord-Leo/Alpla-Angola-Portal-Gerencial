using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHREmployeeSuggestionLookupScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SuggestedPlantLookupComplete",
                table: "HREmployees",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedPlantLookupScope",
                table: "HREmployees",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SuggestedPlantLookupComplete",
                table: "HREmployees");

            migrationBuilder.DropColumn(
                name: "SuggestedPlantLookupScope",
                table: "HREmployees");
        }
    }
}
