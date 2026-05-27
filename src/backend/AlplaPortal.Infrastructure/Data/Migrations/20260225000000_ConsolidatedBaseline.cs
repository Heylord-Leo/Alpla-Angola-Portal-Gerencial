using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AlplaPortal.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Consolidated baseline migration that creates all foundational tables.
    /// 
    /// This replaces the 41 deleted early migrations (20260225142152_InitialCreate through
    /// 20260331095909_AddResponsibleUserIdToDepartment) that were removed from the repository.
    /// 
    /// Schema source: 20260402135031_AddUserSecurityFields.Designer.cs model snapshot,
    /// which captures the complete model state BEFORE the 20260402 migration was applied
    /// (i.e., the cumulative result of all 41 deleted migrations).
    /// 
    /// IMPORTANT: This migration is designed to be safely skipped on databases that already
    /// have these tables (via __EFMigrationsHistory), and to fully create them on fresh databases.
    /// </summary>
    public partial class ConsolidatedBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ═══════════════════════════════════════════════════════════════
            // REFERENCE TABLES (no FK dependencies)
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.CreateTable(
                name: "AdminLogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Level = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExceptionDetail = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminLogEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CapexOpexClassifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapexOpexClassifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Currencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentExtractionSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: true),
                    DefaultProvider = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OpenAiEnabled = table.Column<bool>(type: "bit", nullable: true),
                    OpenAiModel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OpenAiTimeoutSeconds = table.Column<int>(type: "int", nullable: true),
                    LocalOcrEnabled = table.Column<bool>(type: "bit", nullable: true),
                    LocalOcrBaseUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LocalOcrTimeoutSeconds = table.Column<int>(type: "int", nullable: true),
                    AzureDocumentIntelligenceEnabled = table.Column<bool>(type: "bit", nullable: true),
                    AzureDocumentIntelligenceTimeoutSeconds = table.Column<int>(type: "int", nullable: true),
                    GlobalTimeoutSeconds = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentExtractionSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IvaRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RatePercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IvaRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LineItemStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BadgeColor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineItemStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Path = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    UserEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Exception = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NeedLevels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeedLevels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequestStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BadgeColor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequestTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TaxId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PortalCode = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PrimaveraCode = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemCounters",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CurrentValue = table.Column<int>(type: "int", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemCounters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Units",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AllowsDecimalQuantity = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Units", x => x.Id);
                });

            // ═══════════════════════════════════════════════════════════════
            // TABLES WITH FK DEPENDENCIES (Level 1)
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.CreateTable(
                name: "Plants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Plants_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ═══════════════════════════════════════════════════════════════
            // TABLES WITH FK DEPENDENCIES (Level 2 — depends on Plants)
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.CreateTable(
                name: "CostCenters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PlantId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostCenters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostCenters_Plants_PlantId",
                        column: x => x.PlantId,
                        principalTable: "Plants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Users table — depends on Departments, but Departments depends on Users (ResponsibleUserId).
            // Break the cycle: create Users first without the Department FK, then Departments, then add the FK.
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExternalId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ResponsibleUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Departments_Users_ResponsibleUserId",
                        column: x => x.ResponsibleUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Add Users → Departments FK now that Departments table exists
            migrationBuilder.CreateIndex(
                name: "IX_Users_DepartmentId",
                table: "Users",
                column: "DepartmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Departments_DepartmentId",
                table: "Users",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Notifications
            migrationBuilder.CreateTable(
                name: "InformationalNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    IsDismissed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InformationalNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InformationalNotifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationStatuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LastReadCount = table.Column<int>(type: "int", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationStatuses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ═══════════════════════════════════════════════════════════════
            // CORE BUSINESS TABLES
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.CreateTable(
                name: "Requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestTypeId = table.Column<int>(type: "int", nullable: false),
                    StatusId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    PlantId = table.Column<int>(type: "int", nullable: true),
                    RequesterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AreaApproverId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinalApproverId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentResponsibleUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentResponsibleRole = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CurrencyId = table.Column<int>(type: "int", nullable: true),
                    NeedLevelId = table.Column<int>(type: "int", nullable: true),
                    CapexOpexClassificationId = table.Column<int>(type: "int", nullable: true),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    SelectedQuotationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EstimatedTotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NeedByDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestedDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsCancelled = table.Column<bool>(type: "bit", nullable: false),
                    PrimaveraReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AlplaProdReference = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requests", x => x.Id);
                    table.ForeignKey(name: "FK_Requests_Users_AreaApproverId", column: x => x.AreaApproverId, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_Requests_Users_BuyerId", column: x => x.BuyerId, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_Requests_CapexOpexClassifications_CapexOpexClassificationId", column: x => x.CapexOpexClassificationId, principalTable: "CapexOpexClassifications", principalColumn: "Id");
                    table.ForeignKey(name: "FK_Requests_Companies_CompanyId", column: x => x.CompanyId, principalTable: "Companies", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_Requests_Currencies_CurrencyId", column: x => x.CurrencyId, principalTable: "Currencies", principalColumn: "Id");
                    table.ForeignKey(name: "FK_Requests_Departments_DepartmentId", column: x => x.DepartmentId, principalTable: "Departments", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_Requests_Users_FinalApproverId", column: x => x.FinalApproverId, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_Requests_NeedLevels_NeedLevelId", column: x => x.NeedLevelId, principalTable: "NeedLevels", principalColumn: "Id");
                    table.ForeignKey(name: "FK_Requests_Plants_PlantId", column: x => x.PlantId, principalTable: "Plants", principalColumn: "Id");
                    table.ForeignKey(name: "FK_Requests_RequestTypes_RequestTypeId", column: x => x.RequestTypeId, principalTable: "RequestTypes", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_Requests_Users_RequesterId", column: x => x.RequesterId, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_Requests_RequestStatuses_StatusId", column: x => x.StatusId, principalTable: "RequestStatuses", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_Requests_Suppliers_SupplierId", column: x => x.SupplierId, principalTable: "Suppliers", principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RequestAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FileExtension = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FileSizeMBytes = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: false),
                    StorageReference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AttachmentTypeCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestAttachments", x => x.Id);
                    table.ForeignKey(name: "FK_RequestAttachments_Requests_RequestId", column: x => x.RequestId, principalTable: "Requests", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_RequestAttachments_Users_UploadedByUserId", column: x => x.UploadedByUserId, principalTable: "Users", principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RequestStatusHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousStatusId = table.Column<int>(type: "int", nullable: true),
                    NewStatusId = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionTaken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestStatusHistories", x => x.Id);
                    table.ForeignKey(name: "FK_RequestStatusHistories_Users_ActorUserId", column: x => x.ActorUserId, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_RequestStatusHistories_RequestStatuses_NewStatusId", column: x => x.NewStatusId, principalTable: "RequestStatuses", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_RequestStatusHistories_RequestStatuses_PreviousStatusId", column: x => x.PreviousStatusId, principalTable: "RequestStatuses", principalColumn: "Id");
                    table.ForeignKey(name: "FK_RequestStatusHistories_Requests_RequestId", column: x => x.RequestId, principalTable: "Requests", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Quotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    SupplierNameSnapshot = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DocumentDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProformaAttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TotalGrossAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalTaxableBase = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalIvaAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalDiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsSelected = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotations", x => x.Id);
                    table.ForeignKey(name: "FK_Quotations_RequestAttachments_ProformaAttachmentId", column: x => x.ProformaAttachmentId, principalTable: "RequestAttachments", principalColumn: "Id");
                    table.ForeignKey(name: "FK_Quotations_Requests_RequestId", column: x => x.RequestId, principalTable: "Requests", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_Quotations_Suppliers_SupplierId", column: x => x.SupplierId, principalTable: "Suppliers", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RequestLineItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CurrencyId = table.Column<int>(type: "int", nullable: true),
                    CostCenterId = table.Column<int>(type: "int", nullable: false),
                    IvaRateId = table.Column<int>(type: "int", nullable: false),
                    PlantId = table.Column<int>(type: "int", nullable: true),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ItemPriority = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DivergenceNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LineItemStatusId = table.Column<int>(type: "int", nullable: true),
                    ReceivedQuantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestLineItems", x => x.Id);
                    table.ForeignKey(name: "FK_RequestLineItems_CostCenters_CostCenterId", column: x => x.CostCenterId, principalTable: "CostCenters", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_RequestLineItems_Currencies_CurrencyId", column: x => x.CurrencyId, principalTable: "Currencies", principalColumn: "Id");
                    table.ForeignKey(name: "FK_RequestLineItems_IvaRates_IvaRateId", column: x => x.IvaRateId, principalTable: "IvaRates", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_RequestLineItems_LineItemStatuses_LineItemStatusId", column: x => x.LineItemStatusId, principalTable: "LineItemStatuses", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RequestLineItems_Plants_PlantId", column: x => x.PlantId, principalTable: "Plants", principalColumn: "Id");
                    table.ForeignKey(name: "FK_RequestLineItems_Requests_RequestId", column: x => x.RequestId, principalTable: "Requests", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_RequestLineItems_Suppliers_SupplierId", column: x => x.SupplierId, principalTable: "Suppliers", principalColumn: "Id");
                    table.ForeignKey(name: "FK_RequestLineItems_Units_UnitId", column: x => x.UnitId, principalTable: "Units", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuotationItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuotationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrossSubtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IvaRateId = table.Column<int>(type: "int", nullable: true),
                    IvaRatePercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IvaAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineItemStatusId = table.Column<int>(type: "int", nullable: true),
                    DivergenceNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReceivedQuantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuotationItems", x => x.Id);
                    table.ForeignKey(name: "FK_QuotationItems_IvaRates_IvaRateId", column: x => x.IvaRateId, principalTable: "IvaRates", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_QuotationItems_LineItemStatuses_LineItemStatusId", column: x => x.LineItemStatusId, principalTable: "LineItemStatuses", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_QuotationItems_Quotations_QuotationId", column: x => x.QuotationId, principalTable: "Quotations", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_QuotationItems_Units_UnitId", column: x => x.UnitId, principalTable: "Units", principalColumn: "Id");
                });

            // ═══════════════════════════════════════════════════════════════
            // JUNCTION / SCOPE TABLES
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.CreateTable(
                name: "UserDepartmentScopes",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDepartmentScopes", x => new { x.UserId, x.DepartmentId });
                    table.ForeignKey(name: "FK_UserDepartmentScopes_Departments_DepartmentId", column: x => x.DepartmentId, principalTable: "Departments", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_UserDepartmentScopes_Users_UserId", column: x => x.UserId, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPlantScopes",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlantId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPlantScopes", x => new { x.UserId, x.PlantId });
                    table.ForeignKey(name: "FK_UserPlantScopes_Plants_PlantId", column: x => x.PlantId, principalTable: "Plants", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_UserPlantScopes_Users_UserId", column: x => x.UserId, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoleAssignments",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    DepartmentScopeId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoleAssignments", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(name: "FK_UserRoleAssignments_Departments_DepartmentScopeId", column: x => x.DepartmentScopeId, principalTable: "Departments", principalColumn: "Id");
                    table.ForeignKey(name: "FK_UserRoleAssignments_Roles_RoleId", column: x => x.RoleId, principalTable: "Roles", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_UserRoleAssignments_Users_UserId", column: x => x.UserId, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });

            // ═══════════════════════════════════════════════════════════════
            // INDEXES
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.CreateIndex(name: "IX_Currencies_Code", table: "Currencies", column: "Code", unique: true);
            migrationBuilder.CreateIndex(name: "IX_CostCenters_Code", table: "CostCenters", column: "Code", unique: true);
            migrationBuilder.CreateIndex(name: "IX_CostCenters_PlantId", table: "CostCenters", column: "PlantId");
            migrationBuilder.CreateIndex(name: "IX_Departments_ResponsibleUserId", table: "Departments", column: "ResponsibleUserId");
            migrationBuilder.CreateIndex(name: "IX_InformationalNotifications_UserId", table: "InformationalNotifications", column: "UserId");
            migrationBuilder.CreateIndex(name: "IX_LineItemStatuses_Code", table: "LineItemStatuses", column: "Code", unique: true);
            migrationBuilder.CreateIndex(name: "IX_NeedLevels_Code", table: "NeedLevels", column: "Code", unique: true);
            migrationBuilder.CreateIndex(name: "IX_NotificationStatuses_UserId_Category", table: "NotificationStatuses", columns: new[] { "UserId", "Category" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_Plants_CompanyId", table: "Plants", column: "CompanyId");
            migrationBuilder.CreateIndex(name: "IX_Quotations_ProformaAttachmentId", table: "Quotations", column: "ProformaAttachmentId");
            migrationBuilder.CreateIndex(name: "IX_Quotations_RequestId", table: "Quotations", column: "RequestId");
            migrationBuilder.CreateIndex(name: "IX_Quotations_SupplierId", table: "Quotations", column: "SupplierId");
            migrationBuilder.CreateIndex(name: "IX_QuotationItems_IvaRateId", table: "QuotationItems", column: "IvaRateId");
            migrationBuilder.CreateIndex(name: "IX_QuotationItems_LineItemStatusId", table: "QuotationItems", column: "LineItemStatusId");
            migrationBuilder.CreateIndex(name: "IX_QuotationItems_QuotationId", table: "QuotationItems", column: "QuotationId");
            migrationBuilder.CreateIndex(name: "IX_QuotationItems_UnitId", table: "QuotationItems", column: "UnitId");
            migrationBuilder.CreateIndex(name: "IX_Requests_AreaApproverId", table: "Requests", column: "AreaApproverId");
            migrationBuilder.CreateIndex(name: "IX_Requests_BuyerId", table: "Requests", column: "BuyerId");
            migrationBuilder.CreateIndex(name: "IX_Requests_CapexOpexClassificationId", table: "Requests", column: "CapexOpexClassificationId");
            migrationBuilder.CreateIndex(name: "IX_Requests_CompanyId", table: "Requests", column: "CompanyId");
            migrationBuilder.CreateIndex(name: "IX_Requests_CreatedAtUtc", table: "Requests", column: "CreatedAtUtc");
            migrationBuilder.CreateIndex(name: "IX_Requests_CurrencyId", table: "Requests", column: "CurrencyId");
            migrationBuilder.CreateIndex(name: "IX_Requests_DepartmentId", table: "Requests", column: "DepartmentId");
            migrationBuilder.CreateIndex(name: "IX_Requests_FinalApproverId", table: "Requests", column: "FinalApproverId");
            migrationBuilder.CreateIndex(name: "IX_Requests_NeedLevelId", table: "Requests", column: "NeedLevelId");
            migrationBuilder.CreateIndex(name: "IX_Requests_PlantId", table: "Requests", column: "PlantId");
            migrationBuilder.CreateIndex(name: "IX_Requests_RequestNumber", table: "Requests", column: "RequestNumber", unique: true, filter: "[RequestNumber] IS NOT NULL");
            migrationBuilder.CreateIndex(name: "IX_Requests_RequestTypeId", table: "Requests", column: "RequestTypeId");
            migrationBuilder.CreateIndex(name: "IX_Requests_RequesterId", table: "Requests", column: "RequesterId");
            migrationBuilder.CreateIndex(name: "IX_Requests_StatusId", table: "Requests", column: "StatusId");
            migrationBuilder.CreateIndex(name: "IX_Requests_SupplierId", table: "Requests", column: "SupplierId");
            migrationBuilder.CreateIndex(name: "IX_RequestAttachments_RequestId", table: "RequestAttachments", column: "RequestId");
            migrationBuilder.CreateIndex(name: "IX_RequestAttachments_UploadedByUserId", table: "RequestAttachments", column: "UploadedByUserId");
            migrationBuilder.CreateIndex(name: "IX_RequestLineItems_CostCenterId", table: "RequestLineItems", column: "CostCenterId");
            migrationBuilder.CreateIndex(name: "IX_RequestLineItems_CurrencyId", table: "RequestLineItems", column: "CurrencyId");
            migrationBuilder.CreateIndex(name: "IX_RequestLineItems_IvaRateId", table: "RequestLineItems", column: "IvaRateId");
            migrationBuilder.CreateIndex(name: "IX_RequestLineItems_LineItemStatusId", table: "RequestLineItems", column: "LineItemStatusId");
            migrationBuilder.CreateIndex(name: "IX_RequestLineItems_PlantId", table: "RequestLineItems", column: "PlantId");
            migrationBuilder.CreateIndex(name: "IX_RequestLineItems_RequestId", table: "RequestLineItems", column: "RequestId");
            migrationBuilder.CreateIndex(name: "IX_RequestLineItems_SupplierId", table: "RequestLineItems", column: "SupplierId");
            migrationBuilder.CreateIndex(name: "IX_RequestLineItems_UnitId", table: "RequestLineItems", column: "UnitId");
            migrationBuilder.CreateIndex(name: "IX_RequestLineItems_RequestId_IsDeleted", table: "RequestLineItems", columns: new[] { "RequestId", "IsDeleted" });
            migrationBuilder.CreateIndex(name: "IX_RequestStatuses_Code", table: "RequestStatuses", column: "Code", unique: true);
            migrationBuilder.CreateIndex(name: "IX_RequestStatuses_DisplayOrder", table: "RequestStatuses", column: "DisplayOrder", unique: true);
            migrationBuilder.CreateIndex(name: "IX_RequestStatusHistories_ActorUserId", table: "RequestStatusHistories", column: "ActorUserId");
            migrationBuilder.CreateIndex(name: "IX_RequestStatusHistories_NewStatusId", table: "RequestStatusHistories", column: "NewStatusId");
            migrationBuilder.CreateIndex(name: "IX_RequestStatusHistories_PreviousStatusId", table: "RequestStatusHistories", column: "PreviousStatusId");
            migrationBuilder.CreateIndex(name: "IX_RequestStatusHistories_RequestId", table: "RequestStatusHistories", column: "RequestId");
            migrationBuilder.CreateIndex(name: "IX_RequestTypes_Code", table: "RequestTypes", column: "Code", unique: true);
            migrationBuilder.CreateIndex(name: "IX_Suppliers_Name", table: "Suppliers", column: "Name", unique: true);
            migrationBuilder.CreateIndex(name: "IX_Suppliers_PortalCode", table: "Suppliers", column: "PortalCode", unique: true, filter: "[PortalCode] IS NOT NULL");
            migrationBuilder.CreateIndex(name: "IX_Suppliers_PrimaveraCode", table: "Suppliers", column: "PrimaveraCode", unique: true, filter: "[PrimaveraCode] IS NOT NULL AND [PrimaveraCode] <> ''");
            migrationBuilder.CreateIndex(name: "IX_Units_Code", table: "Units", column: "Code", unique: true);
            migrationBuilder.CreateIndex(name: "IX_UserDepartmentScopes_DepartmentId", table: "UserDepartmentScopes", column: "DepartmentId");
            migrationBuilder.CreateIndex(name: "IX_UserPlantScopes_PlantId", table: "UserPlantScopes", column: "PlantId");
            migrationBuilder.CreateIndex(name: "IX_UserRoleAssignments_DepartmentScopeId", table: "UserRoleAssignments", column: "DepartmentScopeId");
            migrationBuilder.CreateIndex(name: "IX_UserRoleAssignments_RoleId", table: "UserRoleAssignments", column: "RoleId");

            // ═══════════════════════════════════════════════════════════════
            // SEED DATA (from EF Core HasData in model snapshot)
            // ═══════════════════════════════════════════════════════════════

            // CapexOpexClassifications
            migrationBuilder.InsertData(table: "CapexOpexClassifications", columns: new[] { "Id", "Name", "IsActive" }, values: new object[,] { { 1, "CAPEX", true }, { 2, "OPEX", true } });

            // Companies
            migrationBuilder.InsertData(table: "Companies", columns: new[] { "Id", "Name", "IsActive" }, values: new object[,] { { 1, "AlplaPLASTICO", true }, { 2, "AlplaSOPRO", true } });

            // Currencies
            migrationBuilder.InsertData(table: "Currencies", columns: new[] { "Id", "Code", "Symbol", "IsActive" }, values: new object[,] { { 1, "AOA", "Kz", true }, { 2, "USD", "$", true }, { 3, "EUR", "€", true } });

            // IvaRates
            migrationBuilder.InsertData(table: "IvaRates", columns: new[] { "Id", "Code", "Name", "RatePercent", "IsActive", "DisplayOrder" }, values: new object[,]
            {
                { 1, "IVA_14", "IVA 14%", 14.0m, true, 1 },
                { 2, "IVA_7", "IVA 7%", 7.0m, true, 2 },
                { 3, "IVA_5", "IVA 5%", 5.0m, true, 3 },
                { 4, "IVA_3", "IVA 3%", 3.0m, true, 4 },
                { 5, "IVA_0", "Isento (0%)", 0.0m, true, 5 }
            });

            // LineItemStatuses
            migrationBuilder.InsertData(table: "LineItemStatuses", columns: new[] { "Id", "Code", "Name", "BadgeColor", "DisplayOrder", "IsActive" }, values: new object[,]
            {
                { 1, "WAITING_QUOTATION", "Aguardando Cotação", "blue", 1, true },
                { 2, "PENDING", "Pendente", "yellow", 2, true },
                { 3, "UNDER_REVIEW", "Em Análise", "indigo", 3, true },
                { 8, "WAITING_ORDER", "Aguardando Encomenda", "slate", 4, true },
                { 4, "ORDERED", "Encomendado", "cyan", 5, true },
                { 5, "PARTIALLY_RECEIVED", "Recebido Parcial", "orange", 6, true },
                { 6, "RECEIVED", "Recebido", "green", 7, true },
                { 7, "CANCELLED", "Cancelado", "red", 8, true }
            });

            // NeedLevels
            migrationBuilder.InsertData(table: "NeedLevels", columns: new[] { "Id", "Code", "Name", "IsActive" }, values: new object[,]
            {
                { 1, "BAIXO", "Baixo", true },
                { 2, "NORMAL", "Normal", true },
                { 3, "URGENTE", "Urgente", true },
                { 4, "CRITICO", "Crítico", true }
            });

            // RequestStatuses (21 statuses from model snapshot — note: non-sequential IDs)
            migrationBuilder.InsertData(table: "RequestStatuses", columns: new[] { "Id", "Code", "Name", "BadgeColor", "DisplayOrder", "IsActive" }, values: new object[,]
            {
                { 1, "DRAFT", "Rascunho", "gray", 1, true },
                { 19, "SUBMITTED", "Submetido", "cyan", 2, true },
                { 2, "WAITING_QUOTATION", "Aguardando Cotação", "blue", 3, true },
                { 3, "WAITING_AREA_APPROVAL", "Aguardando Aprovação da Área", "indigo", 4, true },
                { 4, "AREA_ADJUSTMENT", "Reajuste A.A", "orange", 5, true },
                { 5, "WAITING_FINAL_APPROVAL", "Aguardando Aprovação Final", "purple", 6, true },
                { 6, "FINAL_ADJUSTMENT", "Reajuste A.F", "teal", 7, true },
                { 7, "REJECTED", "Rejeitado", "red", 8, true },
                { 8, "WAITING_COST_CENTER", "Inserir C.C", "yellow", 9, true },
                { 9, "APPROVED", "Aprovado", "green", 10, true },
                { 10, "PROFORMA_INVOICE_INSERTED", "Fatura Proforma Inserida", "slate", 11, true },
                { 11, "PO_REQUESTED", "Solicitado P.O", "sky", 12, true },
                { 12, "PO_ISSUED", "P.O Emitida", "lime", 13, true },
                { 13, "PAYMENT_REQUEST_SENT", "Solicitação Pagamento Enviada", "rose", 14, true },
                { 14, "PAYMENT_SCHEDULED", "Pagamento Agendado", "violet", 15, true },
                { 15, "PAYMENT_COMPLETED", "Pagamento Realizado", "fuchsia", 16, true },
                { 16, "WAITING_RECEIPT", "Aguardando Recibo", "stone", 17, true },
                { 21, "IN_FOLLOWUP", "Em Acompanhamento", "amber", 18, true },
                { 17, "COMPLETED", "Finalizado", "carbon", 19, true },
                { 18, "CANCELLED", "Cancelado", "zinc", 20, true },
                { 20, "QUOTATION_COMPLETED", "Cotação Concluída", "emerald", 21, false }
            });

            // RequestTypes
            migrationBuilder.InsertData(table: "RequestTypes", columns: new[] { "Id", "Code", "Name", "IsActive" }, values: new object[,]
            {
                { 1, "QUOTATION", "Cotação", true },
                { 2, "PAYMENT", "Pagamento", true }
            });

            // Roles (11 roles)
            migrationBuilder.InsertData(table: "Roles", columns: new[] { "Id", "RoleName" }, values: new object[,]
            {
                { 1, "System Administrator" },
                { 2, "Local Manager" },
                { 3, "Requester" },
                { 4, "Buyer" },
                { 5, "Area Approver" },
                { 6, "Final Approver" },
                { 7, "Finance" },
                { 8, "Receiving" },
                { 9, "Contracts" },
                { 10, "Import" },
                { 11, "Viewer / Management" }
            });

            // Suppliers
            migrationBuilder.InsertData(table: "Suppliers", columns: new[] { "Id", "Name", "IsActive", "PortalCode", "TaxId", "PrimaveraCode" }, values: new object[,]
            {
                { 1, "Alpla Global Services", true, "SUP-000001", null, null },
                { 2, "Standard Supplier 01", true, "SUP-000002", null, null }
            });

            // Units
            migrationBuilder.InsertData(table: "Units", columns: new[] { "Id", "Code", "Name", "IsActive", "AllowsDecimalQuantity" }, values: new object[,]
            {
                { 1, "UN", "Unidade", true, false },
                { 2, "EA", "Each", true, false },
                { 3, "KG", "Quilograma", true, true },
                { 4, "L", "Litro", true, true },
                { 5, "M", "Metro", true, true },
                { 6, "CX", "Caixa", true, false }
            });

            // Plants (depends on Companies)
            migrationBuilder.InsertData(table: "Plants", columns: new[] { "Id", "Code", "Name", "IsActive", "CompanyId" }, values: new object[,]
            {
                { 1, "V1", "Viana 1", true, 1 },
                { 2, "V2", "Viana 2", true, 1 },
                { 3, "V3", "Viana 3", true, 2 }
            });

            // CostCenters (depends on Plants)
            migrationBuilder.InsertData(table: "CostCenters", columns: new[] { "Id", "Code", "Name", "IsActive", "PlantId" }, values: new object[,]
            {
                { 1, "PET1", "PET 1", true, 1 },
                { 2, "CAPS1", "CAPS 1", true, 1 },
                { 3, "PET2", "PET 2", true, 2 },
                { 4, "CAPS2", "CAPS 2", true, 2 },
                { 5, "SBM", "SBM", true, 3 }
            });

            // Departments
            migrationBuilder.InsertData(table: "Departments", columns: new[] { "Id", "Code", "Name", "IsActive", "ResponsibleUserId" }, values: new object[,]
            {
                { 1, "ADM", "Admin", true, null },
                { 2, "FIN", "Financeiro", true, null },
                { 3, "LOG", "Logística", true, null },
                { 4, "TI", "TI", true, null }
            });

            // NOTE: Seed users are NOT included in this consolidated baseline.
            // The original migrations seeded development/test users with hardcoded password hashes.
            // For production safety, admin users should be created through the application's
            // user management interface or a separate admin seed script.
            // See: docs/ADMIN_USER_SEED_TEMPLATE.sql
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop in reverse dependency order
            migrationBuilder.DropTable(name: "QuotationItems");
            migrationBuilder.DropTable(name: "RequestLineItems");
            migrationBuilder.DropTable(name: "RequestStatusHistories");
            migrationBuilder.DropTable(name: "RequestAttachments");
            migrationBuilder.DropTable(name: "UserRoleAssignments");
            migrationBuilder.DropTable(name: "UserPlantScopes");
            migrationBuilder.DropTable(name: "UserDepartmentScopes");
            migrationBuilder.DropTable(name: "NotificationStatuses");
            migrationBuilder.DropTable(name: "InformationalNotifications");
            migrationBuilder.DropTable(name: "Quotations");
            migrationBuilder.DropTable(name: "Requests");
            migrationBuilder.DropTable(name: "CostCenters");
            migrationBuilder.DropTable(name: "Departments");
            migrationBuilder.DropTable(name: "Users");
            migrationBuilder.DropTable(name: "Plants");
            migrationBuilder.DropTable(name: "Companies");
            migrationBuilder.DropTable(name: "RequestStatuses");
            migrationBuilder.DropTable(name: "RequestTypes");
            migrationBuilder.DropTable(name: "Roles");
            migrationBuilder.DropTable(name: "Suppliers");
            migrationBuilder.DropTable(name: "SystemCounters");
            migrationBuilder.DropTable(name: "IvaRates");
            migrationBuilder.DropTable(name: "LineItemStatuses");
            migrationBuilder.DropTable(name: "NeedLevels");
            migrationBuilder.DropTable(name: "Currencies");
            migrationBuilder.DropTable(name: "Units");
            migrationBuilder.DropTable(name: "CapexOpexClassifications");
            migrationBuilder.DropTable(name: "AdminLogEntries");
            migrationBuilder.DropTable(name: "LogEntries");
            migrationBuilder.DropTable(name: "DocumentExtractionSettings");
        }
    }
}
