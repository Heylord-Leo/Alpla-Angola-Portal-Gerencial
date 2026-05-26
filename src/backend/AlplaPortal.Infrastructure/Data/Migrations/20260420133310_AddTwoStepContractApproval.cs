using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTwoStepContractApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FinalApproverId",
                table: "Contracts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TechnicalApproverId",
                table: "Contracts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_FinalApproverId",
                table: "Contracts",
                column: "FinalApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_TechnicalApproverId",
                table: "Contracts",
                column: "TechnicalApproverId");

            migrationBuilder.AddForeignKey(
                name: "FK_Contracts_Users_FinalApproverId",
                table: "Contracts",
                column: "FinalApproverId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Contracts_Users_TechnicalApproverId",
                table: "Contracts",
                column: "TechnicalApproverId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contracts_Users_FinalApproverId",
                table: "Contracts");

            migrationBuilder.DropForeignKey(
                name: "FK_Contracts_Users_TechnicalApproverId",
                table: "Contracts");

            migrationBuilder.DropIndex(
                name: "IX_Contracts_FinalApproverId",
                table: "Contracts");

            migrationBuilder.DropIndex(
                name: "IX_Contracts_TechnicalApproverId",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "FinalApproverId",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "TechnicalApproverId",
                table: "Contracts");
        }
    }
}
