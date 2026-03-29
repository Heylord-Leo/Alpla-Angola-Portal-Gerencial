using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefactorRequestCompanyPlant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Shift DisplayOrder to avoid unique constraint conflicts during sequential updates in a fresh DB
            migrationBuilder.Sql("UPDATE [RequestStatuses] SET [DisplayOrder] = [DisplayOrder] + 1000");
            migrationBuilder.Sql("UPDATE [LineItemStatuses] SET [DisplayOrder] = [DisplayOrder] + 1000");

            migrationBuilder.DropForeignKey(
                name: "FK_Requests_Plants_PlantId",
                table: "Requests");

            migrationBuilder.AlterColumn<int>(
                name: "PlantId",
                table: "Requests",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Requests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PlantId",
                table: "RequestLineItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Plants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Companies",
                columns: new[] { "Id", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, true, "AlplaPLASTICO" },
                    { 2, true, "AlplaSOPRO" }
                });

            migrationBuilder.UpdateData(
                table: "LineItemStatuses",
                keyColumn: "Id",
                keyValue: 4,
                column: "DisplayOrder",
                value: 5);

            migrationBuilder.UpdateData(
                table: "LineItemStatuses",
                keyColumn: "Id",
                keyValue: 5,
                column: "DisplayOrder",
                value: 6);

            migrationBuilder.UpdateData(
                table: "LineItemStatuses",
                keyColumn: "Id",
                keyValue: 6,
                column: "DisplayOrder",
                value: 7);

            migrationBuilder.UpdateData(
                table: "LineItemStatuses",
                keyColumn: "Id",
                keyValue: 7,
                column: "DisplayOrder",
                value: 8);

            migrationBuilder.InsertData(
                table: "LineItemStatuses",
                columns: new[] { "Id", "BadgeColor", "Code", "DisplayOrder", "IsActive", "Name" },
                values: new object[] { 8, "slate", "WAITING_ORDER", 4, true, "Aguardando Encomenda" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 17,
                column: "DisplayOrder",
                value: 19);

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 18,
                column: "DisplayOrder",
                value: 20);

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 20,
                column: "DisplayOrder",
                value: 21);

            migrationBuilder.InsertData(
                table: "RequestStatuses",
                columns: new[] { "Id", "BadgeColor", "Code", "DisplayOrder", "IsActive", "Name" },
                values: new object[] { 21, "indigo", "IN_FOLLOWUP", 18, true, "Em Acompanhamento" });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "DepartmentId", "Email", "FullName", "IsActive" },
                values: new object[] { new Guid("44444444-4444-4444-4444-444444444444"), null, "dev@alpla.com", "Dev Fallback", true });

            migrationBuilder.UpdateData(
                table: "Plants",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Code", "CompanyId", "IsActive", "Name" },
                values: new object[] { "V1", 1, true, "Viana 1" });

            migrationBuilder.InsertData(
                table: "Plants",
                columns: new[] { "Id", "Code", "CompanyId", "IsActive", "Name" },
                values: new object[,]
                {
                    { 2, "V2", 1, true, "Viana 2" },
                    { 3, "V3", 2, true, "Viana 3" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_CompanyId",
                table: "Requests",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLineItems_PlantId",
                table: "RequestLineItems",
                column: "PlantId");

            migrationBuilder.CreateIndex(
                name: "IX_Plants_CompanyId",
                table: "Plants",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Plants_Companies_CompanyId",
                table: "Plants",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RequestLineItems_Plants_PlantId",
                table: "RequestLineItems",
                column: "PlantId",
                principalTable: "Plants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_Companies_CompanyId",
                table: "Requests",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_Plants_PlantId",
                table: "Requests",
                column: "PlantId",
                principalTable: "Plants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Plants_Companies_CompanyId",
                table: "Plants");

            migrationBuilder.DropForeignKey(
                name: "FK_RequestLineItems_Plants_PlantId",
                table: "RequestLineItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Requests_Companies_CompanyId",
                table: "Requests");

            migrationBuilder.DropForeignKey(
                name: "FK_Requests_Plants_PlantId",
                table: "Requests");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Requests_CompanyId",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_RequestLineItems_PlantId",
                table: "RequestLineItems");

            migrationBuilder.DropIndex(
                name: "IX_Plants_CompanyId",
                table: "Plants");

            migrationBuilder.DeleteData(
                table: "LineItemStatuses",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "Plants",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Plants",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Plants",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 21);

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"));

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "PlantId",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Plants");

            migrationBuilder.AlterColumn<int>(
                name: "PlantId",
                table: "Requests",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "LineItemStatuses",
                keyColumn: "Id",
                keyValue: 4,
                column: "DisplayOrder",
                value: 4);

            migrationBuilder.UpdateData(
                table: "LineItemStatuses",
                keyColumn: "Id",
                keyValue: 5,
                column: "DisplayOrder",
                value: 5);

            migrationBuilder.UpdateData(
                table: "LineItemStatuses",
                keyColumn: "Id",
                keyValue: 6,
                column: "DisplayOrder",
                value: 6);

            migrationBuilder.UpdateData(
                table: "LineItemStatuses",
                keyColumn: "Id",
                keyValue: 7,
                column: "DisplayOrder",
                value: 7);

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 17,
                column: "DisplayOrder",
                value: 18);

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 18,
                column: "DisplayOrder",
                value: 19);

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 20,
                column: "DisplayOrder",
                value: 20);

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_Plants_PlantId",
                table: "Requests",
                column: "PlantId",
                principalTable: "Plants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
