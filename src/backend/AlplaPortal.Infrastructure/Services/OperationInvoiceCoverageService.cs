using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services;

/// <inheritdoc />
public class OperationInvoiceCoverageService : IOperationInvoiceCoverageService
{
    private readonly ApplicationDbContext _context;

    public OperationInvoiceCoverageService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<GroupCoverageChange>> RederiveAsync(
        IReadOnlyCollection<Guid> requestPoGroupIds,
        bool forceGroupTouch,
        CancellationToken cancellationToken = default)
    {
        var changes = new List<GroupCoverageChange>();
        var ids = requestPoGroupIds.Distinct().ToList();
        if (ids.Count == 0) return changes;

        // Tracked on purpose: the caller's SaveChanges persists the recomputed status atomically
        // with whatever change triggered the re-derivation.
        var groups = await _context.RequestPoGroups
            .Where(g => ids.Contains(g.Id))
            .ToListAsync(cancellationToken);

        // The derivation must see the CALLER'S IN-TRANSACTION STATE, not the database's: a
        // validation flips the invoice status in memory before saving, and an allocation
        // replace-set adds/updates/removes rows before saving. Tracked queries give identity
        // resolution (modified rows come back with their in-memory values); Added and Deleted
        // entries are reconciled through the change tracker.
        var dbAllocations = await _context.OperationInvoiceAllocations
            .Where(a => ids.Contains(a.RequestPoGroupId))
            .ToListAsync(cancellationToken);

        var allocationEntities = dbAllocations
            .Where(a => _context.Entry(a).State != EntityState.Deleted)
            .Concat(_context.OperationInvoiceAllocations.Local
                .Where(a => ids.Contains(a.RequestPoGroupId) &&
                            _context.Entry(a).State == EntityState.Added))
            .ToList();

        var invoiceIds = allocationEntities.Select(a => a.OperationInvoiceId).Distinct().ToList();
        var invoiceStatusById = (await _context.OperationInvoices
                .Where(i => invoiceIds.Contains(i.Id))
                .ToListAsync(cancellationToken))
            // Superseded exclusion mirrors the obligations projection. Replace is blocked on
            // downstream evidence, so a superseded invoice never carries allocations — the filter
            // keeps the two readers rule-identical regardless.
            .Where(i => i.SupersededByOperationInvoiceId == null)
            .ToDictionary(i => i.Id, i => i.Status);

        var approvedShortCloseGroupIds = (await _context.OperationInvoiceShortCloses
                .Where(c => ids.Contains(c.RequestPoGroupId))
                .ToListAsync(cancellationToken))
            .Where(c => _context.Entry(c).State != EntityState.Deleted &&
                        string.Equals(c.Status, RequestConstants.ShortCloseStatuses.Approved,
                            StringComparison.OrdinalIgnoreCase))
            .Select(c => c.RequestPoGroupId)
            .ToHashSet();

        foreach (var group in groups)
        {
            var coverage = OperationInvoiceAggregateDeriver.Derive(
                isClassified: group.SourceDocumentType != null,
                requiresOperationInvoice: group.RequiresOperationInvoice,
                expectedTotal: group.ExpectedOperationInvoiceTotal,
                allocations: allocationEntities
                    .Where(a => a.RequestPoGroupId == group.Id &&
                                invoiceStatusById.ContainsKey(a.OperationInvoiceId))
                    .Select(a => new AllocationCoverage(
                        invoiceStatusById[a.OperationInvoiceId], a.AllocatedGrossAmount)),
                hasApprovedShortClose: approvedShortCloseGroupIds.Contains(group.Id));

            var previous = group.OperationInvoiceStatus;
            if (!string.Equals(previous, coverage.Status, StringComparison.OrdinalIgnoreCase))
            {
                group.OperationInvoiceStatus = coverage.Status;
            }

            if (forceGroupTouch)
            {
                // Concurrency guard: even a status-neutral effective-coverage write must bump the
                // group row so the RowVersion WHERE clause detects a racing writer.
                group.UpdatedAtUtc = DateTime.UtcNow;
            }

            changes.Add(new GroupCoverageChange(group.Id, previous, coverage.Status, coverage));
        }

        return changes;
    }
}
