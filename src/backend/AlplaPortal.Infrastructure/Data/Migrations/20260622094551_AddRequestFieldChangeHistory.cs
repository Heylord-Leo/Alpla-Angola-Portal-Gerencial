using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestFieldChangeHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RequestFieldChangeHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FieldDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreviousValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StatusCodeAtChange = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LineItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestFieldChangeHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestFieldChangeHistories_RequestLineItems_LineItemId",
                        column: x => x.LineItemId,
                        principalTable: "RequestLineItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestFieldChangeHistories_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RequestFieldChangeHistories_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestFieldChangeHistories_ActorUserId",
                table: "RequestFieldChangeHistories",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestFieldChangeHistories_LineItemId",
                table: "RequestFieldChangeHistories",
                column: "LineItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestFieldChangeHistories_RequestId",
                table: "RequestFieldChangeHistories",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestFieldChangeHistories");
        }
    }
}
