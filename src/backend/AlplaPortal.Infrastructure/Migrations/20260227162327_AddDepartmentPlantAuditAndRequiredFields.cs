using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentPlantAuditAndRequiredFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Requests_Departments_DepartmentId",
                table: "Requests");

            migrationBuilder.DropForeignKey(
                name: "FK_Requests_Plants_PlantId",
                table: "Requests");

            // 1. Add IsActive columns first so we can insert data with it
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Plants",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Departments",
                type: "bit",
                nullable: false,
                defaultValue: true);

            // 2. Insert dummy records to satisfy FKs if tables are empty or existing requests are null
            // Using ID 1 since it's likely the first. 
            // We use SQL because InsertData might conflict with existing IDs if any
            migrationBuilder.Sql("SET IDENTITY_INSERT Departments ON; IF NOT EXISTS (SELECT 1 FROM Departments WHERE Id = 1) INSERT INTO Departments (Id, Name, Code, IsActive) VALUES (1, 'Administração', 'ADM', 1); SET IDENTITY_INSERT Departments OFF;");
            migrationBuilder.Sql("SET IDENTITY_INSERT Plants ON; IF NOT EXISTS (SELECT 1 FROM Plants WHERE Id = 1) INSERT INTO Plants (Id, Name, Code, IsActive) VALUES (1, 'Luanda', 'LUA', 1); SET IDENTITY_INSERT Plants OFF;");

            // 3. Update existing requests
            migrationBuilder.Sql("UPDATE Requests SET DepartmentId = 1 WHERE DepartmentId IS NULL");
            migrationBuilder.Sql("UPDATE Requests SET PlantId = 1 WHERE PlantId IS NULL");

            // 4. Now make them non-nullable
            migrationBuilder.AlterColumn<int>(
                name: "PlantId",
                table: "Requests",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "DepartmentId",
                table: "Requests",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_Departments_DepartmentId",
                table: "Requests",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_Plants_PlantId",
                table: "Requests",
                column: "PlantId",
                principalTable: "Plants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Requests_Departments_DepartmentId",
                table: "Requests");

            migrationBuilder.DropForeignKey(
                name: "FK_Requests_Plants_PlantId",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Plants");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Departments");

            migrationBuilder.AlterColumn<int>(
                name: "PlantId",
                table: "Requests",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "DepartmentId",
                table: "Requests",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_Departments_DepartmentId",
                table: "Requests",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_Plants_PlantId",
                table: "Requests",
                column: "PlantId",
                principalTable: "Plants",
                principalColumn: "Id");
        }
    }
}
