using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Approvals;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

/// <summary>
/// Shared seed for the phase 1 P.O. cancellation tests: one QUOTATION request (plant 1, department 5, company 1) with an
/// APPROVED batch #1 holding two lines, line 1 in group A (the one under test) and line 2 in sibling group B, each with
/// its own supplier, currency and winning candidate. Users: the owning Buyer, another Buyer, the requester, a SysAdmin,
/// two Finance users scoped to plant 1, one scoped to plant 9, one inactive Finance user in plant 1.
/// </summary>
internal static class PoCancellationTestSeed
{
    public const int Plant = 1, OtherPlant = 9, Dept = 5, Company = 1;
    public const int SupplierA = 10, SupplierB = 20;
    public const int R_BUYER = 4, R_FINANCE = 3, R_ADMIN = 1;

    public sealed class Seed
    {
        public Guid RequestId, Owner, OtherBuyer, Requester, Admin, Finance1, Finance2, FinanceOtherPlant, FinanceInactive, AreaManager;
        public Guid Line1, Line2, GroupA, GroupB, Batch, BatchItem1, BatchItem2, Q1, Q2, Qi1, Qi1Alt, Qi2, PoAttachmentA;
        public string RequestNumber = string.Empty;
    }

    public static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    public static readonly string[] StatusCodes =
    {
        RequestConstants.Statuses.Draft, RequestConstants.Statuses.WaitingQuotation, RequestConstants.Statuses.WaitingAreaApproval,
        RequestConstants.Statuses.WaitingFinalApproval, RequestConstants.Statuses.FinalApproved, RequestConstants.Statuses.QuotationCompleted,
        RequestConstants.Statuses.PoRequested, RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PoPartiallyUploaded,
        RequestConstants.Statuses.WaitingPoCorrection, RequestConstants.Statuses.PaymentRequestSent, RequestConstants.Statuses.PaymentScheduled,
        RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.Paid, RequestConstants.Statuses.WaitingReceipt,
        RequestConstants.Statuses.InFollowup, RequestConstants.Statuses.Completed, RequestConstants.Statuses.Cancelled, RequestConstants.Statuses.Rejected,
        RequestConstants.Statuses.AdvancePaymentRequired, RequestConstants.Statuses.AdvancePaymentScheduled, RequestConstants.Statuses.AdvancePaymentCompleted,
        RequestConstants.Statuses.WaitingSupplierDelivery, RequestConstants.Statuses.WaitingReconciliation, RequestConstants.Statuses.AreaAdjustment,
        RequestConstants.Statuses.FinalAdjustment,
    };

    public static int StatusId(ApplicationDbContext ctx, string code) => ctx.RequestStatuses.AsNoTracking().Single(s => s.Code == code).Id;

