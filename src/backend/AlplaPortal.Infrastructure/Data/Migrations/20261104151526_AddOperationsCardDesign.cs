using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationsCardDesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperationsCardDesigns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BoardKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    DesignJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastSaveToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationsCardDesigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationsCardDesigns_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OperationsDesignAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoardKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    StorageReference = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PurgeAttempts = table.Column<int>(type: "int", nullable: false),
                    LastPurgeAttemptUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastPurgeError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationsDesignAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationsDesignAssets_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationsCardDesigns_UpdatedByUserId",
                table: "OperationsCardDesigns",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_OperationsCardDesigns_Board_Machine",
                table: "OperationsCardDesigns",
                columns: new[] { "BoardKey", "MachineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationsDesignAssets_Board_Deleted",
                table: "OperationsDesignAssets",
                columns: new[] { "BoardKey", "DeletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationsDesignAssets_CreatedByUserId",
                table: "OperationsDesignAssets",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_OperationsDesignAssets_Board_Sha256_Active",
                table: "OperationsDesignAssets",
                columns: new[] { "BoardKey", "Sha256" },
                unique: true,
                filter: "[DeletedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationsCardDesigns");

            migrationBuilder.DropTable(
                name: "OperationsDesignAssets");
        }
    }
}
