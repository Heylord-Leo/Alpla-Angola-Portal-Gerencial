using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Services;

/// <summary>
/// Canonical allocator for <see cref="AlplaPortal.Domain.Entities.RequestPayment.PaymentSequence"/>.
///
/// The RequestPayments unique key is (RequestId, PaymentType, PaymentSequence) — REQUEST-scoped, not
/// per-group. Every writer that creates a RequestPayment must therefore allocate the next sequence
/// across ALL of the request's payments of that PaymentType, whether the payment is attached to a
/// RequestPoGroup or group-less (e.g. the reconciliation remaining-balance row). Allocating per-group
/// restarts at 1 for each sibling and collides on the unique index.
///
/// Invariants preserved:
///  • request scope — counts every RequestPayment with this RequestId + PaymentType;
///  • payment-type independence — ADVANCE and FINAL_BALANCE sequences advance separately;
///  • cancelled sequences are never reused — CANCELLED rows keep their sequence and are counted.
///
/// Race-awareness is bounded by the current transaction architecture (no explicit row locks): the
/// unique index remains the ultimate guard. This helper reads both persisted rows and rows already
/// tracked-but-unsaved in the same context, so multiple allocations within one unit of work stay
/// monotonic even before SaveChanges.
///
/// MAINTENANCE TRIGGER: any change to this allocator or to any RequestPayment creation site MUST be
/// validated against the Finance DEV Regression Harness (ZZTEST-FIN-*) —
/// docs/FINANCE_DEV_REGRESSION_HARNESS.md.
/// </summary>
public static class PaymentSequenceAllocator
{
    public static async Task<int> NextSequenceAsync(
        ApplicationDbContext context,
        Guid requestId,
        string paymentType,
        CancellationToken cancellationToken = default)
    {
        var maxPersisted = await context.RequestPayments
            .Where(p => p.RequestId == requestId && p.PaymentType == paymentType)
            .Select(p => (int?)p.PaymentSequence)
            .MaxAsync(cancellationToken) ?? 0;

        var maxTracked = context.RequestPayments.Local
            .Where(p => p.RequestId == requestId && p.PaymentType == paymentType)
            .Select(p => p.PaymentSequence)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Max(maxPersisted, maxTracked) + 1;
    }
}
