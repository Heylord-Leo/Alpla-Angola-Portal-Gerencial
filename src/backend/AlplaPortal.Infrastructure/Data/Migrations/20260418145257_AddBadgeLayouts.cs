using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBadgeLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BadgeLayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LayoutConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompanyCode = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    BadgeType = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PlantCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BadgeLayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BadgeLayouts_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BadgeLayouts_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BadgePrintHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EmployeeName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Department = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CardNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompanyCode = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PlantCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhotoSource = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PhotoReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SnapshotPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BadgeLayoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PrintedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrintedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PrintCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BadgePrintHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BadgePrintHistories_BadgeLayouts_BadgeLayoutId",
                        column: x => x.BadgeLayoutId,
                        principalTable: "BadgeLayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BadgePrintHistories_Users_PrintedByUserId",
                        column: x => x.PrintedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BadgePrintEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BadgePrintHistoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReprintedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReprintedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BadgePrintEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BadgePrintEvents_BadgePrintHistories_BadgePrintHistoryId",
                        column: x => x.BadgePrintHistoryId,
                        principalTable: "BadgePrintHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BadgePrintEvents_Users_ReprintedByUserId",
                        column: x => x.ReprintedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BadgeLayouts_CompanyCode_BadgeType_Status",
                table: "BadgeLayouts",
                columns: new[] { "CompanyCode", "BadgeType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BadgeLayouts_CreatedByUserId",
                table: "BadgeLayouts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BadgeLayouts_Name_Version",
                table: "BadgeLayouts",
                columns: new[] { "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BadgeLayouts_Status",
                table: "BadgeLayouts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BadgeLayouts_UpdatedByUserId",
                table: "BadgeLayouts",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BadgePrintEvents_BadgePrintHistoryId",
                table: "BadgePrintEvents",
                column: "BadgePrintHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_BadgePrintEvents_ReprintedAtUtc",
                table: "BadgePrintEvents",
                column: "ReprintedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BadgePrintEvents_ReprintedByUserId",
                table: "BadgePrintEvents",
                column: "ReprintedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BadgePrintHistories_BadgeLayoutId",
                table: "BadgePrintHistories",
                column: "BadgeLayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_BadgePrintHistories_CompanyCode",
                table: "BadgePrintHistories",
                column: "CompanyCode");

            migrationBuilder.CreateIndex(
                name: "IX_BadgePrintHistories_EmployeeCode",
                table: "BadgePrintHistories",
                column: "EmployeeCode");

            migrationBuilder.CreateIndex(
                name: "IX_BadgePrintHistories_PrintedAtUtc",
                table: "BadgePrintHistories",
                column: "PrintedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BadgePrintHistories_PrintedByUserId",
                table: "BadgePrintHistories",
                column: "PrintedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BadgePrintEvents");

            migrationBuilder.DropTable(
                name: "BadgePrintHistories");

            migrationBuilder.DropTable(
                name: "BadgeLayouts");
        }
    }
}
