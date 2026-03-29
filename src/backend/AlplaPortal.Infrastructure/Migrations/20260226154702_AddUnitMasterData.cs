using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Unit",
                table: "RequestLineItems");

            migrationBuilder.AddColumn<int>(
                name: "UnitId",
                table: "RequestLineItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Units",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Units", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Units",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { 1, "UN", "Unidade" },
                    { 2, "EA", "Each" },
                    { 3, "KG", "Quilograma" },
                    { 4, "L", "Litro" },
                    { 5, "M", "Metro" },
                    { 6, "CX", "Caixa" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestLineItems_UnitId",
                table: "RequestLineItems",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestLineItems_Units_UnitId",
                table: "RequestLineItems",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RequestLineItems_Units_UnitId",
                table: "RequestLineItems");

            migrationBuilder.DropTable(
                name: "Units");

            migrationBuilder.DropIndex(
                name: "IX_RequestLineItems_UnitId",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "RequestLineItems");

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "RequestLineItems",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }
    }
}
