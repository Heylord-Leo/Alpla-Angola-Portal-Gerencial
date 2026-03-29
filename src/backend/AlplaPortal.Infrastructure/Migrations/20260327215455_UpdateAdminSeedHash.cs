using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAdminSeedHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$ZQ/WRuE5UuZpWPECThOQNudjM1i1jftPfkHq0vvMXgZKBDHxHsML.");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                column: "PasswordHash",
                value: "$2a$11$ZQ/WRuE5UuZpWPECThOQNudjM1i1jftPfkHq0vvMXgZKBDHxHsML.");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                column: "PasswordHash",
                value: "$2a$11$ZQ/WRuE5UuZpWPECThOQNudjM1i1jftPfkHq0vvMXgZKBDHxHsML.");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                column: "PasswordHash",
                value: "$2a$11$ZQ/WRuE5UuZpWPECThOQNudjM1i1jftPfkHq0vvMXgZKBDHxHsML.");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                column: "PasswordHash",
                value: "$2a$11$ZQ/WRuE5UuZpWPECThOQNudjM1i1jftPfkHq0vvMXgZKBDHxHsML.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$mC7Gdbv8L.ee8H5PF8tGbuG.K.XqR.hWbH6mZzQ.T1/zV9.qB.Z2O");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                column: "PasswordHash",
                value: "$2a$11$mC7Gdbv8L.ee8H5PF8tGbuG.K.XqR.hWbH6mZzQ.T1/zV9.qB.Z2O");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                column: "PasswordHash",
                value: "$2a$11$mC7Gdbv8L.ee8H5PF8tGbuG.K.XqR.hWbH6mZzQ.T1/zV9.qB.Z2O");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                column: "PasswordHash",
                value: "$2a$11$mC7Gdbv8L.ee8H5PF8tGbuG.K.XqR.hWbH6mZzQ.T1/zV9.qB.Z2O");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                column: "PasswordHash",
                value: "$2a$11$mC7Gdbv8L.ee8H5PF8tGbuG.K.XqR.hWbH6mZzQ.T1/zV9.qB.Z2O");
        }
    }
}
