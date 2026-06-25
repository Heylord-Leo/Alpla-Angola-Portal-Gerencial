using AlplaPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace AlplaPortal.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();
    public DbSet<UserPlantScope> UserPlantScopes => Set<UserPlantScope>();
    public DbSet<UserDepartmentScope> UserDepartmentScopes => Set<UserDepartmentScope>();

    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<AnnualBudget> AnnualBudgets => Set<AnnualBudget>();

    public DbSet<RequestType> RequestTypes => Set<RequestType>();
    public DbSet<RequestStatus> RequestStatuses => Set<RequestStatus>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<CapexOpexClassification> CapexOpexClassifications => Set<CapexOpexClassification>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<NeedLevel> NeedLevels => Set<NeedLevel>();
    public DbSet<LineItemStatus> LineItemStatuses => Set<LineItemStatus>();
    public DbSet<IvaRate> IvaRates => Set<IvaRate>();

    public DbSet<Request> Requests => Set<Request>();
    public DbSet<RequestLineItem> RequestLineItems => Set<RequestLineItem>();
    public DbSet<RequestStatusHistory> RequestStatusHistories => Set<RequestStatusHistory>();
    public DbSet<RequestFieldChangeHistory> RequestFieldChangeHistories => Set<RequestFieldChangeHistory>();
    public DbSet<RequestAttachment> RequestAttachments => Set<RequestAttachment>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<QuotationItem> QuotationItems => Set<QuotationItem>();
    public DbSet<SystemCounter> SystemCounters => Set<SystemCounter>();
    public DbSet<DocumentExtractionSettings> DocumentExtractionSettings => Set<DocumentExtractionSettings>();
    public DbSet<OcrModuleConfig> OcrModuleConfigs => Set<OcrModuleConfig>();
    public DbSet<SmtpSettings> SmtpSettings => Set<SmtpSettings>();
    public DbSet<LogEntry> LogEntries => Set<LogEntry>();
    public DbSet<AdminLogEntry> AdminLogEntries => Set<AdminLogEntry>();
    public DbSet<NotificationStatus> NotificationStatuses => Set<NotificationStatus>();
    public DbSet<InformationalNotification> InformationalNotifications => Set<InformationalNotification>();
    public DbSet<ItemCatalog> ItemCatalogItems => Set<ItemCatalog>();
    public DbSet<OcrExtractedItem> OcrExtractedItems => Set<OcrExtractedItem>();
    public DbSet<ReconciliationRecord> ReconciliationRecords => Set<ReconciliationRecord>();

    // Integration Foundation (Phase 0)
    public DbSet<IntegrationProvider> IntegrationProviders => Set<IntegrationProvider>();
    public DbSet<IntegrationConnectionStatus> IntegrationConnectionStatuses => Set<IntegrationConnectionStatus>();
    public DbSet<IntegrationProviderSettings> IntegrationProviderSettings => Set<IntegrationProviderSettings>();

    // HR Leave Module (Phase 1)
    public DbSet<HREmployee> HREmployees => Set<HREmployee>();
    public DbSet<DepartmentMaster> DepartmentMasters => Set<DepartmentMaster>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveRecord> LeaveRecords => Set<LeaveRecord>();
    public DbSet<LeaveStatusHistory> LeaveStatusHistories => Set<LeaveStatusHistory>();
    public DbSet<HRSyncLog> HRSyncLogs => Set<HRSyncLog>();

    // Badge Management Module
    public DbSet<BadgeLayout> BadgeLayouts => Set<BadgeLayout>();
    public DbSet<BadgePrintHistory> BadgePrintHistories => Set<BadgePrintHistory>();
    public DbSet<BadgePrintEvent> BadgePrintEvents => Set<BadgePrintEvent>();

    // Contracts Management Module
    public DbSet<ContractType> ContractTypes => Set<ContractType>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractDocument> ContractDocuments => Set<ContractDocument>();
    public DbSet<ContractHistory> ContractHistories => Set<ContractHistory>();
    public DbSet<ContractAlert> ContractAlerts => Set<ContractAlert>();
    public DbSet<ContractPaymentObligation> ContractPaymentObligations => Set<ContractPaymentObligation>();

    // Contracts OCR (Phase 1)
    public DbSet<ContractOcrExtractionRecord> ContractOcrExtractionRecords => Set<ContractOcrExtractionRecord>();
    public DbSet<ContractOcrExtractedField> ContractOcrExtractedFields => Set<ContractOcrExtractedField>();

    // HR Monthly Changes Middleware (Innux → Portal → Primavera)
    public DbSet<MCProcessingRun> MCProcessingRuns => Set<MCProcessingRun>();
    public DbSet<MCAttendanceSnapshot> MCAttendanceSnapshots => Set<MCAttendanceSnapshot>();
    public DbSet<MCMonthlyChangeItem> MCMonthlyChangeItems => Set<MCMonthlyChangeItem>();
    public DbSet<MCPrimaveraCodeMapping> MCPrimaveraCodeMappings => Set<MCPrimaveraCodeMapping>();
    public DbSet<MCDetectionThreshold> MCDetectionThresholds => Set<MCDetectionThreshold>();
    public DbSet<MCExportBatch> MCExportBatches => Set<MCExportBatch>();
    public DbSet<MCExportRow> MCExportRows => Set<MCExportRow>();
    public DbSet<MCProcessingLog> MCProcessingLogs => Set<MCProcessingLog>();

    // Supplier Registration (Ficha de Fornecedor)
    public DbSet<SupplierDocument> SupplierDocuments => Set<SupplierDocument>();
    public DbSet<SupplierStatusHistory> SupplierStatusHistories => Set<SupplierStatusHistory>();

    // Proforma Deadline Alerts (audit / dedup)
    public DbSet<ProformaDeadlineAlert> ProformaDeadlineAlerts => Set<ProformaDeadlineAlert>();

    // I.T Equipment Module
    public DbSet<ITEquipment> ITEquipments => Set<ITEquipment>();
    public DbSet<ITEquipmentAssignment> ITEquipmentAssignments => Set<ITEquipmentAssignment>();
    public DbSet<ITEquipmentMovementLog> ITEquipmentMovementLogs => Set<ITEquipmentMovementLog>();
    public DbSet<ITEquipmentAcquisition> ITEquipmentAcquisitions => Set<ITEquipmentAcquisition>();
    public DbSet<ITEquipmentDocument> ITEquipmentDocuments => Set<ITEquipmentDocument>();
    public DbSet<ITEquipmentType> ITEquipmentTypes => Set<ITEquipmentType>();
    public DbSet<ITEquipmentDeliveryTerm> ITEquipmentDeliveryTerms => Set<ITEquipmentDeliveryTerm>();
    public DbSet<ITEquipmentDeliveryItem> ITEquipmentDeliveryItems => Set<ITEquipmentDeliveryItem>();
    public DbSet<ITEquipmentManufacturer> ITEquipmentManufacturers => Set<ITEquipmentManufacturer>();
    public DbSet<ITEquipmentModel> ITEquipmentModels => Set<ITEquipmentModel>();
    public DbSet<ITEquipmentProcessor> ITEquipmentProcessors => Set<ITEquipmentProcessor>();
    public DbSet<ITEquipmentMemoryOption> ITEquipmentMemoryOptions => Set<ITEquipmentMemoryOption>();

    // Accounts Payable Notification System
    public DbSet<AccountsPayableNotificationConfig> AccountsPayableNotificationConfigs => Set<AccountsPayableNotificationConfig>();
    public DbSet<AccountsPayableNotificationLog> AccountsPayableNotificationLogs => Set<AccountsPayableNotificationLog>();

    // Buy-to-Pay (Advance Payment / Reconciliation)
    public DbSet<RequestPayment> RequestPayments => Set<RequestPayment>();
    public DbSet<RequestReconciliation> RequestReconciliations => Set<RequestReconciliation>();

    // Email Outbox (async email delivery queue)
    public DbSet<EmailOutboxEntry> EmailOutbox => Set<EmailOutboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply Configurations (like Request mapping)
        modelBuilder.Entity<OcrModuleConfig>().HasData(
            new OcrModuleConfig { Id = 1, ModuleKey = "REQUESTS", DisplayName = "Requests & Buy2Pay", IsEnabled = true, AllowedExtensions = ".pdf,.jpg,.jpeg,.png", UpdatedBy = "System", UpdatedAtUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new OcrModuleConfig { Id = 2, ModuleKey = "CONTRACTS", DisplayName = "Contracts Management", IsEnabled = true, AllowedExtensions = ".pdf,.jpg,.jpeg,.png", UpdatedBy = "System", UpdatedAtUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        modelBuilder.Entity<RequestLineItem>()
            .HasOne(r => r.Unit)
            .WithMany()
            .HasForeignKey(r => r.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<RequestLineItem>()
            .HasOne(r => r.LineItemStatus)
            .WithMany()
            .HasForeignKey(r => r.LineItemStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<QuotationItem>()
            .HasOne(q => q.LineItemStatus)
            .WithMany()
            .HasForeignKey(q => q.LineItemStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Request>()
            .HasOne(r => r.Buyer)
            .WithMany()
            .HasForeignKey(r => r.BuyerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Request>()
            .HasOne(r => r.AreaApprover)
            .WithMany()
            .HasForeignKey(r => r.AreaApproverId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Request>()
            .HasOne(r => r.FinalApprover)
            .WithMany()
            .HasForeignKey(r => r.FinalApproverId)
            .OnDelete(DeleteBehavior.Restrict);

        // Security & Scoping Keys
        modelBuilder.Entity<UserRoleAssignment>().HasKey(ura => new { ura.UserId, ura.RoleId });
        modelBuilder.Entity<UserPlantScope>().HasKey(ups => new { ups.UserId, ups.PlantId });
        modelBuilder.Entity<UserDepartmentScope>().HasKey(uds => new { uds.UserId, uds.DepartmentId });

        // Annual Budget Constraints
        // Note: Logical unique key is handled in application logic because CostCenterId can be NULL
        // We will just create a non-unique covering index to speed up lookups
        modelBuilder.Entity<AnnualBudget>()
            .HasIndex(a => new { a.Year, a.CompanyId, a.PlantId, a.DepartmentId, a.CostCenterId, a.CurrencyId })
            .HasDatabaseName("IX_AnnualBudget_Hierarchy");

        modelBuilder.Entity<AnnualBudget>()
            .Property(a => a.TotalAmount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<AnnualBudget>()
            .HasOne(a => a.Company)
            .WithMany()
            .HasForeignKey(a => a.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AnnualBudget>()
            .HasOne(a => a.Plant)
            .WithMany()
            .HasForeignKey(a => a.PlantId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AnnualBudget>()
            .HasOne(a => a.Department)
            .WithMany()
            .HasForeignKey(a => a.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AnnualBudget>()
            .HasOne(a => a.CostCenter)
            .WithMany()
            .HasForeignKey(a => a.CostCenterId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AnnualBudget>()
            .HasOne(a => a.Currency)
            .WithMany()
            .HasForeignKey(a => a.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Department Responsible User mapping (ambiguity resolution)
        modelBuilder.Entity<Department>()
            .HasOne(d => d.ResponsibleUser)
            .WithMany()
            .HasForeignKey(d => d.ResponsibleUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Standard User -> Department link
        modelBuilder.Entity<User>()
            .HasOne(u => u.Department)
            .WithMany()
            .HasForeignKey(u => u.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // ─── HR Leave Module Configuration ───

        // DepartmentMaster
        modelBuilder.Entity<DepartmentMaster>(entity =>
        {
            entity.HasIndex(d => new { d.SourceSystem, d.SourceDatabase, d.DepartmentCode }).IsUnique();
        });

        // HREmployee
        modelBuilder.Entity<HREmployee>(entity =>
        {
            entity.HasIndex(e => e.InnuxEmployeeId).IsUnique();
            entity.HasIndex(e => e.EmployeeCode).IsUnique();
            entity.HasIndex(e => e.PlantId);
            entity.HasIndex(e => e.PortalDepartmentId);
            entity.HasIndex(e => e.DepartmentMasterId);
            entity.HasIndex(e => e.ManagerUserId);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.IsMapped);

            entity.HasOne(e => e.Plant)
                .WithMany()
                .HasForeignKey(e => e.PlantId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.PortalDepartment)
                .WithMany()
                .HasForeignKey(e => e.PortalDepartmentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.DepartmentMaster)
                .WithMany(d => d.Employees)
                .HasForeignKey(e => e.DepartmentMasterId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ManagerUser)
                .WithMany()
                .HasForeignKey(e => e.ManagerUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // LeaveType
        modelBuilder.Entity<LeaveType>(entity =>
        {
            entity.HasIndex(lt => lt.Code).IsUnique();
        });

        // LeaveRecord
        modelBuilder.Entity<LeaveRecord>(entity =>
        {
            entity.HasIndex(lr => lr.EmployeeId);
            entity.HasIndex(lr => lr.StatusCode);
            entity.HasIndex(lr => lr.StartDate);
            entity.HasIndex(lr => lr.EndDate);

            entity.HasOne(lr => lr.Employee)
                .WithMany()
                .HasForeignKey(lr => lr.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(lr => lr.LeaveType)
                .WithMany()
                .HasForeignKey(lr => lr.LeaveTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(lr => lr.RequestedByUser)
                .WithMany()
                .HasForeignKey(lr => lr.RequestedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(lr => lr.ApprovedByUser)
                .WithMany()
                .HasForeignKey(lr => lr.ApprovedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(lr => lr.StatusHistory)
                .WithOne(sh => sh.LeaveRecord)
                .HasForeignKey(sh => sh.LeaveRecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // LeaveStatusHistory
        modelBuilder.Entity<LeaveStatusHistory>(entity =>
        {
            entity.HasOne(sh => sh.ActorUser)
                .WithMany()
                .HasForeignKey(sh => sh.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // HRSyncLog
        modelBuilder.Entity<HRSyncLog>(entity =>
        {
            entity.HasOne(sl => sl.TriggeredByUser)
                .WithMany()
                .HasForeignKey(sl => sl.TriggeredByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ─── Badge Management Module Configuration ───

        // BadgeLayout
        modelBuilder.Entity<BadgeLayout>(entity =>
        {
            entity.HasIndex(bl => bl.Status);
            entity.HasIndex(bl => new { bl.Name, bl.Version }).IsUnique();
            entity.HasIndex(bl => new { bl.CompanyCode, bl.BadgeType, bl.Status });

            entity.HasOne(bl => bl.CreatedByUser)
                .WithMany()
                .HasForeignKey(bl => bl.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(bl => bl.UpdatedByUser)
                .WithMany()
                .HasForeignKey(bl => bl.UpdatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // BadgePrintHistory
        modelBuilder.Entity<BadgePrintHistory>(entity =>
        {
            entity.HasIndex(bph => bph.EmployeeCode);
            entity.HasIndex(bph => bph.PrintedAtUtc);
            entity.HasIndex(bph => bph.PrintedByUserId);
            entity.HasIndex(bph => bph.CompanyCode);

            entity.HasOne(bph => bph.BadgeLayout)
                .WithMany()
                .HasForeignKey(bph => bph.BadgeLayoutId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(bph => bph.PrintedByUser)
                .WithMany()
                .HasForeignKey(bph => bph.PrintedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(bph => bph.ReprintEvents)
                .WithOne(ev => ev.BadgePrintHistory)
                .HasForeignKey(ev => ev.BadgePrintHistoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // BadgePrintEvent
        modelBuilder.Entity<BadgePrintEvent>(entity =>
        {
            entity.HasIndex(ev => ev.BadgePrintHistoryId);
            entity.HasIndex(ev => ev.ReprintedAtUtc);

            entity.HasOne(ev => ev.ReprintedByUser)
                .WithMany()
                .HasForeignKey(ev => ev.ReprintedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── Contracts Management Module Configuration ───

        modelBuilder.Entity<ContractType>(entity =>
        {
            entity.HasIndex(ct => ct.Code).IsUnique();
        });

        modelBuilder.Entity<Contract>(entity =>
        {
            entity.HasIndex(c => c.ContractNumber).IsUnique();
            entity.HasIndex(c => c.StatusCode);
            entity.HasIndex(c => c.CompanyId);
            entity.HasIndex(c => c.PlantId);
            entity.HasIndex(c => c.DepartmentId);
            entity.HasIndex(c => c.SupplierId);
            entity.HasIndex(c => c.ExpirationDateUtc);

            entity.Property(c => c.TotalContractValue).HasColumnType("decimal(18,2)");
            entity.Property(c => c.LatePenaltyValue).HasColumnType("decimal(18,4)");
            entity.Property(c => c.LateInterestValue).HasColumnType("decimal(18,4)");

            entity.HasOne(c => c.ContractType)
                .WithMany()
                .HasForeignKey(c => c.ContractTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.Supplier)
                .WithMany()
                .HasForeignKey(c => c.SupplierId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(c => c.Department)
                .WithMany()
                .HasForeignKey(c => c.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.Company)
                .WithMany()
                .HasForeignKey(c => c.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.Plant)
                .WithMany()
                .HasForeignKey(c => c.PlantId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(c => c.Currency)
                .WithMany()
                .HasForeignKey(c => c.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.CreatedByUser)
                .WithMany()
                .HasForeignKey(c => c.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Two-step approval participants (DEC-118)
            // NoAction: SQL Server cannot have multiple cascade paths to the same table (Users).
            // Application layer ensures FKs are cleared before any user deletion.
            entity.HasOne(c => c.TechnicalApprover)
                .WithMany()
                .HasForeignKey(c => c.TechnicalApproverId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(c => c.FinalApprover)
                .WithMany()
                .HasForeignKey(c => c.FinalApproverId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasMany(c => c.Documents)
                .WithOne(d => d.Contract)
                .HasForeignKey(d => d.ContractId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(c => c.Histories)
                .WithOne(h => h.Contract)
                .HasForeignKey(h => h.ContractId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(c => c.PaymentObligations)
                .WithOne(o => o.Contract)
                .HasForeignKey(o => o.ContractId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(c => c.Alerts)
                .WithOne(a => a.Contract)
                .HasForeignKey(a => a.ContractId)
                .OnDelete(DeleteBehavior.Cascade);

            // Reverse navigation: Request.ContractId → Contract.LinkedRequests
            // Restrict (not SetNull) to avoid SQL Server multiple cascade path conflict.
            // Application layer guards against deleting contracts with linked requests.
            entity.HasMany(c => c.LinkedRequests)
                .WithOne(r => r.Contract)
                .HasForeignKey(r => r.ContractId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ContractDocument>(entity =>
        {
            entity.HasIndex(cd => cd.ContractId);
            entity.HasIndex(cd => cd.FileHash).HasFilter("[FileHash] IS NOT NULL");

            entity.HasOne(cd => cd.UploadedByUser)
                .WithMany()
                .HasForeignKey(cd => cd.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ContractHistory>(entity =>
        {
            entity.HasIndex(ch => ch.ContractId);
            entity.HasIndex(ch => ch.OccurredAtUtc);

            entity.HasOne(ch => ch.ActorUser)
                .WithMany()
                .HasForeignKey(ch => ch.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ContractAlert>(entity =>
        {
            entity.HasIndex(ca => ca.ContractId);
            entity.HasIndex(ca => new { ca.IsDismissed, ca.TriggerDateUtc });
        });

        modelBuilder.Entity<ContractPaymentObligation>(entity =>
        {
            entity.HasIndex(o => o.ContractId);
            entity.HasIndex(o => o.StatusCode);
            entity.HasIndex(o => o.DueDateUtc);

            entity.Property(o => o.ExpectedAmount).HasColumnType("decimal(18,2)");

            entity.HasOne(o => o.Currency)
                .WithMany()
                .HasForeignKey(o => o.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── Contract OCR (Phase 1) ───────────────────────────────────────────

        modelBuilder.Entity<ContractOcrExtractionRecord>(entity =>
        {
            // Quick status polling from frontend
            entity.HasIndex(r => new { r.ContractId, r.Status });
            entity.HasIndex(r => r.ContractDocumentId);

            entity.Property(r => r.QualityScore).HasColumnType("decimal(9,4)");

            // Record → Contract
            // NoAction: avoids SQL Server multiple cascade path conflict.
            // Contract → ContractDocuments already cascades; a second cascade path via
            // OcrExtractionRecord → Contract → ContractDocuments triggers error 1785.
            entity.HasOne(r => r.Contract)
                .WithMany()
                .HasForeignKey(r => r.ContractId)
                .OnDelete(DeleteBehavior.NoAction);

            // Record → ContractDocument (many-to-one)
            // Restrict so the source document is always traceable from the record.
            entity.HasOne(r => r.ContractDocument)
                .WithMany()
                .HasForeignKey(r => r.ContractDocumentId)
                .OnDelete(DeleteBehavior.Restrict);

            // Record → TriggeredByUser
            // NoAction: avoids SQL Server multiple cascade path conflict via Contract → User.
            entity.HasOne(r => r.TriggeredByUser)
                .WithMany()
                .HasForeignKey(r => r.TriggeredByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            // Record → ExtractedFields (owned — cascade delete)
            entity.HasMany(r => r.ExtractedFields)
                .WithOne(f => f.ExtractionRecord)
                .HasForeignKey(f => f.ExtractionRecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ContractOcrExtractedField>(entity =>
        {
            entity.HasIndex(f => f.ExtractionRecordId);
            entity.HasIndex(f => new { f.ContractId, f.FieldName });

            entity.Property(f => f.ConfidenceScore).HasColumnType("decimal(9,4)");
        });

        // ContractDocument → OcrExtractionRecord (nullable FK, reverse direction)
        // NoAction: avoids SQL Server multiple cascade path error 1785.
        // The document itself is never auto-deleted by OCR record deletion.
        modelBuilder.Entity<ContractDocument>(entity =>
        {
            entity.HasOne(d => d.OcrExtractionRecord)
                .WithMany()
                .HasForeignKey(d => d.OcrExtractionRecordId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // Request → ContractPaymentObligation FK (unidirectional, Restrict)
        // Restrict to avoid SQL Server multiple cascade path conflict.
        modelBuilder.Entity<Request>()
            .HasOne(r => r.ContractPaymentObligation)
            .WithMany()
            .HasForeignKey(r => r.ContractPaymentObligationId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Request>()
            .HasIndex(r => r.ContractPaymentObligationId)
            .HasFilter("[ContractPaymentObligationId] IS NOT NULL");

        modelBuilder.Entity<Request>()
            .HasIndex(r => r.ContractId)
            .HasFilter("[ContractId] IS NOT NULL");

        // Unique Constraints for Master Data
        modelBuilder.Entity<Unit>().HasIndex(u => u.Code).IsUnique();
        modelBuilder.Entity<Currency>().HasIndex(c => c.Code).IsUnique();
        modelBuilder.Entity<NeedLevel>().HasIndex(n => n.Code).IsUnique();
        modelBuilder.Entity<RequestStatus>().HasIndex(r => r.Code).IsUnique();
        modelBuilder.Entity<RequestStatus>().HasIndex(r => r.DisplayOrder).IsUnique();
        modelBuilder.Entity<LineItemStatus>().HasIndex(s => s.Code).IsUnique();
        modelBuilder.Entity<Supplier>().HasIndex(s => s.PortalCode).IsUnique();
        modelBuilder.Entity<Supplier>().HasIndex(s => s.Name).IsUnique();
        modelBuilder.Entity<Supplier>().HasIndex(s => s.PrimaveraCode).IsUnique().HasFilter("[PrimaveraCode] IS NOT NULL AND [PrimaveraCode] <> ''");
        modelBuilder.Entity<Supplier>().HasIndex(s => s.RegistrationStatus);

        // ─── Supplier Document Configuration (Ficha de Fornecedor) ───
        modelBuilder.Entity<SupplierDocument>(entity =>
        {
            entity.HasIndex(sd => sd.SupplierId);
            entity.HasIndex(sd => new { sd.SupplierId, sd.DocumentType });
            entity.HasIndex(sd => sd.FileHash).HasFilter("[FileHash] IS NOT NULL");

            entity.HasOne(sd => sd.Supplier)
                .WithMany(s => s.Documents)
                .HasForeignKey(sd => sd.SupplierId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(sd => sd.UploadedByUser)
                .WithMany()
                .HasForeignKey(sd => sd.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── Supplier Approval Workflow (Phase 2 — DAF/DG) ───

        // Supplier → DAF/DG Approver FKs (NoAction: avoids SQL Server multiple cascade path)
        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.HasOne(s => s.DafApprover)
                .WithMany()
                .HasForeignKey(s => s.DafApproverId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(s => s.DgApprover)
                .WithMany()
                .HasForeignKey(s => s.DgApproverId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasMany(s => s.StatusHistories)
                .WithOne(h => h.Supplier)
                .HasForeignKey(h => h.SupplierId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // SupplierStatusHistory
        modelBuilder.Entity<SupplierStatusHistory>(entity =>
        {
            entity.HasIndex(h => h.SupplierId);
            entity.HasIndex(h => h.OccurredAtUtc);

            entity.HasOne(h => h.ActorUser)
                .WithMany()
                .HasForeignKey(h => h.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── Proforma Deadline Alerts ───

        modelBuilder.Entity<ProformaDeadlineAlert>(entity =>
        {
            // Dedup: one alert per (Request, Level, Recipient) — globally unique
            entity.HasIndex(a => new { a.RequestId, a.AlertLevel, a.RecipientUserId })
                .IsUnique()
                .HasDatabaseName("IX_ProformaDeadlineAlerts_Dedup");

            entity.HasIndex(a => a.RequestId);

            entity.HasOne(a => a.Request)
                .WithMany()
                .HasForeignKey(a => a.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.RecipientUser)
                .WithMany()
                .HasForeignKey(a => a.RecipientUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<CostCenter>().HasIndex(c => c.Code).IsUnique();

        // ─── Accounts Payable Notification Configuration ───
        modelBuilder.Entity<AccountsPayableNotificationConfig>(entity =>
        {
            entity.HasIndex(c => c.CompanyId).IsUnique();
            entity.HasOne(c => c.Company)
                .WithMany()
                .HasForeignKey(c => c.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── Accounts Payable Notification Logs (dedup + audit) ───
        modelBuilder.Entity<AccountsPayableNotificationLog>(entity =>
        {
            entity.HasIndex(l => new { l.RequestId, l.EventCode, l.RecipientEmail })
                .IsUnique()
                .HasFilter("[Success] = 1 AND [Skipped] = 0")
                .HasDatabaseName("IX_ApNotifLogs_Dedup");
            entity.HasIndex(l => l.RequestId);
            entity.HasIndex(l => l.CompanyId);
        });

        // ─── Buy-to-Pay: RequestPayments ───
        modelBuilder.Entity<RequestPayment>(entity =>
        {
            entity.HasIndex(p => p.RequestId)
                .HasDatabaseName("IX_RequestPayments_RequestId");
            entity.HasIndex(p => new { p.RequestId, p.PaymentType, p.PaymentSequence })
                .IsUnique()
                .HasDatabaseName("IX_RequestPayments_RequestId_Type_Seq");

            entity.Property(p => p.PlannedPercent).HasColumnType("decimal(5,2)");
            entity.Property(p => p.PlannedAmount).HasColumnType("decimal(18,2)");
            entity.Property(p => p.ActualPaidAmount).HasColumnType("decimal(18,2)");
            entity.Property(p => p.DivergenceAmount).HasColumnType("decimal(18,2)");
            entity.Property(p => p.PaymentType).HasMaxLength(32).IsRequired();
            entity.Property(p => p.PaymentStatus).HasMaxLength(32).IsRequired();
            entity.Property(p => p.CurrencyCode).HasMaxLength(10).IsRequired();

            entity.HasOne(p => p.Request)
                .WithMany(r => r.Payments)
                .HasForeignKey(p => p.RequestId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(p => p.PaymentProofAttachment)
                .WithMany()
                .HasForeignKey(p => p.PaymentProofAttachmentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(p => p.ScheduledByUser)
                .WithMany()
                .HasForeignKey(p => p.ScheduledByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(p => p.PaidByUser)
                .WithMany()
                .HasForeignKey(p => p.PaidByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(p => p.CreatedByUser)
                .WithMany()
                .HasForeignKey(p => p.CreatedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ─── Buy-to-Pay: RequestReconciliations (1:N) ───
        modelBuilder.Entity<RequestReconciliation>(entity =>
        {
            entity.HasIndex(r => r.RequestId)
                .HasDatabaseName("IX_RequestReconciliations_RequestId");
            entity.HasIndex(r => new { r.RequestId, r.ReconciliationSequence })
                .IsUnique()
                .HasDatabaseName("IX_RequestReconciliations_RequestId_Seq");

            entity.Property(r => r.FinalInvoiceAmount).HasColumnType("decimal(18,2)");
            entity.Property(r => r.FinalAcceptedAmount).HasColumnType("decimal(18,2)");
            entity.Property(r => r.DeliveredAcceptedAmount).HasColumnType("decimal(18,2)");
            entity.Property(r => r.DifferenceAmount).HasColumnType("decimal(18,2)");
            entity.Property(r => r.RemainingBalanceAmount).HasColumnType("decimal(18,2)");
            entity.Property(r => r.RefundAmount).HasColumnType("decimal(18,2)");
            entity.Property(r => r.ReconciliationStatus).HasMaxLength(32).IsRequired();
            entity.Property(r => r.CurrencyCode).HasMaxLength(10);

            entity.HasOne(r => r.Request)
                .WithMany(req => req.Reconciliations)
                .HasForeignKey(r => r.RequestId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(r => r.CreditNoteAttachment)
                .WithMany()
                .HasForeignKey(r => r.CreditNoteAttachmentId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(r => r.DebitNoteAttachment)
                .WithMany()
                .HasForeignKey(r => r.DebitNoteAttachmentId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(r => r.StartedByUser)
                .WithMany()
                .HasForeignKey(r => r.StartedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(r => r.CompletedByUser)
                .WithMany()
                .HasForeignKey(r => r.CompletedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // Item Catalog configuration
        modelBuilder.Entity<ItemCatalog>().HasIndex(ic => ic.Code).IsUnique();
        modelBuilder.Entity<ItemCatalog>()
            .HasOne(ic => ic.DefaultUnit)
            .WithMany()
            .HasForeignKey(ic => ic.DefaultUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<RequestLineItem>()
            .HasOne(r => r.ItemCatalogItem)
            .WithMany()
            .HasForeignKey(r => r.ItemCatalogId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CostCenter>()
            .HasOne(c => c.Plant)
            .WithMany()
            .HasForeignKey(c => c.PlantId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RequestType>().HasIndex(rt => rt.Code).IsUnique();
        modelBuilder.Entity<Request>().HasIndex(r => r.RequestNumber).IsUnique().HasFilter("[RequestNumber] IS NOT NULL");
        modelBuilder.Entity<SystemCounter>().HasKey(sc => sc.Id);

        modelBuilder.Entity<Request>().HasIndex(r => r.CreatedAtUtc);
        modelBuilder.Entity<Request>().HasIndex(r => r.StatusId);
        modelBuilder.Entity<Request>().HasIndex(r => r.RequesterId);
        
        // Performance Indexes for List Filtering and Scoping
        modelBuilder.Entity<Request>().HasIndex(r => r.RequestTypeId);
        modelBuilder.Entity<Request>().HasIndex(r => r.DepartmentId);
        modelBuilder.Entity<Request>().HasIndex(r => r.PlantId);
        modelBuilder.Entity<Request>().HasIndex(r => r.CompanyId);
        modelBuilder.Entity<Request>().HasIndex(r => r.NeedLevelId);
        modelBuilder.Entity<Request>().HasIndex(r => r.SelectedQuotationId);

        modelBuilder.Entity<RequestLineItem>().HasIndex(r => r.RequestId);
        modelBuilder.Entity<RequestLineItem>().HasIndex(r => new { r.RequestId, r.IsDeleted });

        // ─── Request Field Change History (Audit Trail) ───
        modelBuilder.Entity<RequestFieldChangeHistory>(entity =>
        {
            entity.HasIndex(h => h.RequestId)
                .HasDatabaseName("IX_RequestFieldChangeHistories_RequestId");
            entity.HasIndex(h => h.ActorUserId)
                .HasDatabaseName("IX_RequestFieldChangeHistories_ActorUserId");

            entity.HasOne(h => h.Request)
                .WithMany()
                .HasForeignKey(h => h.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(h => h.ActorUser)
                .WithMany()
                .HasForeignKey(h => h.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(h => h.LineItem)
                .WithMany()
                .HasForeignKey(h => h.LineItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RequestAttachment>().HasIndex(ra => ra.FileHash).HasFilter("[FileHash] IS NOT NULL");

        // Notification Indexes
        modelBuilder.Entity<NotificationStatus>()
            .HasIndex(ns => new { ns.UserId, ns.Category })
            .IsUnique();

        modelBuilder.Entity<InformationalNotification>()
            .HasIndex(infol => infol.UserId);

        // Dedup index for workflow notification orchestrator (CorrelationId + UserId)
        modelBuilder.Entity<InformationalNotification>()
            .HasIndex(infol => new { infol.EventCorrelationId, infol.UserId })
            .HasFilter("[EventCorrelationId] IS NOT NULL")
            .HasDatabaseName("IX_InformationalNotifications_EventCorrelation_User");

        modelBuilder.Entity<Quotation>().Property(q => q.DiscountAmount).HasColumnType("decimal(18,2)");
        
        modelBuilder.Entity<RequestLineItem>().Property(r => r.ReceivedQuantity).HasColumnType("decimal(18,4)");
        modelBuilder.Entity<QuotationItem>().Property(q => q.ReceivedQuantity).HasColumnType("decimal(18,4)");

        // Phase 2: OCR Extracted Items configuration
        modelBuilder.Entity<OcrExtractedItem>(entity =>
        {
            entity.HasOne(o => o.ResolvedUnit)
                .WithMany()
                .HasForeignKey(o => o.ResolvedUnitId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(o => o.RequestId);
            entity.HasIndex(o => o.ExtractionBatchId);
            entity.HasIndex(o => new { o.RequestId, o.ExtractionBatchId });

            // Decimal precision — OCR extraction values
            entity.Property(o => o.Quantity).HasColumnType("decimal(18,4)");
            entity.Property(o => o.UnitPrice).HasColumnType("decimal(18,2)");
            entity.Property(o => o.DiscountAmount).HasColumnType("decimal(18,2)");
            entity.Property(o => o.DiscountPercent).HasColumnType("decimal(9,4)");
            entity.Property(o => o.TaxRate).HasColumnType("decimal(9,4)");
            entity.Property(o => o.LineTotal).HasColumnType("decimal(18,2)");
            entity.Property(o => o.QualityScore).HasColumnType("decimal(9,4)");
        });

        // Phase 2: Reconciliation Records configuration
        modelBuilder.Entity<ReconciliationRecord>(entity =>
        {
            entity.HasOne(r => r.RequesterItem)
                .WithMany()
                .HasForeignKey(r => r.RequesterItemId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(r => r.OcrExtractedItem)
                .WithMany()
                .HasForeignKey(r => r.OcrExtractedItemId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(r => r.QuotationItem)
                .WithMany()
                .HasForeignKey(r => r.QuotationItemId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(r => r.ReviewedByUser)
                .WithMany()
                .HasForeignKey(r => r.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(r => r.RequestId);
            entity.HasIndex(r => r.ExtractionBatchId);
            entity.HasIndex(r => new { r.RequestId, r.ExtractionBatchId });

            // Decimal precision — reconciliation analysis values
            entity.Property(r => r.MatchConfidence).HasColumnType("decimal(9,4)");
            entity.Property(r => r.QuantityDivergence).HasColumnType("decimal(18,4)");
        });

        // Simple Lookup Seeding for V1 Minimums
        modelBuilder.Entity<RequestType>().HasData(
            new RequestType { Id = 1, Code = "QUOTATION", Name = "Cotação" },
            new RequestType { Id = 2, Code = "PAYMENT", Name = "Pagamento" }
        );

        modelBuilder.Entity<RequestStatus>().HasData(
            new RequestStatus { Id = 1, Code = "DRAFT", Name = "Rascunho", DisplayOrder = 1, BadgeColor = "gray" },
            new RequestStatus { Id = 19, Code = "SUBMITTED", Name = "Submetido", DisplayOrder = 2, BadgeColor = "cyan" },
            new RequestStatus { Id = 2, Code = "WAITING_QUOTATION", Name = "Aguardando Cotação", DisplayOrder = 3, BadgeColor = "blue" },
            new RequestStatus { Id = 3, Code = "WAITING_AREA_APPROVAL", Name = "Aguardando Aprovação da Área", DisplayOrder = 4, BadgeColor = "indigo" },
            new RequestStatus { Id = 4, Code = "AREA_ADJUSTMENT", Name = "Reajuste A.A", DisplayOrder = 5, BadgeColor = "orange" },
            new RequestStatus { Id = 5, Code = "WAITING_FINAL_APPROVAL", Name = "Aguardando Aprovação Final", DisplayOrder = 6, BadgeColor = "purple" },
            new RequestStatus { Id = 6, Code = "FINAL_ADJUSTMENT", Name = "Reajuste A.F", DisplayOrder = 7, BadgeColor = "teal" },
            new RequestStatus { Id = 7, Code = "REJECTED", Name = "Rejeitado", DisplayOrder = 8, BadgeColor = "red" },
            new RequestStatus { Id = 8, Code = "WAITING_COST_CENTER", Name = "Inserir C.C", DisplayOrder = 9, BadgeColor = "yellow" },
            new RequestStatus { Id = 9, Code = "APPROVED", Name = "Aprovado", DisplayOrder = 10, BadgeColor = "green" },
            new RequestStatus { Id = 10, Code = "PROFORMA_INVOICE_INSERTED", Name = "Fatura Proforma Inserida", DisplayOrder = 11, BadgeColor = "slate" },
            new RequestStatus { Id = 11, Code = "PO_REQUESTED", Name = "Solicitado P.O", DisplayOrder = 12, BadgeColor = "sky" },
            new RequestStatus { Id = 12, Code = "PO_ISSUED", Name = "P.O Emitida", DisplayOrder = 13, BadgeColor = "lime" },
            new RequestStatus { Id = 13, Code = "PAYMENT_REQUEST_SENT", Name = "Solicitação Pagamento Enviada", DisplayOrder = 14, BadgeColor = "rose" },
            new RequestStatus { Id = 14, Code = "PAYMENT_SCHEDULED", Name = "Pagamento Agendado", DisplayOrder = 15, BadgeColor = "violet" },
            new RequestStatus { Id = 15, Code = "PAYMENT_COMPLETED", Name = "Pagamento Realizado", DisplayOrder = 16, BadgeColor = "fuchsia" },
            new RequestStatus { Id = 16, Code = "WAITING_RECEIPT", Name = "Aguardando Recibo", DisplayOrder = 17, BadgeColor = "stone" },
            new RequestStatus { Id = 21, Code = "IN_FOLLOWUP", Name = "Em Acompanhamento", DisplayOrder = 18, BadgeColor = "amber" },
            new RequestStatus { Id = 17, Code = "COMPLETED", Name = "Finalizado", DisplayOrder = 19, BadgeColor = "carbon" },
            new RequestStatus { Id = 18, Code = "CANCELLED", Name = "Cancelado", DisplayOrder = 20, BadgeColor = "zinc" },
            new RequestStatus { Id = 20, Code = "QUOTATION_COMPLETED", Name = "Cotação Concluída", DisplayOrder = 21, BadgeColor = "emerald", IsActive = false },

            // Buy-to-Pay (Advance Payment) — idempotent seed IDs
            new RequestStatus { Id = 23, Code = "ADVANCE_PAYMENT_REQUIRED", Name = "Adiantamento Necessário", DisplayOrder = 23, BadgeColor = "#ffc107" },
            new RequestStatus { Id = 24, Code = "ADVANCE_PAYMENT_COMPLETED", Name = "Adiantamento Realizado", DisplayOrder = 24, BadgeColor = "#28a745" },
            new RequestStatus { Id = 25, Code = "WAITING_SUPPLIER_DELIVERY", Name = "Ag. Entrega/Serviço", DisplayOrder = 25, BadgeColor = "#6f42c1" },
            new RequestStatus { Id = 26, Code = "WAITING_RECONCILIATION", Name = "Ag. Reconciliação", DisplayOrder = 26, BadgeColor = "#fd7e14" },
            new RequestStatus { Id = 22, Code = "WAITING_PO_CORRECTION", Name = "Devolvido para Compras", DisplayOrder = 22, BadgeColor = "red" }
        );

        modelBuilder.Entity<Currency>().HasData(
            new Currency { Id = 1, Code = "AOA", Symbol = "Kz" },
            new Currency { Id = 2, Code = "USD", Symbol = "$" },
            new Currency { Id = 3, Code = "EUR", Symbol = "€" }
        );

        modelBuilder.Entity<CapexOpexClassification>().HasData(
            new CapexOpexClassification { Id = 1, Name = "CAPEX" },
            new CapexOpexClassification { Id = 2, Name = "OPEX" }
        );

        modelBuilder.Entity<Unit>().HasData(
            new Unit { Id = 1, Code = "UN", Name = "Unidade", AllowsDecimalQuantity = false },
            new Unit { Id = 2, Code = "EA", Name = "Each", AllowsDecimalQuantity = false },
            new Unit { Id = 3, Code = "KG", Name = "Quilograma", AllowsDecimalQuantity = true },
            new Unit { Id = 4, Code = "L",  Name = "Litro", AllowsDecimalQuantity = true },
            new Unit { Id = 5, Code = "M",  Name = "Metro", AllowsDecimalQuantity = true },
            new Unit { Id = 6, Code = "CX", Name = "Caixa", AllowsDecimalQuantity = false }
        );

        modelBuilder.Entity<NeedLevel>().HasData(
            new NeedLevel { Id = 1, Code = "BAIXO", Name = "Baixo" },
            new NeedLevel { Id = 2, Code = "NORMAL", Name = "Normal" },
            new NeedLevel { Id = 3, Code = "URGENTE", Name = "Urgente" },
            new NeedLevel { Id = 4, Code = "CRITICO", Name = "Crítico" }
        );

        modelBuilder.Entity<IvaRate>().HasData(
            new IvaRate { Id = 1, Code = "IVA_14", Name = "IVA 14%", RatePercent = 14.0m, IsActive = true, DisplayOrder = 1 },
            new IvaRate { Id = 2, Code = "IVA_7", Name = "IVA 7%", RatePercent = 7.0m, IsActive = true, DisplayOrder = 2 },
            new IvaRate { Id = 3, Code = "IVA_5", Name = "IVA 5%", RatePercent = 5.0m, IsActive = true, DisplayOrder = 3 },
            new IvaRate { Id = 4, Code = "IVA_3", Name = "IVA 3%", RatePercent = 3.0m, IsActive = true, DisplayOrder = 4 },
            new IvaRate { Id = 5, Code = "IVA_0", Name = "Isento (0%)", RatePercent = 0.0m, IsActive = true, DisplayOrder = 5 }
        );

        // LineItemStatus master data
        // Initial status is auto-assigned by backend based on parent Request type.
        // Buyer-controlled status editing is planned for a future phase.
        modelBuilder.Entity<LineItemStatus>().HasData(
            new LineItemStatus { Id = 1, Code = "WAITING_QUOTATION",  Name = "Aguardando Cotação",  DisplayOrder = 1, BadgeColor = "blue" },
            new LineItemStatus { Id = 2, Code = "PENDING",             Name = "Pendente",            DisplayOrder = 2, BadgeColor = "yellow" },
            new LineItemStatus { Id = 3, Code = "UNDER_REVIEW",        Name = "Em Análise",          DisplayOrder = 3, BadgeColor = "indigo" },
            new LineItemStatus { Id = 8, Code = "WAITING_ORDER",       Name = "Aguardando Encomenda", DisplayOrder = 4, BadgeColor = "slate" },
            new LineItemStatus { Id = 4, Code = "ORDERED",             Name = "Encomendado",         DisplayOrder = 5, BadgeColor = "cyan" },
            new LineItemStatus { Id = 5, Code = "PARTIALLY_RECEIVED",  Name = "Recebido Parcial",   DisplayOrder = 6, BadgeColor = "orange" },
            new LineItemStatus { Id = 6, Code = "RECEIVED",            Name = "Recebido",            DisplayOrder = 7, BadgeColor = "green" },
            new LineItemStatus { Id = 7, Code = "CANCELLED",           Name = "Cancelado",           DisplayOrder = 8, BadgeColor = "red" }
        );
        
        // Official Roles V1
        modelBuilder.Entity<Role>().HasData(
            new Role { Id = 1, RoleName = "System Administrator" },
            new Role { Id = 2, RoleName = "Local Manager" },
            new Role { Id = 3, RoleName = "Requester" },
            new Role { Id = 4, RoleName = "Buyer" },
            new Role { Id = 5, RoleName = "Area Approver" },
            new Role { Id = 6, RoleName = "Final Approver" },
            new Role { Id = 7, RoleName = "Finance" },
            new Role { Id = 8, RoleName = "Receiving" },
            new Role { Id = 9, RoleName = "Contracts" },
            new Role { Id = 10, RoleName = "Import" },
            new Role { Id = 11, RoleName = "Viewer / Management" },
            new Role { Id = 12, RoleName = "HR" },
            new Role { Id = 13, RoleName = "IT" },
            new Role { Id = 14, RoleName = AlplaPortal.Domain.Constants.RoleConstants.Operations }
        );

        // Initial Users & Roles Seed
        var adminId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var requesterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var buyerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var approver1Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var approver2Id = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var defaultPasswordHash = "$2a$11$ZQ/WRuE5UuZpWPECThOQNudjM1i1jftPfkHq0vvMXgZKBDHxHsML."; // temp123

        modelBuilder.Entity<User>().HasData(
            new User { Id = adminId, FullName = "Administrador do Sistema", Email = "admin.portal@alpla.com", IsActive = true, MustChangePassword = true, CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), PasswordHash = defaultPasswordHash },
            new User { Id = requesterId, FullName = "Utilizador Solicitante", Email = "requester@alpla.com", IsActive = true, MustChangePassword = true, CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), PasswordHash = defaultPasswordHash },
            new User { Id = buyerId, FullName = "Comprador Central", Email = "buyer@alpla.com", IsActive = true, MustChangePassword = true, CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), PasswordHash = defaultPasswordHash },
            new User { Id = approver1Id, FullName = "Aprovador de Area", Email = "approver1@alpla.com", IsActive = true, MustChangePassword = true, CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), PasswordHash = defaultPasswordHash },
            new User { Id = approver2Id, FullName = "Aprovador Final", Email = "approver2@alpla.com", IsActive = true, MustChangePassword = true, CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), PasswordHash = defaultPasswordHash }
        );

        modelBuilder.Entity<UserRoleAssignment>().HasData(
            new UserRoleAssignment { UserId = adminId, RoleId = 1 },
            new UserRoleAssignment { UserId = requesterId, RoleId = 3 },
            new UserRoleAssignment { UserId = buyerId, RoleId = 4 },
            new UserRoleAssignment { UserId = approver1Id, RoleId = 5 },
            new UserRoleAssignment { UserId = approver2Id, RoleId = 6 }
        );

        modelBuilder.Entity<Department>().HasData(
            new Department { Id = 1, Name = "Admin", IsActive = true, Code = "ADM" },
            new Department { Id = 2, Name = "Financeiro", IsActive = true, Code = "FIN" },
            new Department { Id = 3, Name = "Logística", IsActive = true, Code = "LOG" },
            new Department { Id = 4, Name = "TI", IsActive = true, Code = "TI" }
        );

        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique().HasFilter("\"Code\" IS NOT NULL");
            entity.Property(e => e.Code).HasMaxLength(20);
        });

        modelBuilder.Entity<Company>().HasData(
            new Company { Id = 1, Name = "AlplaPLASTICO", Code = "APA", IsActive = true },
            new Company { Id = 2, Name = "AlplaSOPRO", Code = "APS", IsActive = true }
        );

        modelBuilder.Entity<Plant>().HasData(
            new Plant { Id = 1, Name = "Viana 1", Code = "AOVIA1", CompanyId = 1, IsActive = true },
            new Plant { Id = 2, Name = "Viana 2", Code = "AOVIA2", CompanyId = 1, IsActive = true },
            new Plant { Id = 3, Name = "Viana 3", Code = "AOVIA3", CompanyId = 2, IsActive = true }
        );

        modelBuilder.Entity<Supplier>().HasData(
            new Supplier { Id = 1, Name = "Alpla Global Services", PortalCode = "SUP-000001", IsActive = true, Origin = "MANUAL", RegistrationStatus = "ACTIVE", CreatedAtUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Supplier { Id = 2, Name = "Standard Supplier 01", PortalCode = "SUP-000002", IsActive = true, Origin = "MANUAL", RegistrationStatus = "ACTIVE", CreatedAtUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );

        // Real operational Cost Centers — linked to Plants per allocation rules (DEC-078)
        modelBuilder.Entity<CostCenter>().HasData(
            new CostCenter { Id = 1, Code = "PET1",  Name = "PET 1",  PlantId = 1, IsActive = true }, // Viana 1
            new CostCenter { Id = 2, Code = "CAPS1", Name = "CAPS 1", PlantId = 1, IsActive = true }, // Viana 1
            new CostCenter { Id = 3, Code = "PET2",  Name = "PET 2",  PlantId = 2, IsActive = true }, // Viana 2
            new CostCenter { Id = 4, Code = "CAPS2", Name = "CAPS 2", PlantId = 2, IsActive = true }, // Viana 2
            new CostCenter { Id = 5, Code = "SBM",   Name = "SBM",    PlantId = 3, IsActive = true }  // Viana 3
        );

        // ─── Integration Foundation (Phase 0) ───

        // IntegrationProvider configuration
        modelBuilder.Entity<IntegrationProvider>().HasIndex(p => p.Code).IsUnique();
        modelBuilder.Entity<IntegrationProvider>()
            .HasOne(p => p.ConnectionStatus)
            .WithOne(s => s.Provider)
            .HasForeignKey<IntegrationConnectionStatus>(s => s.IntegrationProviderId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<IntegrationProvider>()
            .HasOne(p => p.Settings)
            .WithOne(s => s.Provider)
            .HasForeignKey<IntegrationProviderSettings>(s => s.IntegrationProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Seed: Primavera + Innux (Phase 1A — Primavera has real implementation, Innux remains planned)
        // Seed: OPENAI + SMTP (Phase 2 — Integration Management Module, DEC-135)
        modelBuilder.Entity<IntegrationProvider>().HasData(
            new IntegrationProvider
            {
                Id = 1,
                Code = "PRIMAVERA",
                Name = "Primavera ERP",
                ProviderType = "ERP",
                ConnectionType = "SQL",
                Description = "Enterprise Resource Planning — master data source for employees, articles, suppliers, departments, and cost centers.",
                Environment = "PRODUCTION",
                IsEnabled = false,
                IsPlanned = false,
                DisplayOrder = 1,
                Capabilities = "[\"EMPLOYEES\",\"MATERIALS\",\"SUPPLIERS\",\"DEPARTMENTS\",\"COST_CENTERS\"]",
                CreatedAtUtc = new DateTime(2026, 4, 14, 0, 0, 0, DateTimeKind.Utc)
            },
            new IntegrationProvider
            {
                Id = 2,
                Code = "INNUX",
                Name = "Innux Time & Attendance",
                ProviderType = "TIME_ATTENDANCE",
                ConnectionType = "SQL",
                Description = "Biometric time and attendance system — complementary employee/attendance data source.",
                Environment = "PRODUCTION",
                IsEnabled = false,
                IsPlanned = false,
                DisplayOrder = 2,
                Capabilities = "[\"EMPLOYEES\",\"ATTENDANCE\"]",
                CreatedAtUtc = new DateTime(2026, 4, 14, 0, 0, 0, DateTimeKind.Utc)
            },
            new IntegrationProvider
            {
                Id = 3,
                Code = "OPENAI",
                Name = "OpenAI / ChatGPT API",
                ProviderType = "API",
                ConnectionType = "REST_API",
                Description = "AI-powered document extraction and analysis — OCR processing for proformas, invoices, and contracts.",
                Environment = "PRODUCTION",
                IsEnabled = true,
                IsPlanned = false,
                DisplayOrder = 3,
                Capabilities = "[\"DOCUMENT_EXTRACTION\",\"OCR\"]",
                CreatedAtUtc = new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc)
            },
            new IntegrationProvider
            {
                Id = 4,
                Code = "SMTP",
                Name = "Email / SMTP Service",
                ProviderType = "API",
                ConnectionType = "SMTP",
                Description = "Email notification service — sends workflow alerts, password resets, and proforma deadline reminders.",
                Environment = "PRODUCTION",
                IsEnabled = true,
                IsPlanned = false,
                DisplayOrder = 4,
                Capabilities = "[\"EMAIL_NOTIFICATIONS\",\"ALERTS\"]",
                CreatedAtUtc = new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc)
            },
            new IntegrationProvider
            {
                Id = 5,
                Code = "ALPLAPROD",
                Name = "AlplaPROD 1.0 (Production)",
                ProviderType = "PRODUCTION",
                ConnectionType = "SQL",
                Description = "AlplaPROD 1.0 production databases — Viana 1, Viana 2, Viana 3",
                Environment = "PRODUCTION",
                IsEnabled = true,
                IsPlanned = false,
                DisplayOrder = 40,
                Capabilities = "[\"OPERATIONS\",\"TRANSFERS\",\"LOGISTICS\"]",
                CreatedAtUtc = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        // Seed: initial connection status records
        modelBuilder.Entity<IntegrationConnectionStatus>().HasData(
            new IntegrationConnectionStatus { Id = 1, IntegrationProviderId = 1, CurrentStatus = IntegrationStatusCodes.NotConfigured },
            new IntegrationConnectionStatus { Id = 2, IntegrationProviderId = 2, CurrentStatus = IntegrationStatusCodes.Planned },
            new IntegrationConnectionStatus { Id = 3, IntegrationProviderId = 3, CurrentStatus = IntegrationStatusCodes.NotConfigured },
            new IntegrationConnectionStatus { Id = 4, IntegrationProviderId = 4, CurrentStatus = IntegrationStatusCodes.NotConfigured },
            new IntegrationConnectionStatus { Id = 5, IntegrationProviderId = 5, CurrentStatus = IntegrationStatusCodes.NotConfigured }
        );

        // Seed: HR Leave Types
        modelBuilder.Entity<LeaveType>().HasData(
            new LeaveType { Id = 1, Code = "VACATION", DisplayNamePt = "Férias", Color = "#3b82f6", CountsAgainstBalance = true, DisplayOrder = 1 },
            new LeaveType { Id = 2, Code = "SICK_LEAVE", DisplayNamePt = "Licença Médica", Color = "#ef4444", CountsAgainstBalance = false, DisplayOrder = 2 },
            new LeaveType { Id = 3, Code = "JUSTIFIED_ABSENCE", DisplayNamePt = "Falta Justificada", Color = "#f59e0b", CountsAgainstBalance = false, DisplayOrder = 3 },
            new LeaveType { Id = 4, Code = "UNJUSTIFIED_ABSENCE", DisplayNamePt = "Falta Injustificada", Color = "#dc2626", CountsAgainstBalance = false, DisplayOrder = 4 },
            new LeaveType { Id = 5, Code = "PERSONAL_LEAVE", DisplayNamePt = "Licença Pessoal", Color = "#8b5cf6", CountsAgainstBalance = true, DisplayOrder = 5 },
            new LeaveType { Id = 6, Code = "COMPENSATION_DAY", DisplayNamePt = "Dia de Compensação", Color = "#06b6d4", CountsAgainstBalance = false, DisplayOrder = 6 },
            new LeaveType { Id = 7, Code = "OTHER", DisplayNamePt = "Outros", Color = "#6b7280", CountsAgainstBalance = false, DisplayOrder = 7 }
        );

        // Seed: Contract Types (Phase 1 — procurement-oriented only)
        modelBuilder.Entity<ContractType>().HasData(
            new ContractType { Id = 1, Code = "SERVICE", Name = "Serviço", IsActive = true, DisplayOrder = 1 },
            new ContractType { Id = 2, Code = "LEASE", Name = "Locação", IsActive = true, DisplayOrder = 2 },
            new ContractType { Id = 3, Code = "SUPPLY", Name = "Fornecimento", IsActive = true, DisplayOrder = 3 },
            new ContractType { Id = 4, Code = "MAINTENANCE", Name = "Manutenção", IsActive = true, DisplayOrder = 4 }
        );

        // ─── I.T Equipment Module Configuration ───

        // ITEquipment
        modelBuilder.Entity<ITEquipment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AssetTag).IsUnique();
            entity.HasIndex(e => e.SerialNumber)
                  .IsUnique()
                  .HasFilter("\"SerialNumber\" IS NOT NULL AND \"SerialNumber\" <> ''");
            entity.HasIndex(e => e.MacAddress)
                  .IsUnique()
                  .HasFilter("\"MacAddress\" IS NOT NULL AND \"MacAddress\" <> ''");
            entity.HasIndex(e => e.Hostname)
                  .IsUnique()
                  .HasFilter("\"Hostname\" IS NOT NULL AND \"Hostname\" <> ''");
            entity.HasIndex(e => e.StatusCode);
            entity.Property(e => e.AssetTag).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LegacyAssetCode).HasMaxLength(100);
            entity.Property(e => e.CompanyCode).HasMaxLength(20);
            entity.Property(e => e.PlantCode).HasMaxLength(20);
            entity.Property(e => e.EquipmentTypeShortCode).HasMaxLength(10);
            entity.Property(e => e.QrCodeUrl).HasMaxLength(500);
            entity.Property(e => e.Hostname).HasMaxLength(200);
            entity.Property(e => e.Plant).HasMaxLength(100);
            entity.Property(e => e.EquipmentType).HasMaxLength(50);
            entity.Property(e => e.StatusCode).HasMaxLength(50);
            entity.Property(e => e.Manufacturer).HasMaxLength(200);
            entity.Property(e => e.Model).HasMaxLength(200);
            entity.Property(e => e.SerialNumber).HasMaxLength(200);
            entity.Property(e => e.MacAddress).HasMaxLength(100);
            entity.Property(e => e.WifiMacAddress).HasMaxLength(100);
            entity.Property(e => e.Processor).HasMaxLength(200);
            entity.Property(e => e.MemoryRam).HasMaxLength(100);
            entity.Property(e => e.Color).HasMaxLength(50);
            entity.Property(e => e.CurrentOwnerName).HasMaxLength(200);
            entity.Property(e => e.CurrentOwnerEmployeeId).HasMaxLength(100);
            entity.Property(e => e.SourceType).HasMaxLength(50);
            entity.Property(e => e.IdCard).HasMaxLength(200);
            entity.HasOne(e => e.CompanyRef).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.PlantRef).WithMany().HasForeignKey(e => e.PlantId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.CurrentOwnerUser).WithMany().HasForeignKey(e => e.CurrentOwnerUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.UpdatedByUser).WithMany().HasForeignKey(e => e.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        // ITEquipmentAssignment
        modelBuilder.Entity<ITEquipmentAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AssignedToName).HasMaxLength(200);
            entity.Property(e => e.AssignedToEmail).HasMaxLength(300);
            entity.Property(e => e.AssignedToDepartment).HasMaxLength(200);
            entity.Property(e => e.AssignedToPlant).HasMaxLength(100);
            entity.Property(e => e.AssignmentStatus).HasMaxLength(50);
            entity.HasOne(e => e.Equipment).WithMany(eq => eq.Assignments).HasForeignKey(e => e.EquipmentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.AssignedToUser).WithMany().HasForeignKey(e => e.AssignedToUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        // ITEquipmentMovementLog
        modelBuilder.Entity<ITEquipmentMovementLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MovementType).HasMaxLength(100);
            entity.Property(e => e.PreviousStatus).HasMaxLength(50);
            entity.Property(e => e.NewStatus).HasMaxLength(50);
            entity.Property(e => e.PreviousOwnerName).HasMaxLength(200);
            entity.Property(e => e.NewOwnerName).HasMaxLength(200);
            entity.HasOne(e => e.Equipment).WithMany(eq => eq.MovementLogs).HasForeignKey(e => e.EquipmentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        // ITEquipmentAcquisition (1:1 with ITEquipment)
        modelBuilder.Entity<ITEquipmentAcquisition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.EquipmentId).IsUnique();
            entity.Property(e => e.SupplierName).HasMaxLength(300);
            entity.Property(e => e.PurchaseRequestNumber).HasMaxLength(100);
            entity.Property(e => e.PurchaseOrderNumber).HasMaxLength(100);
            entity.Property(e => e.InvoiceNumber).HasMaxLength(100);
            entity.Property(e => e.PaymentReference).HasMaxLength(200);
            entity.Property(e => e.PurchaseAmount).HasPrecision(18, 2);
            entity.Property(e => e.Currency).HasMaxLength(10);
            entity.HasOne(e => e.Equipment).WithOne(eq => eq.Acquisition).HasForeignKey<ITEquipmentAcquisition>(e => e.EquipmentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.UpdatedByUser).WithMany().HasForeignKey(e => e.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        // ITEquipmentDocument
        modelBuilder.Entity<ITEquipmentDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DocumentType).HasMaxLength(50);
            entity.Property(e => e.FileName).HasMaxLength(300);
            entity.Property(e => e.StorageReference).HasMaxLength(300);
            entity.Property(e => e.FileHash).HasMaxLength(128);
            entity.HasOne(e => e.Equipment).WithMany(eq => eq.Documents).HasForeignKey(e => e.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Acquisition).WithMany().HasForeignKey(e => e.AcquisitionId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Assignment).WithMany().HasForeignKey(e => e.AssignmentId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.DeliveryTerm).WithMany().HasForeignKey(e => e.DeliveryTermId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.UploadedByUser).WithMany().HasForeignKey(e => e.UploadedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        // ITEquipmentDeliveryTerm
        modelBuilder.Entity<ITEquipmentDeliveryTerm>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TermNumber).IsUnique();
            entity.HasIndex(e => e.EmployeeEmail);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.EmployeeUserId);
            entity.Property(e => e.TermNumber).HasMaxLength(20).IsRequired();
            entity.Property(e => e.EmployeeName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.EmployeeEmail).HasMaxLength(300);
            entity.Property(e => e.EmployeeDepartment).HasMaxLength(200);
            entity.Property(e => e.EmployeePosition).HasMaxLength(200);
            entity.Property(e => e.EmployeePlant).HasMaxLength(100);
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.HasOne(e => e.EmployeeUser).WithMany().HasForeignKey(e => e.EmployeeUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.GeneratedDocument).WithMany().HasForeignKey(e => e.GeneratedDocumentId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.SignedDocument).WithMany().HasForeignKey(e => e.SignedDocumentId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.ReturnDocument).WithMany().HasForeignKey(e => e.ReturnDocumentId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.UpdatedByUser).WithMany().HasForeignKey(e => e.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
            // Master Data FK relationships (all NoAction for safety)
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.EmployeePlantRef).WithMany().HasForeignKey(e => e.EmployeePlantId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.EmployeeDepartmentRef).WithMany().HasForeignKey(e => e.EmployeeDepartmentId).OnDelete(DeleteBehavior.NoAction);
        });

        // ITEquipmentDeliveryItem
        modelBuilder.Entity<ITEquipmentDeliveryItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DeliveryTermId);
            entity.HasIndex(e => e.EquipmentId);
            entity.HasIndex(e => e.AssignmentId);
            entity.HasIndex(e => e.ItemStatus);
            entity.Property(e => e.ItemStatus).HasMaxLength(30).IsRequired();
            entity.Property(e => e.ReturnCondition).HasMaxLength(30);
            entity.HasOne(e => e.DeliveryTerm).WithMany(dt => dt.Items).HasForeignKey(e => e.DeliveryTermId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Equipment).WithMany().HasForeignKey(e => e.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Assignment).WithMany().HasForeignKey(e => e.AssignmentId).OnDelete(DeleteBehavior.NoAction);
        });

        // ITEquipmentType (dynamic equipment type management)
        modelBuilder.Entity<ITEquipmentType>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.ShortCode).IsUnique().HasFilter("\"ShortCode\" IS NOT NULL AND \"ShortCode\" <> ''");
            entity.Property(e => e.Code).HasMaxLength(50).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ShortCode).HasMaxLength(10).HasDefaultValue("");
        });

        // Seed: I.T Equipment Types
        var seedDate = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        modelBuilder.Entity<ITEquipmentType>().HasData(
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000001"), Code = "LAPTOP", ShortCode = "LAP", DisplayName = "Laptop", SortOrder = 1, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000002"), Code = "DESKTOP", ShortCode = "DSK", DisplayName = "Desktop", SortOrder = 2, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000003"), Code = "MONITOR", ShortCode = "MON", DisplayName = "Monitor", SortOrder = 3, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000004"), Code = "PRINTER", ShortCode = "PRN", DisplayName = "Impressora", SortOrder = 4, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000005"), Code = "NVR", ShortCode = "NVR", DisplayName = "NVR", SortOrder = 5, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000006"), Code = "MOUSE", ShortCode = "MOU", DisplayName = "Rato", SortOrder = 6, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000007"), Code = "KEYBOARD", ShortCode = "KBD", DisplayName = "Teclado", SortOrder = 7, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000008"), Code = "HEADSET", ShortCode = "HDS", DisplayName = "Headset", SortOrder = 8, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000009"), Code = "DOCKING_STATION", ShortCode = "DOC", DisplayName = "Docking Station", SortOrder = 9, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-00000000000a"), Code = "BAG", ShortCode = "BAG", DisplayName = "Mala / Bolsa", SortOrder = 10, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-00000000000b"), Code = "PHONE", ShortCode = "TEL", DisplayName = "Telemóvel", SortOrder = 11, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-00000000000c"), Code = "CHARGER", ShortCode = "CHG", DisplayName = "Carregador", SortOrder = 12, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-00000000000d"), Code = "TABLET", ShortCode = "TAB", DisplayName = "Tablet", SortOrder = 13, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-00000000000e"), Code = "SERVER", ShortCode = "SRV", DisplayName = "Servidor", SortOrder = 14, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-00000000000f"), Code = "NETWORK_EQUIPMENT", ShortCode = "RTR", DisplayName = "Equipamento de Rede", SortOrder = 15, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000010"), Code = "ACCESS_POINT", ShortCode = "AP", DisplayName = "Access Point", SortOrder = 16, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000011"), Code = "SWITCH", ShortCode = "SWT", DisplayName = "Switch", SortOrder = 17, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000012"), Code = "FIREWALL", ShortCode = "FWL", DisplayName = "Firewall", SortOrder = 18, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000013"), Code = "UPS", ShortCode = "UPS", DisplayName = "UPS / Nobreak", SortOrder = 19, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000014"), Code = "PROJECTOR", ShortCode = "PRJ", DisplayName = "Projetor", SortOrder = 20, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000015"), Code = "SCANNER", ShortCode = "SCN", DisplayName = "Scanner", SortOrder = 21, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000016"), Code = "ACCESSORIES", ShortCode = "ACC", DisplayName = "Acessórios", SortOrder = 22, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentType { Id = Guid.Parse("a0000001-0000-0000-0000-000000000017"), Code = "UNKNOWN", ShortCode = "OTH", DisplayName = "Desconhecido", SortOrder = 99, IsActive = true, CreatedAt = seedDate }
        );

        // ITEquipmentManufacturer
        modelBuilder.Entity<ITEquipmentManufacturer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
        });

        // ITEquipmentModel
        modelBuilder.Entity<ITEquipmentModel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ManufacturerId, e.Name }).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.EquipmentTypeCode).HasMaxLength(50);
            entity.HasOne(e => e.Manufacturer).WithMany(m => m.Models).HasForeignKey(e => e.ManufacturerId).OnDelete(DeleteBehavior.Restrict);
        });

        // ITEquipmentProcessor
        modelBuilder.Entity<ITEquipmentProcessor>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
        });

        // ITEquipmentMemoryOption
        modelBuilder.Entity<ITEquipmentMemoryOption>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DisplayName).IsUnique();
            entity.Property(e => e.DisplayName).HasMaxLength(50).IsRequired();
        });

        // Seed: I.T Equipment Manufacturers
        modelBuilder.Entity<ITEquipmentManufacturer>().HasData(
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-000000000001"), Name = "HP", SortOrder = 1, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-000000000002"), Name = "Dell", SortOrder = 2, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-000000000003"), Name = "Lenovo", SortOrder = 3, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-000000000004"), Name = "Apple", SortOrder = 4, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-000000000005"), Name = "Microsoft", SortOrder = 5, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-000000000006"), Name = "Samsung", SortOrder = 6, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-000000000007"), Name = "LG", SortOrder = 7, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-000000000008"), Name = "Brother", SortOrder = 8, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-000000000009"), Name = "Canon", SortOrder = 9, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-00000000000a"), Name = "Epson", SortOrder = 10, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-00000000000b"), Name = "Hikvision", SortOrder = 11, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-00000000000c"), Name = "Dahua", SortOrder = 12, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-00000000000d"), Name = "Cisco", SortOrder = 13, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-00000000000e"), Name = "Fortinet", SortOrder = 14, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-00000000000f"), Name = "Ubiquiti", SortOrder = 15, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-000000000010"), Name = "APC", SortOrder = 16, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-000000000011"), Name = "Logitech", SortOrder = 17, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentManufacturer { Id = Guid.Parse("b0000001-0000-0000-0000-000000000012"), Name = "N/A", SortOrder = 99, IsActive = true, CreatedAt = seedDate }
        );

        // Seed: I.T Equipment Processors
        modelBuilder.Entity<ITEquipmentProcessor>().HasData(
            new ITEquipmentProcessor { Id = Guid.Parse("c0000001-0000-0000-0000-000000000001"), Name = "Intel Core i3", SortOrder = 1, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentProcessor { Id = Guid.Parse("c0000001-0000-0000-0000-000000000002"), Name = "Intel Core i5", SortOrder = 2, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentProcessor { Id = Guid.Parse("c0000001-0000-0000-0000-000000000003"), Name = "Intel Core i7", SortOrder = 3, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentProcessor { Id = Guid.Parse("c0000001-0000-0000-0000-000000000004"), Name = "Intel Core i9", SortOrder = 4, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentProcessor { Id = Guid.Parse("c0000001-0000-0000-0000-000000000005"), Name = "Intel Xeon", SortOrder = 5, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentProcessor { Id = Guid.Parse("c0000001-0000-0000-0000-000000000006"), Name = "AMD Ryzen 3", SortOrder = 6, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentProcessor { Id = Guid.Parse("c0000001-0000-0000-0000-000000000007"), Name = "AMD Ryzen 5", SortOrder = 7, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentProcessor { Id = Guid.Parse("c0000001-0000-0000-0000-000000000008"), Name = "AMD Ryzen 7", SortOrder = 8, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentProcessor { Id = Guid.Parse("c0000001-0000-0000-0000-000000000009"), Name = "AMD Ryzen 9", SortOrder = 9, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentProcessor { Id = Guid.Parse("c0000001-0000-0000-0000-00000000000a"), Name = "Apple M1", SortOrder = 10, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentProcessor { Id = Guid.Parse("c0000001-0000-0000-0000-00000000000b"), Name = "Apple M2", SortOrder = 11, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentProcessor { Id = Guid.Parse("c0000001-0000-0000-0000-00000000000c"), Name = "Apple M3", SortOrder = 12, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentProcessor { Id = Guid.Parse("c0000001-0000-0000-0000-00000000000d"), Name = "N/A", SortOrder = 99, IsActive = true, CreatedAt = seedDate }
        );

        // Seed: I.T Equipment Memory Options
        modelBuilder.Entity<ITEquipmentMemoryOption>().HasData(
            new ITEquipmentMemoryOption { Id = Guid.Parse("d0000001-0000-0000-0000-000000000001"), DisplayName = "4 GB", ValueInGb = 4, SortOrder = 1, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentMemoryOption { Id = Guid.Parse("d0000001-0000-0000-0000-000000000002"), DisplayName = "8 GB", ValueInGb = 8, SortOrder = 2, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentMemoryOption { Id = Guid.Parse("d0000001-0000-0000-0000-000000000003"), DisplayName = "16 GB", ValueInGb = 16, SortOrder = 3, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentMemoryOption { Id = Guid.Parse("d0000001-0000-0000-0000-000000000004"), DisplayName = "32 GB", ValueInGb = 32, SortOrder = 4, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentMemoryOption { Id = Guid.Parse("d0000001-0000-0000-0000-000000000005"), DisplayName = "64 GB", ValueInGb = 64, SortOrder = 5, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentMemoryOption { Id = Guid.Parse("d0000001-0000-0000-0000-000000000006"), DisplayName = "128 GB", ValueInGb = 128, SortOrder = 6, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentMemoryOption { Id = Guid.Parse("d0000001-0000-0000-0000-000000000007"), DisplayName = "N/A", ValueInGb = null, SortOrder = 99, IsActive = true, CreatedAt = seedDate }
        );

        // Seed: I.T Equipment Models (initial common models)
        modelBuilder.Entity<ITEquipmentModel>().HasData(
            new ITEquipmentModel { Id = Guid.Parse("e0000001-0000-0000-0000-000000000001"), ManufacturerId = Guid.Parse("b0000001-0000-0000-0000-000000000001"), Name = "ProBook 440 G10", EquipmentTypeCode = "LAPTOP", SortOrder = 1, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentModel { Id = Guid.Parse("e0000001-0000-0000-0000-000000000002"), ManufacturerId = Guid.Parse("b0000001-0000-0000-0000-000000000001"), Name = "ProBook 450 G10", EquipmentTypeCode = "LAPTOP", SortOrder = 2, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentModel { Id = Guid.Parse("e0000001-0000-0000-0000-000000000003"), ManufacturerId = Guid.Parse("b0000001-0000-0000-0000-000000000001"), Name = "E24 G5", EquipmentTypeCode = "MONITOR", SortOrder = 3, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentModel { Id = Guid.Parse("e0000001-0000-0000-0000-000000000004"), ManufacturerId = Guid.Parse("b0000001-0000-0000-0000-000000000002"), Name = "Latitude 5440", EquipmentTypeCode = "LAPTOP", SortOrder = 1, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentModel { Id = Guid.Parse("e0000001-0000-0000-0000-000000000005"), ManufacturerId = Guid.Parse("b0000001-0000-0000-0000-000000000002"), Name = "OptiPlex 7010", EquipmentTypeCode = "DESKTOP", SortOrder = 2, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentModel { Id = Guid.Parse("e0000001-0000-0000-0000-000000000006"), ManufacturerId = Guid.Parse("b0000001-0000-0000-0000-000000000002"), Name = "P2422H", EquipmentTypeCode = "MONITOR", SortOrder = 3, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentModel { Id = Guid.Parse("e0000001-0000-0000-0000-000000000007"), ManufacturerId = Guid.Parse("b0000001-0000-0000-0000-000000000002"), Name = "KB216", EquipmentTypeCode = "KEYBOARD", SortOrder = 4, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentModel { Id = Guid.Parse("e0000001-0000-0000-0000-000000000008"), ManufacturerId = Guid.Parse("b0000001-0000-0000-0000-000000000003"), Name = "ThinkPad E14", EquipmentTypeCode = "LAPTOP", SortOrder = 1, IsActive = true, CreatedAt = seedDate },
            new ITEquipmentModel { Id = Guid.Parse("e0000001-0000-0000-0000-000000000009"), ManufacturerId = Guid.Parse("b0000001-0000-0000-0000-000000000011"), Name = "M90", EquipmentTypeCode = "MOUSE", SortOrder = 1, IsActive = true, CreatedAt = seedDate }
        );
    }
}
