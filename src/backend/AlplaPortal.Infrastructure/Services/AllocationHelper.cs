using System;
using System.Collections.Generic;
using System.Linq;

namespace AlplaPortal.Infrastructure.Services;

/// <summary>
/// Shared helper for distributing a monetary amount across allocation lines by percentage.
/// Uses "remainder-to-largest" rounding reconciliation to ensure the sum exactly equals the total.
/// </summary>
public static class AllocationHelper
{
    /// <summary>
    /// Distributes <paramref name="totalAmount"/> across allocation lines proportionally by percentage.
    /// After rounding each amount to 2 decimal places, any rounding residual is applied to the
    /// allocation with the highest percentage (ties broken by lowest AllocationOrder).
    /// </summary>
    /// <param name="totalAmount">The total monetary amount to distribute.</param>
    /// <param name="allocations">Allocation lines with Id, Percentage, and Order.</param>
    /// <returns>A list of (AllocationId, Amount) tuples that sum exactly to totalAmount.</returns>
    public static List<(Guid AllocationId, decimal Amount)> DistributeAmount(
        decimal totalAmount,
        List<(Guid AllocationId, decimal Percentage, int Order)> allocations)
    {
        if (allocations == null || allocations.Count == 0)
            return new List<(Guid, decimal)>();

        if (allocations.Count == 1)
            return new List<(Guid, decimal)> { (allocations[0].AllocationId, totalAmount) };

        // Step 1: Compute rounded amounts
        var results = allocations
            .Select(a => (
                a.AllocationId,
                a.Percentage,
                a.Order,
                Amount: Math.Round(totalAmount * (a.Percentage / 100m), 2, MidpointRounding.ToEven)
            ))
            .ToList();

        // Step 2: Calculate residual
        decimal allocatedSum = results.Sum(r => r.Amount);
        decimal residual = totalAmount - allocatedSum;

        if (residual != 0m)
        {
            // Step 3: Apply residual to the largest allocation (highest %, then lowest order)
            int targetIndex = 0;
            decimal maxPct = results[0].Percentage;
            int minOrder = results[0].Order;

            for (int i = 1; i < results.Count; i++)
            {
                if (results[i].Percentage > maxPct ||
                    (results[i].Percentage == maxPct && results[i].Order < minOrder))
                {
                    targetIndex = i;
                    maxPct = results[i].Percentage;
                    minOrder = results[i].Order;
                }
            }

            var target = results[targetIndex];
            results[targetIndex] = (target.AllocationId, target.Percentage, target.Order, target.Amount + residual);
        }

        return results.Select(r => (r.AllocationId, r.Amount)).ToList();
    }
}
