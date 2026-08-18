using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <summary>
    /// v2.229.3 — DATA-ONLY migration (no schema change) repairing the REQ-17/08/2026-232
    /// Receiving-handoff defect: confirmed advances used to park their groups (and the parent
    /// projection) in ADVANCE_PAYMENT_COMPLETED because the transition to
    /// WAITING_SUPPLIER_DELIVERY never existed, leaving them invisible to the Receiving
    /// workspace and rejected by every receiving endpoint.
    ///
    /// <para>Statements live in <see cref="AdvanceDeliveryRepairSql"/> (shared with the
    /// integration tests): groups matching the exact defect shape move to
    /// WAITING_SUPPLIER_DELIVERY; parents parked in ADVANCE_PAYMENT_COMPLETED follow only when
    /// the calculator's furthest-behind reading agrees. Idempotent; later-stage, cancelled,
    /// receipted and reconciliation-touched rows are excluded by construction.</para>
    /// </summary>
    public partial class HandoffParkedAdvancePaidGroupsToDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var statement in AdvanceDeliveryRepairSql.All)
            {
                migrationBuilder.Sql(statement);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately conservative no-op: corrected rows are indistinguishable from
            // organic WAITING_SUPPLIER_DELIVERY rows (the state every confirmed advance now
            // reaches), and no audit column exists — nor should one — to mark migration
            // participation. Reverting would corrupt organic rows.
        }
    }
}
