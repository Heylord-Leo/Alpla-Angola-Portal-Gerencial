using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddItemCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ItemCatalogId",
                table: "RequestLineItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ItemCatalogItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefaultUnitId = table.Column<int>(type: "int", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Origin = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemCatalogItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemCatalogItems_Units_DefaultUnitId",
                        column: x => x.DefaultUnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestLineItems_ItemCatalogId",
                table: "RequestLineItems",
                column: "ItemCatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCatalogItems_Code",
                table: "ItemCatalogItems",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemCatalogItems_DefaultUnitId",
                table: "ItemCatalogItems",
                column: "DefaultUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestLineItems_ItemCatalogItems_ItemCatalogId",
                table: "RequestLineItems",
                column: "ItemCatalogId",
                principalTable: "ItemCatalogItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RequestLineItems_ItemCatalogItems_ItemCatalogId",
                table: "RequestLineItems");

            migrationBuilder.DropTable(
                name: "ItemCatalogItems");

            migrationBuilder.DropIndex(
                name: "IX_RequestLineItems_ItemCatalogId",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "ItemCatalogId",
                table: "RequestLineItems");
        }
    }
}
