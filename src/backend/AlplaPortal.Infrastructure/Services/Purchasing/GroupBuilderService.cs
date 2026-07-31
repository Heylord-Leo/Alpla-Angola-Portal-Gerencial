using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlplaPortal.Infrastructure.Services.Purchasing;

public class GroupBuilderService : IGroupBuilderService
{
    private readonly ApplicationDbContext _context;
    private readonly PostPaymentCompletionOptions _postPaymentOptions;

    /// <summary>
    /// <paramref name="postPaymentOptions"/> is optional so existing call sites that only care
    /// about grouping keep working; when omitted the Post-Payment classification is treated as
    /// disabled, which is the safe default.
    /// </summary>
    public GroupBuilderService(
        ApplicationDbContext context,
        IOptions<PostPaymentCompletionOptions>? postPaymentOptions = null)
    {
        _context = context;
        _postPaymentOptions = postPaymentOptions?.Value ?? new PostPaymentCompletionOptions();
    }

    public async Task BuildGroupsForRequestAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await _context.Requests
            .Include(r => r.LineItems)
            .Include(r => r.PoGroups)
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (request == null)
            return;

        // Find all awarded items (have SelectedQuotationItemId)
        var awardedItems = request.LineItems.Where(li => !li.IsDeleted && li.SelectedQuotationItemId.HasValue).ToList();

        if (!awardedItems.Any())
            return;

        // Fetch the corresponding quotation items to get supplier and currency info
        var quotationItemIds = awardedItems.Select(li => li.SelectedQuotationItemId.Value).Distinct().ToList();
        var quotationItems = await _context.Set<QuotationItem>()
            .Include(qi => qi.Quotation)
            .ThenInclude(q => q.Supplier)
            .Where(qi => quotationItemIds.Contains(qi.Id))
            .ToDictionaryAsync(qi => qi.Id, cancellationToken);

        // Group the awarded line items by Supplier, Currency, and Request's PaymentConditionCode
        var groupedItems = awardedItems
            .Select(li => new
            {
                LineItem = li,
                QuotationItem = quotationItems.ContainsKey(li.SelectedQuotationItemId.Value) ? quotationItems[li.SelectedQuotationItemId.Value] : null
            })
            .Where(x => x.QuotationItem != null)
            .GroupBy(x => new
            {
                SupplierId = x.QuotationItem.Quotation.SupplierId,
                CurrencyCode = x.QuotationItem.Quotation.Currency,
                PaymentConditionCode = request.PaymentConditionCode // V1 legacy approach
            })
            .ToList();

        // For each group, check if a PO Group already exists. If not, create it.
        foreach (var group in groupedItems)
        {
            var existingGroup = request.PoGroups.FirstOrDefault(g => 
                g.SupplierId == group.Key.SupplierId && 
                g.CurrencyCode == group.Key.CurrencyCode &&
                g.PaymentConditionCode == group.Key.PaymentConditionCode);

            if (existingGroup == null)
            {
                var quotation = group.First().QuotationItem.Quotation;
                var supplier = quotation.Supplier;
                
                // Get currency ID if possible
                var currencyObj = await _context.Currencies.FirstOrDefaultAsync(c => c.Code == group.Key.CurrencyCode, cancellationToken);

                existingGroup = new RequestPoGroup
                {
                    RequestId = request.Id,
                    SupplierId = group.Key.SupplierId,
                    SupplierNameSnapshot = supplier?.Name ?? quotation.SupplierNameSnapshot,
                    SupplierNifSnapshot = supplier?.TaxId,
                    CurrencyId = currencyObj?.Id,
                    CurrencyCode = group.Key.CurrencyCode,
                    PaymentConditionCode = group.Key.PaymentConditionCode,
                    AdvancePaymentPercent = request.AdvancePaymentPercent,
                    Status = RequestConstants.PoGroupStatuses.Pending,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedByUserId = request.CreatedByUserId // Defaulting to request creator for system action
                };
                
                _context.RequestPoGroups.Add(existingGroup);
                
                // We need to save to get the existingGroup.Id if it's generated, though it's Guid.NewGuid() in constructor
            }

            // Assign the LineItems to this group
            foreach (var item in group)
            {
                item.LineItem.RequestPoGroupId = existingGroup.Id;
            }

            // Calculate total for the group based on the awarded quotation items (not line item estimates)
            existingGroup.TotalAmount = group.Sum(x => x.QuotationItem!.LineTotal);

            // Post-Payment Completion (Release 2): derive the group's Final Invoice obligation from
            // the WINNING quotation(s) behind its awarded items. Losing quotations never contribute,
            // because only items carrying SelectedQuotationItemId reach this point.
            ApplyBillingClassification(existingGroup, group.Select(x => x.QuotationItem!.Quotation));
        }

        // Clean up any empty groups (e.g. after a correction/return flow)
        var validGroupIds = groupedItems.Select(g => request.PoGroups.FirstOrDefault(pg => 
            pg.SupplierId == g.Key.SupplierId && 
            pg.CurrencyCode == g.Key.CurrencyCode &&
            pg.PaymentConditionCode == g.Key.PaymentConditionCode)?.Id).Where(id => id.HasValue).ToList();
            
