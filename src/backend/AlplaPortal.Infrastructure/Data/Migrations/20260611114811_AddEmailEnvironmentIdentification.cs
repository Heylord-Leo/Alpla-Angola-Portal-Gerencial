using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailEnvironmentIdentification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: each column is only added if it does not already exist.
            var columns = new[]
            {
                ("AllowRealRecipientsInNonProduction", "bit", "NOT NULL DEFAULT 0"),
                ("EnableBodyWarningBanner",            "bit", "NOT NULL DEFAULT 0"),
                ("EnableSubjectPrefix",                "bit", "NOT NULL DEFAULT 0"),
                ("RedirectAllToTestRecipient",         "bit", "NOT NULL DEFAULT 0"),
                ("ShowOriginalRecipientsInBody",       "bit", "NOT NULL DEFAULT 0"),
                ("SubjectPrefixText",                  "nvarchar(max)", "NULL"),
                ("TestRecipientEmail",                 "nvarchar(max)", "NULL"),
                ("WarningBannerText",                  "nvarchar(max)", "NULL"),
            };

            foreach (var (name, type, constraint) in columns)
            {
                migrationBuilder.Sql($@"
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'SmtpSettings' AND COLUMN_NAME = '{name}'
)
BEGIN
    ALTER TABLE [SmtpSettings] ADD [{name}] {type} {constraint};
END
");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowRealRecipientsInNonProduction",
                table: "SmtpSettings");

            migrationBuilder.DropColumn(
                name: "EnableBodyWarningBanner",
                table: "SmtpSettings");

            migrationBuilder.DropColumn(
                name: "EnableSubjectPrefix",
                table: "SmtpSettings");

            migrationBuilder.DropColumn(
                name: "RedirectAllToTestRecipient",
                table: "SmtpSettings");

            migrationBuilder.DropColumn(
                name: "ShowOriginalRecipientsInBody",
                table: "SmtpSettings");

            migrationBuilder.DropColumn(
                name: "SubjectPrefixText",
                table: "SmtpSettings");

            migrationBuilder.DropColumn(
                name: "TestRecipientEmail",
                table: "SmtpSettings");

            migrationBuilder.DropColumn(
                name: "WarningBannerText",
                table: "SmtpSettings");
        }
    }
}
