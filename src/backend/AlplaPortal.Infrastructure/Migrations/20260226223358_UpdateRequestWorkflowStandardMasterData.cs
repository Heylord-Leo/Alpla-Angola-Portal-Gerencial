using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateRequestWorkflowStandardMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "RequestStatuses",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 1,
                column: "Code",
                value: "DRAFT");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 2,
                column: "Code",
                value: "WAITING_QUOTATION");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "indigo", "WAITING_AREA_APPROVAL" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "orange", "AREA_ADJUSTMENT" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "purple", "WAITING_FINAL_APPROVAL" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "teal", "FINAL_ADJUSTMENT" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 7,
                column: "Code",
                value: "REJECTED");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "yellow", "WAITING_COST_CENTER" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 9,
                column: "Code",
                value: "APPROVED");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "slate", "PROFORMA_INVOICE_INSERTED" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "cyan", "PO_REQUESTED" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "sky", "PO_ISSUED" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "rose", "PAYMENT_REQUEST_SENT" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "violet", "PAYMENT_SCHEDULED" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "fuchsia", "PAYMENT_COMPLETED" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 16,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "stone", "WAITING_RECEIPT" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 17,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "emerald", "COMPLETED" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 18,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "amber", "CANCELLED" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestStatuses_Code",
                table: "RequestStatuses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestStatuses_DisplayOrder",
                table: "RequestStatuses",
                column: "DisplayOrder",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RequestStatuses_Code",
                table: "RequestStatuses");

            migrationBuilder.DropIndex(
                name: "IX_RequestStatuses_DisplayOrder",
                table: "RequestStatuses");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "RequestStatuses",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 1,
                column: "Code",
                value: "RASCUNHO");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 2,
                column: "Code",
                value: "AG_COTACAO");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "orange", "AG_APROV_AREA" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "red", "REAJUSTE_AA" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "orange", "AG_APROV_FINAL" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "red", "REAJUSTE_AF" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 7,
                column: "Code",
                value: "REJEITADO");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "blue", "INSERIR_CC" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 9,
                column: "Code",
                value: "APROVADO");

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "blue", "FATURA_INSERIDA" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "blue", "SOLICITADO_PO" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "green", "PO_EMITIDA" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "blue", "SOLIC_PAG_ENVIADA" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "orange", "PAG_AGENDADO" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "green", "PAG_REALIZADO" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 16,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "orange", "AG_RECIBO" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 17,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "green", "FINALIZADO" });

            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 18,
                columns: new[] { "BadgeColor", "Code" },
                values: new object[] { "gray", "CANCELADO" });
        }
    }
}
