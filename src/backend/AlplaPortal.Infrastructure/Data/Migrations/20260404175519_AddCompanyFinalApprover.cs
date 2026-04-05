using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyFinalApprover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FinalApproverUserId",
                table: "Companies",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 1,
                column: "FinalApproverUserId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: 2,
                column: "FinalApproverUserId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Companies_FinalApproverUserId",
                table: "Companies",
                column: "FinalApproverUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Users_FinalApproverUserId",
                table: "Companies",
                column: "FinalApproverUserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Companies_Users_FinalApproverUserId",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Companies_FinalApproverUserId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "FinalApproverUserId",
                table: "Companies");
        }
    }
}
