using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Migrations
{
    public partial class RepairQuotationItemUnits : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 1: Fix LineNumber for legacy QuotationItems where it is 0
            // Only match if the Description is unambiguous for that Request.
            migrationBuilder.Sql(@"
                UPDATE qi
                SET qi.LineNumber = li.LineNumber
                FROM QuotationItems qi
                JOIN Quotations q ON qi.QuotationId = q.Id
                JOIN RequestLineItems li ON q.RequestId = li.RequestId AND qi.Description = li.Description
                WHERE qi.LineNumber = 0
                AND (
                    SELECT COUNT(*) 
                    FROM RequestLineItems li2 
                    WHERE li2.RequestId = q.RequestId 
                    AND li2.Description = qi.Description
                    AND li2.IsDeleted = 0
                ) = 1
                AND li.IsDeleted = 0;
            ");

            // Phase 2: Backfill UnitId via LineNumber (now that some 0s are fixed)
            migrationBuilder.Sql(@"
                UPDATE qi
                SET qi.UnitId = li.UnitId
                FROM QuotationItems qi
                JOIN Quotations q ON qi.QuotationId = q.Id
                JOIN RequestLineItems li ON q.RequestId = li.RequestId AND qi.LineNumber = li.LineNumber
                WHERE qi.UnitId IS NULL 
                AND li.UnitId IS NOT NULL
                AND li.IsDeleted = 0;
            ");

            // Phase 3: Fallback UnitId via Unambiguous Description matching
            // Useful if LineNumber still doesn't match for some reason
            migrationBuilder.Sql(@"
                UPDATE qi
                SET qi.UnitId = li.UnitId
                FROM QuotationItems qi
                JOIN Quotations q ON qi.QuotationId = q.Id
                JOIN RequestLineItems li ON q.RequestId = li.RequestId AND qi.Description = li.Description
                WHERE qi.UnitId IS NULL 
                AND li.UnitId IS NOT NULL
                AND (
                    SELECT COUNT(*) 
                    FROM RequestLineItems li2 
                    WHERE li2.RequestId = q.RequestId 
                    AND li2.Description = qi.Description
                    AND li2.IsDeleted = 0
                ) = 1
                AND li.IsDeleted = 0;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No reliable way to undo repairs, but usually not needed for data fixes.
        }
    }
}
