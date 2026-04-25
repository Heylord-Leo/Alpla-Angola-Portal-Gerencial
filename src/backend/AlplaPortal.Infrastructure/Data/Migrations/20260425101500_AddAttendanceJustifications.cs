using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceJustifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // HR Attendance Justifications — Portal-local table for manager/employee
            // attendance justification workflow. This does NOT write to Innux or Primavera.
            // Future: justification codes will be mapped to Primavera occurrence codes.
            migrationBuilder.CreateTable(
                name: "HRAttendanceJustifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    HREmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    JustificationCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    JustificationText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HRAttendanceJustifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HRAttendanceJustifications_HREmployees_HREmployeeId",
                        column: x => x.HREmployeeId,
                        principalTable: "HREmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HRAttendanceJustifications_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Index: employee + date for efficient lookup
            migrationBuilder.CreateIndex(
                name: "IX_HRAttendanceJustifications_HREmployeeId_Date",
                table: "HRAttendanceJustifications",
                columns: new[] { "HREmployeeId", "Date" });

            // Index: status for filtering pending approvals
            migrationBuilder.CreateIndex(
                name: "IX_HRAttendanceJustifications_Status",
                table: "HRAttendanceJustifications",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "HRAttendanceJustifications");
        }
    }
}
