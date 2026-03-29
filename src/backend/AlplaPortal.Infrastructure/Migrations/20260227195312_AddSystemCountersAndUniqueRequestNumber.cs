using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemCountersAndUniqueRequestNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemCounters",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CurrentValue = table.Column<int>(type: "int", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemCounters", x => x.Id);
                });

            migrationBuilder.Sql(@"
                WITH CTE AS (
                    SELECT 
                        Id,
                        ROW_NUMBER() OVER (PARTITION BY RequestNumber ORDER BY CreatedAtUtc ASC) as rn
                    FROM Requests
                    WHERE RequestNumber IS NOT NULL
                )
                UPDATE R
                SET R.RequestNumber = R.RequestNumber + '-DUP-' + CAST(C.rn - 1 AS NVARCHAR(10))
                FROM Requests R
                JOIN CTE C ON R.Id = C.Id
                WHERE C.rn > 1;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_RequestNumber",
                table: "Requests",
                column: "RequestNumber",
                unique: true,
                filter: "[RequestNumber] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemCounters");

            migrationBuilder.DropIndex(
                name: "IX_Requests_RequestNumber",
                table: "Requests");
        }
    }
}