    public static async Task<Seed> SeedAsync(ApplicationDbContext ctx,
        string groupAStatus = "PO_ISSUED", string? groupAPoNumber = "ECF10 2026/364", bool groupAPoAttachment = true,
        string groupBStatus = "WAITING_PO", string requestStatus = "PO_PARTIALLY_UPLOADED", string requestType = "QUOTATION",
        string? line1StatusCode = "WAITING_QUOTATION", bool apConfig = true, bool apNotifyOnPoCancelled = false, bool financeEmail = true,
        string? legacyReturnReason = null, string? line2StatusCode = "WAITING_QUOTATION")
    {
        var s = new Seed();
        foreach (var f in typeof(Seed).GetFields().Where(f => f.FieldType == typeof(Guid))) f.SetValue(s, Guid.NewGuid());
        s.RequestNumber = "REQ-27/09/2026-483";

        ctx.RequestTypes.AddRange(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" }, new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        var id = 1;
        foreach (var code in StatusCodes) ctx.RequestStatuses.Add(new RequestStatus { Id = id++, Code = code, Name = code, DisplayOrder = id });
        ctx.LineItemStatuses.AddRange(
            new LineItemStatus { Id = 1, Code = "WAITING_QUOTATION", Name = "Aguardando Cotação" }, new LineItemStatus { Id = 2, Code = "PENDING", Name = "Pendente" },
            new LineItemStatus { Id = 3, Code = "UNDER_REVIEW", Name = "Em Análise" }, new LineItemStatus { Id = 8, Code = "WAITING_ORDER", Name = "Aguardando Encomenda" },
            new LineItemStatus { Id = 4, Code = "ORDERED", Name = "Encomendado" }, new LineItemStatus { Id = 5, Code = "PARTIALLY_RECEIVED", Name = "Recebido Parcial" },
            new LineItemStatus { Id = 6, Code = "RECEIVED", Name = "Recebido" }, new LineItemStatus { Id = 7, Code = "CANCELLED", Name = "Cancelado" });
        ctx.Roles.AddRange(new Role { Id = R_ADMIN, RoleName = RoleConstants.SystemAdministrator }, new Role { Id = R_FINANCE, RoleName = RoleConstants.Finance }, new Role { Id = R_BUYER, RoleName = RoleConstants.Buyer });
        ctx.Companies.Add(new Company { Id = Company, Name = "AlplaPLASTICO", IsActive = true });
        ctx.Plants.AddRange(new Plant { Id = Plant, Name = "Viana 3", CompanyId = Company }, new Plant { Id = OtherPlant, Name = "Outra", CompanyId = Company });
        ctx.Set<Department>().Add(new Department { Id = Dept, Name = "Manutenção", IsActive = true });
        ctx.Suppliers.AddRange(
            new Supplier { Id = SupplierA, Name = "PRODUCTIVE SYSTEMS", TaxId = "5400000001", RegistrationStatus = "ACTIVE", PortalCode = "SUP-A" },
            new Supplier { Id = SupplierB, Name = "OUTRO FORNECEDOR", TaxId = "5400000002", RegistrationStatus = "ACTIVE", PortalCode = "SUP-B" });
        if (apConfig)
            ctx.AccountsPayableNotificationConfigs.Add(new AccountsPayableNotificationConfig { Id = 1, CompanyId = Company, Email = "ap@alpla.test", IsActive = true, NotifyFinanceUsersByEmail = financeEmail, NotifyOnPoCancelled = apNotifyOnPoCancelled });

        User U(Guid uid, string name, string email, bool active = true) => new() { Id = uid, FullName = name, Email = email, IsActive = active };
        ctx.Users.AddRange(U(s.Owner, "Comprador Dono", "owner@test.local"), U(s.OtherBuyer, "Outro Comprador", "other@test.local"), U(s.Requester, "Requisitante", "req@test.local"),
            U(s.Admin, "Admin", "admin@test.local"), U(s.Finance1, "Finance Um", "fin1@test.local"), U(s.Finance2, "Finance Dois", "fin2@test.local"),
            U(s.FinanceOtherPlant, "Finance Outra Planta", "fin9@test.local"), U(s.FinanceInactive, "Finance Inativo", "fininactive@test.local", active: false),
            U(s.AreaManager, "Gestor Área", "area@test.local"));
        ctx.UserRoleAssignments.AddRange(
            new UserRoleAssignment { UserId = s.Owner, RoleId = R_BUYER }, new UserRoleAssignment { UserId = s.OtherBuyer, RoleId = R_BUYER },
            new UserRoleAssignment { UserId = s.Admin, RoleId = R_ADMIN }, new UserRoleAssignment { UserId = s.Finance1, RoleId = R_FINANCE },
            new UserRoleAssignment { UserId = s.Finance2, RoleId = R_FINANCE }, new UserRoleAssignment { UserId = s.FinanceOtherPlant, RoleId = R_FINANCE },
            new UserRoleAssignment { UserId = s.FinanceInactive, RoleId = R_FINANCE });
        ctx.UserPlantScopes.AddRange(
            new UserPlantScope { UserId = s.Owner, PlantId = Plant }, new UserPlantScope { UserId = s.OtherBuyer, PlantId = Plant },
            new UserPlantScope { UserId = s.Finance1, PlantId = Plant }, new UserPlantScope { UserId = s.Finance2, PlantId = Plant },
            new UserPlantScope { UserId = s.FinanceOtherPlant, PlantId = OtherPlant }, new UserPlantScope { UserId = s.FinanceInactive, PlantId = Plant });
        await ctx.SaveChangesAsync();

        ctx.Requests.Add(new Request
        {
            Id = s.RequestId, RequestNumber = s.RequestNumber, Title = "Belt MF501756", RequestTypeId = requestType == "QUOTATION" ? 1 : 2,
            StatusId = StatusId(ctx, requestStatus), RequesterId = s.Requester, BuyerId = s.Owner, AreaApproverId = s.AreaManager,
            DepartmentId = Dept, CompanyId = Company, PlantId = Plant, EstimatedTotalAmount = 0m, ApprovedTotalAmount = 24133.06m + 500m, ApprovedCurrencyCode = "RND",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10), UpdatedAtUtc = DateTime.UtcNow,
        });
        int? LineStatus(string? code) => code == null ? null : ctx.LineItemStatuses.Local.Concat(ctx.LineItemStatuses).First(l => l.Code == code).Id;
        ctx.RequestLineItems.AddRange(
            new RequestLineItem { Id = s.Line1, RequestId = s.RequestId, LineNumber = 1, Description = "Belt MF501756", Quantity = 1, RequestPoGroupId = s.GroupA, SelectedQuotationItemId = s.Qi1, QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationApproved, LineItemStatusId = LineStatus(line1StatusCode), CreatedAtUtc = DateTime.UtcNow },
            new RequestLineItem { Id = s.Line2, RequestId = s.RequestId, LineNumber = 2, Description = "Rolamento", Quantity = 2, RequestPoGroupId = s.GroupB, SelectedQuotationItemId = s.Qi2, QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationApproved, LineItemStatusId = LineStatus(line2StatusCode), CreatedAtUtc = DateTime.UtcNow });
        ctx.Quotations.AddRange(
            new Quotation { Id = s.Q1, RequestId = s.RequestId, SupplierId = SupplierA, SupplierNameSnapshot = "PRODUCTIVE SYSTEMS", Currency = "RND", TotalAmount = 24133.06m, SourceType = "MANUAL", CreatedAtUtc = DateTime.UtcNow.AddDays(-9), CreatedByUserId = s.Owner },
            new Quotation { Id = s.Q2, RequestId = s.RequestId, SupplierId = SupplierB, SupplierNameSnapshot = "OUTRO FORNECEDOR", Currency = "AOA", TotalAmount = 500m, SourceType = "MANUAL", CreatedAtUtc = DateTime.UtcNow.AddDays(-9), CreatedByUserId = s.Owner });
        ctx.QuotationItems.AddRange(
            new QuotationItem { Id = s.Qi1, QuotationId = s.Q1, Description = "Belt", MappedRequestLineItemId = s.Line1, ReconciliationStatus = "MAPPED", Quantity = 1, UnitPrice = 24133.06m, LineTotal = 24133.06m },
            new QuotationItem { Id = s.Qi1Alt, QuotationId = s.Q1, Description = "Belt alt", MappedRequestLineItemId = s.Line1, ReconciliationStatus = "MAPPED", Quantity = 1, UnitPrice = 25000m, LineTotal = 25000m },
            new QuotationItem { Id = s.Qi2, QuotationId = s.Q2, Description = "Rolamento", MappedRequestLineItemId = s.Line2, ReconciliationStatus = "MAPPED", Quantity = 2, UnitPrice = 250m, LineTotal = 500m });
        ctx.ApprovalBatches.Add(new ApprovalBatch { Id = s.Batch, RequestId = s.RequestId, BatchNumber = 1, Status = RequestConstants.ApprovalBatchStatuses.Approved, ApprovedTotalAmount = 24133.06m + 500m, CreatedAtUtc = DateTime.UtcNow.AddDays(-8), CreatedByUserId = s.Owner });
        var cand1 = Guid.NewGuid(); var cand1Alt = Guid.NewGuid(); var cand2 = Guid.NewGuid();
        ctx.ApprovalBatchItems.AddRange(
            new ApprovalBatchItem { Id = s.BatchItem1, ApprovalBatchId = s.Batch, RequestLineItemId = s.Line1, SelectedQuotationItemId = s.Qi1, SelectedCandidateId = cand1, WinnerSelectedByUserId = s.AreaManager, WinnerSelectedAtUtc = DateTime.UtcNow.AddDays(-7), CreatedAtUtc = DateTime.UtcNow.AddDays(-8) },
            new ApprovalBatchItem { Id = s.BatchItem2, ApprovalBatchId = s.Batch, RequestLineItemId = s.Line2, SelectedQuotationItemId = s.Qi2, SelectedCandidateId = cand2, WinnerSelectedByUserId = s.AreaManager, WinnerSelectedAtUtc = DateTime.UtcNow.AddDays(-7), CreatedAtUtc = DateTime.UtcNow.AddDays(-8) });
        ctx.ApprovalBatchItemCandidates.AddRange(
            new ApprovalBatchItemCandidate { Id = cand1, ApprovalBatchItemId = s.BatchItem1, QuotationItemId = s.Qi1, QuotationId = s.Q1, SupplierId = SupplierA, SupplierNameSnapshot = "PRODUCTIVE SYSTEMS", QuotedDescription = "Belt", QuotedQuantity = 1, UnitPrice = 24133.06m, LineTotal = 24133.06m, Currency = "RND", CreatedAtUtc = DateTime.UtcNow.AddDays(-8), CreatedByUserId = s.Owner },
            new ApprovalBatchItemCandidate { Id = cand1Alt, ApprovalBatchItemId = s.BatchItem1, QuotationItemId = s.Qi1Alt, QuotationId = s.Q1, SupplierId = SupplierA, SupplierNameSnapshot = "PRODUCTIVE SYSTEMS", QuotedDescription = "Belt alt", QuotedQuantity = 1, UnitPrice = 25000m, LineTotal = 25000m, Currency = "RND", CreatedAtUtc = DateTime.UtcNow.AddDays(-8), CreatedByUserId = s.Owner },
            new ApprovalBatchItemCandidate { Id = cand2, ApprovalBatchItemId = s.BatchItem2, QuotationItemId = s.Qi2, QuotationId = s.Q2, SupplierId = SupplierB, SupplierNameSnapshot = "OUTRO FORNECEDOR", QuotedDescription = "Rolamento", QuotedQuantity = 2, UnitPrice = 250m, LineTotal = 500m, Currency = "AOA", CreatedAtUtc = DateTime.UtcNow.AddDays(-8), CreatedByUserId = s.Owner });
        ctx.RequestPoGroups.AddRange(
            new RequestPoGroup { Id = s.GroupA, RequestId = s.RequestId, SupplierId = SupplierA, SupplierNameSnapshot = "PRODUCTIVE SYSTEMS", CurrencyCode = "RND", PlantId = Plant, TotalAmount = 24133.06m, Status = groupAStatus, ApprovalBatchId = s.Batch, PurchaseOrderNumber = groupAPoNumber, PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid, PoResponsibleBuyerId = groupAPoNumber != null ? s.Owner : null, CreatedAtUtc = DateTime.UtcNow.AddDays(-7), CreatedByUserId = s.Owner },
            new RequestPoGroup { Id = s.GroupB, RequestId = s.RequestId, SupplierId = SupplierB, SupplierNameSnapshot = "OUTRO FORNECEDOR", CurrencyCode = "AOA", PlantId = Plant, TotalAmount = 500m, Status = groupBStatus, ApprovalBatchId = s.Batch, CreatedAtUtc = DateTime.UtcNow.AddDays(-7), CreatedByUserId = s.Owner });
        if (groupAPoAttachment)
            ctx.RequestAttachments.Add(new RequestAttachment { Id = s.PoAttachmentA, RequestId = s.RequestId, RequestPoGroupId = s.GroupA, AttachmentTypeCode = RequestAttachment.TYPE_PO, FileName = "po-a.pdf", FileExtension = ".pdf", StorageReference = "po-a.pdf", UploadedByUserId = s.Owner, UploadedAtUtc = DateTime.UtcNow.AddDays(-6) });
        ctx.RequestStatusHistories.Add(new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = s.RequestId, ActorUserId = s.AreaManager, ActionTaken = "BATCH_FINAL_APPROVED", NewStatusId = StatusId(ctx, RequestConstants.Statuses.FinalApproved), CreatedAtUtc = DateTime.UtcNow.AddDays(-7), Comment = "Lote #1 aprovado" });
        if (groupAPoNumber != null)
            ctx.RequestStatusHistories.Add(new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = s.RequestId, ActorUserId = s.Owner, ActionTaken = "REGISTER_PO", NewStatusId = StatusId(ctx, RequestConstants.Statuses.PoIssued), CreatedAtUtc = DateTime.UtcNow.AddDays(-6), Comment = $"[Grupo P.O.: PRODUCTIVE SYSTEMS | Total: 24.133,06 | GroupId: {s.GroupA.ToString().Substring(0, 8)}] P.O registrada com sucesso." });
        if (legacyReturnReason != null)
            ctx.RequestStatusHistories.Add(new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = s.RequestId, ActorUserId = s.Finance1, ActionTaken = "FINANCE_RETURN_ADJUSTMENT", NewStatusId = StatusId(ctx, RequestConstants.Statuses.WaitingPoCorrection), CreatedAtUtc = DateTime.UtcNow.AddDays(-1), Comment = $"[Lote #1 | PRODUCTIVE SYSTEMS | RND | Total: 24.133,06] Grupo devolvido por Finanças para correção da P.O (GroupId: {s.GroupA}). Motivo: {legacyReturnReason}" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return s;
    }

    public static Mock<AdminLogWriter> AdminLog()
    {
        var m = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        m.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        return m;
    }

    /// <summary>Orchestrator mock that records events and simulates the recipient bookkeeping callback.</summary>
    public static (Mock<IWorkflowNotificationOrchestrator> Mock, List<WorkflowEvent> Events) OrchestratorMock(int financeCount = 2, bool buyerResolved = true)
    {
        var events = new List<WorkflowEvent>();
        var m = new Mock<IWorkflowNotificationOrchestrator>();
        m.Setup(o => o.EmitAsync(It.IsAny<WorkflowEvent>())).Returns<WorkflowEvent>(e =>
        {
            events.Add(e);
            var policy = financeCount == 0 ? PoCancellationRecipientOutcome.PolicyNoEligibleUsers : PoCancellationRecipientOutcome.PolicyQueued;
            e.OnRecipientsResolved?.Invoke(new PoCancellationRecipientOutcome(financeCount, financeCount, financeCount, policy, buyerResolved, buyerResolved));
            return Task.CompletedTask;
        });
        return (m, events);
    }

    public static PoGroupCancellationService Service(ApplicationDbContext ctx, IWorkflowNotificationOrchestrator orchestrator) =>
        new(ctx, new RequestStatusSyncService(ctx, NullLogger<RequestStatusSyncService>.Instance), orchestrator, AdminLog().Object, NullLogger<PoGroupCancellationService>.Instance);

    public static PoGroupCancellationActor Buyer(Guid id) => new(id, new[] { RoleConstants.Buyer });
    public static PoGroupCancellationActor Finance(Guid id) => new(id, new[] { RoleConstants.Finance });
    public static PoGroupCancellationActor Admin(Guid id) => new(id, new[] { RoleConstants.SystemAdministrator });
    public static PoGroupCancellationActor AdminAndFinance(Guid id) => new(id, new[] { RoleConstants.SystemAdministrator, RoleConstants.Finance });

    public static CancelPoGroupCommand Cancel(Seed s, PoGroupCancellationActor actor, string kind = "RESTART_QUOTATION", string reasonCode = "ORDER_CANCELLED_BY_COMPANY",
        string reason = "Pedido cancelado pela empresa; a necessidade deixou de existir.", string? ecfStatus = "DECLARED_CANCELLED", string? ecfReference = "ECF10 2026/364",
        Guid? groupId = null, byte[]? rowVersion = null, string? ecfNote = null)
        => new(s.RequestId, groupId ?? s.GroupA, actor, kind, reasonCode, reason, ecfStatus, ecfReference, ecfNote, null, rowVersion);
}
