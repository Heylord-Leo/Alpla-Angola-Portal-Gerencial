using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledDateUtcToRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledDateUtc",
                table: "Requests",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScheduledDateUtc",
                table: "Requests");
        }
    }
}
