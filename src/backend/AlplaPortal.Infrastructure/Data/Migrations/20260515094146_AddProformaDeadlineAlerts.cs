using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProformaDeadlineAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProformaDeadlineAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlertLevel = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailSent = table.Column<bool>(type: "bit", nullable: false),
                    InAppSent = table.Column<bool>(type: "bit", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProformaDeadlineAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProformaDeadlineAlerts_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProformaDeadlineAlerts_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProformaDeadlineAlerts_Dedup",
                table: "ProformaDeadlineAlerts",
                columns: new[] { "RequestId", "AlertLevel", "RecipientUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProformaDeadlineAlerts_RecipientUserId",
                table: "ProformaDeadlineAlerts",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProformaDeadlineAlerts_RequestId",
                table: "ProformaDeadlineAlerts",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProformaDeadlineAlerts");
        }
    }
}
