using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CorrectDocumentTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // HAND-CORRECTED: the scaffolder paired Requests.BillingDocumentType with
            // SourceDocumentTypeOcrSuggestion (a name-similarity guess) and then added
            // SourceDocumentType separately. That would have moved the user's classification into
            // the OCR-suggestion column. The rename below is the intended one; the OCR suggestion
            // is a genuinely new column and is added further down.
            migrationBuilder.RenameColumn(
                name: "BillingDocumentType",
                table: "Requests",
                newName: "SourceDocumentType");

            migrationBuilder.RenameColumn(
                name: "FinalInvoiceStatus",
                table: "RequestPoGroups",
                newName: "OperationInvoiceStatus");

            migrationBuilder.RenameColumn(
                name: "BillingDocumentType",
                table: "RequestPoGroups",
                newName: "SourceDocumentType");

            migrationBuilder.AddColumn<bool>(
                name: "ClassificationConflictAcknowledged",
                table: "Requests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ClassificationJustification",
                table: "Requests",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceDocumentTypeEvidenceJson",
                table: "Requests",
                type: "nvarchar(max)",
                nullable: true);

            // New column (see the note on the rename above) — what OCR proposed, never the choice.
            migrationBuilder.AddColumn<string>(
                name: "SourceDocumentTypeOcrSuggestion",
                table: "Requests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SourceDocumentTypeOcrConfidence",
                table: "Requests",
                type: "decimal(5,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceDocumentTypeSource",
                table: "Requests",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresAdvanceRegularization",
                table: "RequestPoGroups",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresFinanceClassificationReview",
                table: "RequestPoGroups",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresOperationInvoice",
                table: "RequestPoGroups",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresSeparateFiscalReceipt",
                table: "RequestPoGroups",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ClassificationConflictAcknowledged",
                table: "Quotations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ClassificationJustification",
                table: "Quotations",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentTypeEvidenceJson",
                table: "Quotations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DocumentTypeOcrConfidence",
                table: "Quotations",
                type: "decimal(5,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentTypeOcrSuggestion",
                table: "Quotations",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentTypeSource",
                table: "Quotations",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClassificationConflictAcknowledged",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "ClassificationJustification",
                table: "Requests");

            // HAND-CORRECTED: SourceDocumentType on Requests is a RENAMED column (reversed at the
            // end of this method), not a new one — dropping it here would destroy the
            // classification. The genuinely new column to drop is the OCR suggestion.
            migrationBuilder.DropColumn(
                name: "SourceDocumentTypeOcrSuggestion",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "SourceDocumentTypeEvidenceJson",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "SourceDocumentTypeOcrConfidence",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "SourceDocumentTypeSource",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "RequiresAdvanceRegularization",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "RequiresFinanceClassificationReview",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "RequiresOperationInvoice",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "RequiresSeparateFiscalReceipt",
                table: "RequestPoGroups");

            migrationBuilder.DropColumn(
                name: "ClassificationConflictAcknowledged",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "ClassificationJustification",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "DocumentTypeEvidenceJson",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "DocumentTypeOcrConfidence",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "DocumentTypeOcrSuggestion",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "DocumentTypeSource",
                table: "Quotations");

            // HAND-CORRECTED to mirror the corrected Up(): SourceDocumentType is the renamed
            // column, SourceDocumentTypeOcrSuggestion is dropped as a genuinely new column.
            migrationBuilder.RenameColumn(
                name: "SourceDocumentType",
                table: "Requests",
                newName: "BillingDocumentType");

            migrationBuilder.RenameColumn(
                name: "SourceDocumentType",
                table: "RequestPoGroups",
                newName: "BillingDocumentType");

            migrationBuilder.RenameColumn(
                name: "OperationInvoiceStatus",
                table: "RequestPoGroups",
                newName: "FinalInvoiceStatus");
        }
    }
}
