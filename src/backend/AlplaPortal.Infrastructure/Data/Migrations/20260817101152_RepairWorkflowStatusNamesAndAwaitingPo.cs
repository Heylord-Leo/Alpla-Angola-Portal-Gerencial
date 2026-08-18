using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <summary>
    /// v2.229.1 — DATA-ONLY migration (no schema change) fixing the two REQ-17/08/2026-232
    /// findings:
    ///
    /// <para>1. The long-orphaned PO_REQUESTED lookup (Id 11) becomes the request-level
    /// "awaiting first P.O." state, renamed "Aguardando P.O." (ASCII-safe through any
    /// transport). Requests parked in QUOTATION_COMPLETED by the old calculator defect —
    /// every non-cancelled group still WAITING_PO — are corrected to it.</para>
    ///
    /// <para>2. The three status names corrupted by the BOM-less-SQL/sqlcmd-ANSI transport
    /// defect are repaired via NCHAR()-constructed, idempotent UPDATEs
    /// (<see cref="WorkflowStatusRepairSql"/>) — deliberately encoding-proof, because the
    /// transport itself was the original corruption source.</para>
    /// </summary>
    public partial class RepairWorkflowStatusNamesAndAwaitingPo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 11,
                column: "Name",
                value: "Aguardando P.O.");

            foreach (var statement in WorkflowStatusRepairSql.All)
            {
                migrationBuilder.Sql(statement);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "RequestStatuses",
                keyColumn: "Id",
                keyValue: 11,
                column: "Name",
                value: "Solicitado P.O");

            // Deliberately conservative Down:
            // - The mojibake repairs are NOT reversed — restoring corrupted text is never a
            //   valid state, in any environment.
            // - The parked-request correction is NOT reversed — a later, legitimate
            //   PO_REQUESTED request is indistinguishable from a corrected one without an audit
            //   column this migration must not introduce; turning every PO_REQUESTED request
            //   back into QUOTATION_COMPLETED would corrupt organic rows.
        }
    }
}
