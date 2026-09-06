using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalStageTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperationalStageStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Domain = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StageCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StageEnteredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IsBackfilled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalStageStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperationalStageTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Domain = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FromStageCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ToStageCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TransitionSource = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalStageTransitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalStageState_Domain_Stage",
                table: "OperationalStageStates",
                columns: new[] { "Domain", "StageCode" })
                .Annotation("SqlServer:Include", new[] { "StageEnteredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalStageState_RequestId",
                table: "OperationalStageStates",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "UX_OperationalStageState_Entity",
                table: "OperationalStageStates",
                columns: new[] { "EntityType", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationalStageTransition_Entity_Occurred",
                table: "OperationalStageTransitions",
                columns: new[] { "EntityType", "EntityId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalStageTransition_RequestId",
                table: "OperationalStageTransitions",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationalStageStates");

            migrationBuilder.DropTable(
                name: "OperationalStageTransitions");
        }
    }
}
