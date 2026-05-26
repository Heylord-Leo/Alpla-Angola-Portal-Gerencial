using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContractPaymentRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowsManualDueDateOverride",
                table: "Contracts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FinancialNotes",
                table: "Contracts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GracePeriodDays",
                table: "Contracts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasLateInterest",
                table: "Contracts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasLatePenalty",
                table: "Contracts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LateInterestTypeCode",
                table: "Contracts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LateInterestValue",
                table: "Contracts",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LatePenaltyTypeCode",
                table: "Contracts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LatePenaltyValue",
                table: "Contracts",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentFixedDay",
                table: "Contracts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentRuleSummary",
                table: "Contracts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentTermDays",
                table: "Contracts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTermTypeCode",
                table: "Contracts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PenaltyNotes",
                table: "Contracts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceEventTypeCode",
                table: "Contracts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingReference",
                table: "ContractPaymentObligations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CalculatedDueDateUtc",
                table: "ContractPaymentObligations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DueDateSourceCode",
                table: "ContractPaymentObligations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GraceDateUtc",
                table: "ContractPaymentObligations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvoiceReceivedDateUtc",
                table: "ContractPaymentObligations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObligationNotes",
                table: "ContractPaymentObligations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PenaltyStartDateUtc",
                table: "ContractPaymentObligations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReferenceDateUtc",
                table: "ContractPaymentObligations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ServiceAcceptanceDateUtc",
                table: "ContractPaymentObligations",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowsManualDueDateOverride",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "FinancialNotes",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "GracePeriodDays",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "HasLateInterest",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "HasLatePenalty",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "LateInterestTypeCode",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "LateInterestValue",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "LatePenaltyTypeCode",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "LatePenaltyValue",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PaymentFixedDay",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PaymentRuleSummary",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PaymentTermDays",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PaymentTermTypeCode",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PenaltyNotes",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "ReferenceEventTypeCode",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "BillingReference",
                table: "ContractPaymentObligations");

            migrationBuilder.DropColumn(
                name: "CalculatedDueDateUtc",
                table: "ContractPaymentObligations");

            migrationBuilder.DropColumn(
                name: "DueDateSourceCode",
                table: "ContractPaymentObligations");

            migrationBuilder.DropColumn(
                name: "GraceDateUtc",
                table: "ContractPaymentObligations");

            migrationBuilder.DropColumn(
                name: "InvoiceReceivedDateUtc",
                table: "ContractPaymentObligations");

            migrationBuilder.DropColumn(
                name: "ObligationNotes",
                table: "ContractPaymentObligations");

            migrationBuilder.DropColumn(
                name: "PenaltyStartDateUtc",
                table: "ContractPaymentObligations");

            migrationBuilder.DropColumn(
                name: "ReferenceDateUtc",
                table: "ContractPaymentObligations");

            migrationBuilder.DropColumn(
                name: "ServiceAcceptanceDateUtc",
                table: "ContractPaymentObligations");
        }
    }
}
