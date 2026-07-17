using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Requests;

/// <summary>
/// Default <see cref="ILineItemFactory"/>. Mirrors the total/line-number/status math historically
/// inlined in RequestsController.AddLineItem so both the standard add-item flow and the buyer
/// reconciliation workaround share one implementation.
/// </summary>
public class LineItemFactory : ILineItemFactory
{
    private readonly ApplicationDbContext _context;

    public LineItemFactory(ApplicationDbContext context)
    {
        _context = context;
    }

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public async Task<RequestLineItem> BuildAndStageAsync(Request request, LineItemCreationSpec spec, Guid actorId, string actorFullName)
    {
        var typeCode = request.RequestType?.Code;

        // Validate and normalize ItemPriority — backend enforces valid codes.
        var validPriorities = new[] { "HIGH", "MEDIUM", "LOW" };
        var itemPriority = validPriorities.Contains(spec.ItemPriority?.ToUpper()) ? spec.ItemPriority!.ToUpper() : "MEDIUM";

        // Auto-assign initial item status based on parent request type (backend-controlled).
        int? lineItemStatusId = typeCode switch
        {
            "QUOTATION" => 1, // WAITING_QUOTATION
            "PAYMENT" => 2,   // PENDING
            _ => null
        };

        var nextLineNumber = request.LineItems.Any() ? request.LineItems.Max(l => l.LineNumber) + 1 : 1;

        var quantity = spec.Quantity;
        var unitPrice = spec.UnitPrice;
        var netTotal = Round2((quantity * unitPrice) - (spec.DiscountAmount ?? 0));
        var ivaRate = spec.IvaRateId.HasValue ? await _context.IvaRates.FindAsync(spec.IvaRateId.Value) : null;
        var ivaAmount = ivaRate != null ? Round2(netTotal * (ivaRate.RatePercent / 100m)) : 0m;
        var computedTotal = Round2(netTotal + ivaAmount);

        var newItem = new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            LineNumber = nextLineNumber,
            ItemPriority = itemPriority,
            Description = spec.Description,
            Quantity = quantity,
            UnitId = spec.UnitId,
            UnitPrice = unitPrice,
            DiscountPercent = spec.DiscountPercent,
            DiscountAmount = spec.DiscountAmount,
            TotalAmount = computedTotal,
            CurrencyId = typeCode == "QUOTATION" && spec.CurrencyId.HasValue ? spec.CurrencyId : request.CurrencyId,
            PlantId = spec.PlantId,
            CostCenterId = spec.CostCenterId,
            IvaRateId = spec.IvaRateId,
            LineItemStatusId = lineItemStatusId,
            QuotationLifecycleStatus = spec.QuotationLifecycleStatus,
            SupplierId = typeCode == "PAYMENT" ? request.SupplierId : null,
            SupplierName = typeCode == "PAYMENT" ? null : spec.SupplierName,
            Notes = spec.Notes,
            ItemCatalogId = spec.ItemCatalogId,
            DueDate = spec.DueDate,
            CreationOrigin = spec.CreationOrigin,
            SourceProformaAttachmentId = spec.SourceProformaAttachmentId,
            CreationIdempotencyKey = spec.CreationIdempotencyKey,
            IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actorId
        };

        _context.RequestLineItems.Add(newItem);

        var comment = spec.HistoryComment
            ?? $"Item #{newItem.LineNumber} (\"{newItem.Description}\") adicionado ao pedido por {actorFullName}.";

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actorId,
            ActionTaken = spec.HistoryAction,
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = comment,
            CreatedAtUtc = DateTime.UtcNow
        });

        return newItem;
    }
}
