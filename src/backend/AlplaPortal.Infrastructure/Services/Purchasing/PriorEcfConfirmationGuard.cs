using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Purchasing;

/// <summary>
/// D4 replacement-P.O. block. The dependency follows the LINE ITEM through <see cref="RequestPoGroupCancellationItem"/>
/// rows, so it survives any number of new batches / groups / restart cycles. Locking: inside the caller's transaction
/// the matching cancellation rows are read <c>WITH (UPDLOCK, HOLDLOCK)</c> (repo convention: OperationInvoicesController
/// / SupplierCreationService); Finance's confirm/reject reads the same row <c>WITH (UPDLOCK, ROWLOCK)</c>, so a
/// registration and a confirmation can never interleave — one of them waits and then sees the committed state.
/// </summary>
public sealed class PriorEcfConfirmationGuard : IPriorEcfConfirmationGuard
{
    private readonly ApplicationDbContext _context;
    public PriorEcfConfirmationGuard(ApplicationDbContext context) => _context = context;

    public async Task<PriorEcfConfirmationDto> EvaluateForGroupAsync(Guid groupId, bool lockRows)
    {
        var lineIds = await _context.RequestLineItems.AsNoTracking()
            .Where(li => li.RequestPoGroupId == groupId && !li.IsDeleted)
            .Select(li => li.Id)
            .ToListAsync();
        return await EvaluateForLineItemsAsync(lineIds, lockRows);
    }

    public async Task<PriorEcfConfirmationDto> EvaluateForLineItemsAsync(IReadOnlyCollection<Guid> lineItemIds, bool lockRows)
    {
        var result = new PriorEcfConfirmationDto();
        if (lineItemIds.Count == 0) return result;
        var ids = lineItemIds.Distinct().ToList();

        List<RequestPoGroupCancellation> candidates;
        if (lockRows && _context.Database.IsSqlServer())
        {
            // Row + range locks held until the caller's transaction ends. A HOLDLOCK on the join range also blocks a
            // concurrent INSERT of new cancellation items for these lines (a parallel cancel of a sibling cycle).
            var parameters = ids.Select(id => (object)id).ToArray();
            var placeholders = string.Join(",", ids.Select((_, i) => "{" + i + "}"));
            candidates = await _context.RequestPoGroupCancellations
                .FromSqlRaw($"SELECT c.* FROM [RequestPoGroupCancellations] c WITH (UPDLOCK, HOLDLOCK) " +
                            $"INNER JOIN [RequestPoGroupCancellationItems] i ON i.[RequestPoGroupCancellationId] = c.[Id] " +
                            $"WHERE i.[RequestLineItemId] IN ({placeholders})", parameters)
                .AsNoTracking()
                .Include(c => c.Items)
                .ToListAsync();
        }
        else
        {
            candidates = await _context.RequestPoGroupCancellations.AsNoTracking()
                .Include(c => c.Items)
                .Where(c => c.Items.Any(i => ids.Contains(i.RequestLineItemId)))
                .ToListAsync();
        }

        var blocking = candidates
            .Where(c => c.FinanceConfirmationRequired
                        && c.FinanceConfirmationStatus != PoGroupCancellationConstants.FinanceConfirmationStatuses.Confirmed)
            .OrderBy(c => c.CancelledAtUtc)
            .ToList();
        if (blocking.Count == 0) return result;

        var lineNumbers = await _context.RequestLineItems.AsNoTracking()
            .Where(li => ids.Contains(li.Id))
            .Select(li => new { li.Id, li.LineNumber })
            .ToDictionaryAsync(x => x.Id, x => x.LineNumber);
        var groupIds = blocking.Select(c => c.RequestPoGroupId).Distinct().ToList();
        var supplierNames = await _context.RequestPoGroups.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .Select(g => new { g.Id, Name = g.Supplier != null ? g.Supplier.Name : g.SupplierNameSnapshot })
            .ToDictionaryAsync(x => x.Id, x => x.Name);

        result.Blocked = true;
        result.Cancellations = blocking.Select(c => new PriorEcfBlockingCancellationDto
        {
            CancellationId = c.Id,
            PreviousPoGroupId = c.RequestPoGroupId,
            PreviousPoNumber = c.PurchaseOrderNumberSnapshot,
            PreviousSupplierName = supplierNames.TryGetValue(c.RequestPoGroupId, out var n) ? n : null,
            FinanceConfirmationStatus = c.FinanceConfirmationStatus,
            EcfStatus = c.EcfStatus,
            FinanceConfirmationNote = c.FinanceConfirmationNote,
            LineNumbers = c.Items.Where(i => ids.Contains(i.RequestLineItemId))
                .Select(i => lineNumbers.TryGetValue(i.RequestLineItemId, out var ln) ? ln : 0)
                .Where(ln => ln > 0).Distinct().OrderBy(ln => ln).ToList(),
        }).ToList();
        var refs = string.Join(", ", result.Cancellations.Select(c => c.PreviousPoNumber ?? "P.O. sem número"));
        result.Message = $"A nova P.O não pode ser registada: a anulação da(s) P.O/ECF anterior(es) ({refs}) ainda não foi confirmada por Finanças. " +
                         "A cotação e a aprovação podem prosseguir; o registo fica desbloqueado após a confirmação em Obrigações & Pagamentos → Cancelamentos a confirmar.";
        return result;
    }
}