        var emptyGroups = request.PoGroups.Where(g => !validGroupIds.Contains(g.Id)).ToList();
        if (emptyGroups.Any())
        {
            _context.RequestPoGroups.RemoveRange(emptyGroups);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task BuildGroupsForBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var batch = await _context.ApprovalBatches
            .Include(b => b.Items)
                .ThenInclude(bi => bi.RequestLineItem)
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);

        if (batch == null)
            return;

        var request = await _context.Requests
            .FirstOrDefaultAsync(r => r.Id == batch.RequestId, cancellationToken);

        if (request == null)
            return;

        // Fetch the corresponding quotation items for all batch items
        var quotationItemIds = batch.Items.Select(bi => bi.SelectedQuotationItemId).Distinct().ToList();
        var quotationItems = await _context.Set<QuotationItem>()
            .Include(qi => qi.Quotation)
                .ThenInclude(q => q.Supplier)
            .Where(qi => quotationItemIds.Contains(qi.Id))
            .ToDictionaryAsync(qi => qi.Id, cancellationToken);

        // Group the batch items by Supplier, Currency, and Request's PaymentConditionCode (V1 legacy)
        var groupedItems = batch.Items
            .Select(bi => new
            {
                BatchItem = bi,
                LineItem = bi.RequestLineItem,
                QuotationItem = quotationItems.ContainsKey(bi.SelectedQuotationItemId) ? quotationItems[bi.SelectedQuotationItemId] : null
            })
            .Where(x => x.QuotationItem != null)
            .GroupBy(x => new
            {
                SupplierId = x.QuotationItem!.Quotation.SupplierId,
                CurrencyCode = x.QuotationItem!.Quotation.Currency,
                PaymentConditionCode = request.PaymentConditionCode // V1 legacy approach — same as BuildGroupsForRequestAsync
            })
            .ToList();

        // For each group, create a new PO group scoped to this batch
        foreach (var group in groupedItems)
        {
            var quotation = group.First().QuotationItem!.Quotation;
            var supplier = quotation.Supplier;

            // Get currency ID if possible
            var currencyObj = await _context.Currencies.FirstOrDefaultAsync(c => c.Code == group.Key.CurrencyCode, cancellationToken);

            var poGroup = new RequestPoGroup
            {
                RequestId = batch.RequestId,
                ApprovalBatchId = batchId,
                SupplierId = group.Key.SupplierId,
                SupplierNameSnapshot = supplier?.Name ?? quotation.SupplierNameSnapshot,
                SupplierNifSnapshot = supplier?.TaxId,
                CurrencyId = currencyObj?.Id,
                CurrencyCode = group.Key.CurrencyCode,
                PaymentConditionCode = group.Key.PaymentConditionCode,
                AdvancePaymentPercent = request.AdvancePaymentPercent,
                Status = RequestConstants.PoGroupStatuses.Pending,
                TotalAmount = group.Sum(x => x.QuotationItem!.LineTotal),
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = request.CreatedByUserId // System action
            };

            _context.RequestPoGroups.Add(poGroup);

            // Post-Payment Completion (Release 2) — see ApplyBillingClassification.
            ApplyBillingClassification(poGroup, group.Select(x => x.QuotationItem!.Quotation));

            // Assign the LineItems to this group
            foreach (var item in group)
            {
                item.LineItem.RequestPoGroupId = poGroup.Id;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Post-Payment Completion (Release 2): sets a PO group's <c>BillingDocumentType</c> and derived
    /// <c>FinalInvoiceStatus</c> from the winning quotations behind its awarded items.
    ///
    /// <list type="bullet">
    /// <item>PROFORMA → PENDING_UPLOAD — a Final Invoice is owed after payment.</item>
    /// <item>FINAL_INVOICE → NOT_APPLICABLE — the obligation was already met.</item>
    /// <item>missing, or two winning quotations disagreeing → UNCLASSIFIED, which blocks completion
    /// until a human classifies it. Guessing an obligation is never acceptable.</item>
    /// </list>
    ///
    /// <para><b>Winner replacement.</b> Groups are rebuilt whenever the award changes, so this runs
    /// again and recomputes the obligation. It refuses to touch a group that has already started its
    /// post-payment lifecycle (an invoice uploaded, a fiscal receipt attached, or the operational
    /// receipt confirmed): silently rewriting an obligation that documents already answer would
    /// discard real evidence. In practice groups are rebuilt only before the operational stages, so
    /// that guard is a safety net rather than a routine path.</para>
    /// </summary>
    private void ApplyBillingClassification(RequestPoGroup poGroup, IEnumerable<Quotation> winningQuotations)
    {
        if (PostPaymentCompletionPolicy.IsFeatureDisabled(_postPaymentOptions))
            return;

        var hasPostPaymentActivity =
            poGroup.FinalInvoiceAttachmentId != null ||
            poGroup.FiscalReceiptAttachmentId != null ||
            poGroup.OperationalReceiptCompletedAtUtc != null;

        if (hasPostPaymentActivity)
            return;

        var distinctTypes = winningQuotations
            .Select(q => RequestConstants.BillingDocumentTypes.Normalize(q.DocumentType))
            .Distinct()
            .ToList();

        // Exactly one agreed classification is required; anything else stays UNCLASSIFIED.
        var resolved = distinctTypes.Count == 1 ? distinctTypes[0] : null;

        poGroup.BillingDocumentType = resolved;
        poGroup.FinalInvoiceStatus = RequestConstants.BillingDocumentTypes.ToFinalInvoiceStatus(resolved);
    }
}
