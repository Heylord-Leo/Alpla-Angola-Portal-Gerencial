using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestWorkflowParticipantsAndStatusSeeding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BadgeColor",
                table: "RequestStatuses",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "RequestStatuses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "AreaApproverId",
                table: "Requests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BuyerId",
                table: "Requests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FinalApproverId",
                table: "Requests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "BadgeColor", "Code", "DisplayOrder", "Name" },
                values: new object[] { "gray", "RASCUNHO", 1, "Rascunho" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "BadgeColor", "Code", "DisplayOrder", "Name" },
                values: new object[] { "blue", "AG_COTACAO", 2, "Aguardando Cotação" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "BadgeColor", "Code", "DisplayOrder", "Name" },
                values: new object[] { "orange", "AG_APROV_AREA", 3, "Aguardando Aprovação Área" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "BadgeColor", "Code", "DisplayOrder", "Name" },
                values: new object[] { "red", "REAJUSTE_AA", 4, "Reajuste A.A" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "BadgeColor", "Code", "DisplayOrder", "Name" },
                values: new object[] { "orange", "AG_APROV_FINAL", 5, "Aguardando Aprovação Final" });

            migrationBuilder.InsertData(
                table: "RequestStatuses",
                columns: new[] { "Id", "BadgeColor", "Code", "DisplayOrder", "IsActive", "Name" },
                values: new object[,]
                {
                    { 6, "red", "REAJUSTE_AF", 6, true, "Reajuste A.F" },
                    { 7, "red", "REJEITADO", 7, true, "Rejeitado" },
                    { 8, "blue", "INSERIR_CC", 8, true, "Inserir C.C" },
                    { 9, "green", "APROVADO", 9, true, "Aprovado" },
                    { 10, "blue", "FATURA_INSERIDA", 10, true, "Fatura Proforma Inserida" },
                    { 11, "blue", "SOLICITADO_PO", 11, true, "Solicitado P.O" },
                    { 12, "green", "PO_EMITIDA", 12, true, "P.O Emitida" },
                    { 13, "blue", "SOLIC_PAG_ENVIADA", 13, true, "Solicitação Pagamento Enviada" },
                    { 14, "orange", "PAG_AGENDADO", 14, true, "Pagamento Agendado" },
                    { 15, "green", "PAG_REALIZADO", 15, true, "Pagamento Realizado" },
                    { 16, "orange", "AG_RECIBO", 16, true, "Aguardando Recibo" },
                    { 17, "green", "FINALIZADO", 17, true, "Finalizado" },
                    { 18, "gray", "CANCELADO", 18, true, "Cancelado" }
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "DepartmentId", "Email", "FullName", "IsActive" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), null, "requester@alpla.com", "Mock Requester", true },
                    { new Guid("22222222-2222-2222-2222-222222222222"), null, "comprador@alpla.com", "Comprador Especialista", true },
                    { new Guid("33333333-3333-3333-3333-333333333333"), null, "diretor@alpla.com", "Diretor (Aprovador)", true }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_AreaApproverId",
                table: "Requests",
                column: "AreaApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_BuyerId",
                table: "Requests",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_FinalApproverId",
                table: "Requests",
                column: "FinalApproverId");

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_Users_AreaApproverId",
                table: "Requests",
                column: "AreaApproverId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_Users_BuyerId",
                table: "Requests",
                column: "BuyerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_Users_FinalApproverId",
                table: "Requests",
                column: "FinalApproverId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Requests_Users_AreaApproverId",
                table: "Requests");

            migrationBuilder.DropForeignKey(
                name: "FK_Requests_Users_BuyerId",
                table: "Requests");

            migrationBuilder.DropForeignKey(
                name: "FK_Requests_Users_FinalApproverId",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Requests_AreaApproverId",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Requests_BuyerId",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Requests_FinalApproverId",
                table: "Requests");

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 17);

            migrationBuilder.DeleteData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"));

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"));

            migrationBuilder.DropColumn(
                name: "BadgeColor",
                table: "RequestStatuses");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "RequestStatuses");

            migrationBuilder.DropColumn(
                name: "AreaApproverId",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "BuyerId",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "FinalApproverId",
                table: "Requests");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Code", "Name" },
                values: new object[] { "DRAFT", "Draft" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Code", "Name" },
                values: new object[] { "PENDING_APPROVAL", "Pending Approval" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Code", "Name" },
                values: new object[] { "APPROVED", "Approved" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Code", "Name" },
                values: new object[] { "REJECTED", "Rejected" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Code", "Name" },
                values: new object[] { "CANCELLED", "Cancelled" });
        }
    }
}
