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
    public DbSet<RequestAttachment> RequestAttachments => Set<RequestAttachment>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<QuotationItem> QuotationItems => Set<QuotationItem>();
    public DbSet<SystemCounter> SystemCounters => Set<SystemCounter>();
    public DbSet<DocumentExtractionSettings> DocumentExtractionSettings => Set<DocumentExtractionSettings>();
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply Configurations (like Request mapping)
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
        modelBuilder.Entity<CostCenter>().HasIndex(c => c.Code).IsUnique();

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

        modelBuilder.Entity<RequestLineItem>().HasIndex(r => r.RequestId);
        modelBuilder.Entity<RequestLineItem>().HasIndex(r => new { r.RequestId, r.IsDeleted });

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
        modelBuilder.Entity<OcrExtractedItem>()
            .HasOne(o => o.ResolvedUnit)
            .WithMany()
            .HasForeignKey(o => o.ResolvedUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OcrExtractedItem>().HasIndex(o => o.RequestId);
        modelBuilder.Entity<OcrExtractedItem>().HasIndex(o => o.ExtractionBatchId);
        modelBuilder.Entity<OcrExtractedItem>().HasIndex(o => new { o.RequestId, o.ExtractionBatchId });

        // Phase 2: Reconciliation Records configuration
        modelBuilder.Entity<ReconciliationRecord>()
            .HasOne(r => r.RequesterItem)
            .WithMany()
            .HasForeignKey(r => r.RequesterItemId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ReconciliationRecord>()
            .HasOne(r => r.OcrExtractedItem)
            .WithMany()
            .HasForeignKey(r => r.OcrExtractedItemId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ReconciliationRecord>()
            .HasOne(r => r.QuotationItem)
            .WithMany()
            .HasForeignKey(r => r.QuotationItemId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ReconciliationRecord>()
            .HasOne(r => r.ReviewedByUser)
            .WithMany()
            .HasForeignKey(r => r.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ReconciliationRecord>().HasIndex(r => r.RequestId);
        modelBuilder.Entity<ReconciliationRecord>().HasIndex(r => r.ExtractionBatchId);
        modelBuilder.Entity<ReconciliationRecord>().HasIndex(r => new { r.RequestId, r.ExtractionBatchId });

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
            new Role { Id = 11, RoleName = "Viewer / Management" }
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

        modelBuilder.Entity<Company>().HasData(
            new Company { Id = 1, Name = "AlplaPLASTICO", IsActive = true },
            new Company { Id = 2, Name = "AlplaSOPRO", IsActive = true }
        );

        modelBuilder.Entity<Plant>().HasData(
            new Plant { Id = 1, Name = "Viana 1", Code = "V1", CompanyId = 1, IsActive = true },
            new Plant { Id = 2, Name = "Viana 2", Code = "V2", CompanyId = 1, IsActive = true },
            new Plant { Id = 3, Name = "Viana 3", Code = "V3", CompanyId = 2, IsActive = true }
        );

        modelBuilder.Entity<Supplier>().HasData(
            new Supplier { Id = 1, Name = "Alpla Global Services", PortalCode = "SUP-000001", IsActive = true, Origin = "MANUAL" },
            new Supplier { Id = 2, Name = "Standard Supplier 01", PortalCode = "SUP-000002", IsActive = true, Origin = "MANUAL" }
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
            }
        );

        // Seed: initial connection status records
        modelBuilder.Entity<IntegrationConnectionStatus>().HasData(
            new IntegrationConnectionStatus { Id = 1, IntegrationProviderId = 1, CurrentStatus = IntegrationStatusCodes.NotConfigured },
            new IntegrationConnectionStatus { Id = 2, IntegrationProviderId = 2, CurrentStatus = IntegrationStatusCodes.Planned }
        );
    }
}
