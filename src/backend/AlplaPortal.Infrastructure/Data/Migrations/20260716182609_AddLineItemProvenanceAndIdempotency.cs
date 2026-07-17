using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLineItemProvenanceAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreationIdempotencyKey",
                table: "RequestLineItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreationOrigin",
                table: "RequestLineItems",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceProformaAttachmentId",
                table: "RequestLineItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestLineItems_RequestId_CreationIdempotencyKey",
                table: "RequestLineItems",
                columns: new[] { "RequestId", "CreationIdempotencyKey" },
                unique: true,
                filter: "[CreationIdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLineItems_RequestId_SourceProformaAttachmentId",
                table: "RequestLineItems",
                columns: new[] { "RequestId", "SourceProformaAttachmentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RequestLineItems_RequestId_CreationIdempotencyKey",
                table: "RequestLineItems");

            migrationBuilder.DropIndex(
                name: "IX_RequestLineItems_RequestId_SourceProformaAttachmentId",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "CreationIdempotencyKey",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "CreationOrigin",
                table: "RequestLineItems");

            migrationBuilder.DropColumn(
                name: "SourceProformaAttachmentId",
                table: "RequestLineItems");
        }
    }
}
