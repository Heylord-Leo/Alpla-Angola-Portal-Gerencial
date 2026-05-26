using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlantSuggestionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SuggestedPlantConfidence",
                table: "HREmployees",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedPlantReason",
                table: "HREmployees",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SuggestedPlantResolvedAtUtc",
                table: "HREmployees",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedPlantSource",
                table: "HREmployees",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SuggestedPlantConfidence",
                table: "HREmployees");

            migrationBuilder.DropColumn(
                name: "SuggestedPlantReason",
                table: "HREmployees");

            migrationBuilder.DropColumn(
                name: "SuggestedPlantResolvedAtUtc",
                table: "HREmployees");

            migrationBuilder.DropColumn(
                name: "SuggestedPlantSource",
                table: "HREmployees");
        }
    }
}
