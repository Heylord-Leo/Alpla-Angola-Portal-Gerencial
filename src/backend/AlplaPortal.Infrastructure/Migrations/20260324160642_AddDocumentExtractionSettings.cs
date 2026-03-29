using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentExtractionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentExtractionSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DefaultProvider = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: true),
                    GlobalTimeoutSeconds = table.Column<int>(type: "int", nullable: true),
                    LocalOcrEnabled = table.Column<bool>(type: "bit", nullable: true),
                    LocalOcrBaseUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LocalOcrTimeoutSeconds = table.Column<int>(type: "int", nullable: true),
                    OpenAiEnabled = table.Column<bool>(type: "bit", nullable: true),
                    OpenAiTimeoutSeconds = table.Column<int>(type: "int", nullable: true),
                    AzureDocumentIntelligenceEnabled = table.Column<bool>(type: "bit", nullable: true),
                    AzureDocumentIntelligenceTimeoutSeconds = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentExtractionSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentExtractionSettings");
        }
    }
}
