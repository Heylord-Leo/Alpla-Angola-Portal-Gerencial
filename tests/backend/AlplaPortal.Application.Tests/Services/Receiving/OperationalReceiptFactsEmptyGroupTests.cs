using System;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Receiving;

// v2.245.4 — a group with NO active items is never "fully received": an unlinked group can neither be
// stamped operationally complete nor advanced to WAITING_RECEIPT by a confirmation.
public class OperationalReceiptFactsEmptyGroupTests
{
    private static RequestLineItem Item(string status) => new()
    {
        Id = Guid.NewGuid(), LineNumber = 1, Quantity = 1,
        LineItemStatus = new LineItemStatus { Code = status, Name = status },
    };

    [Fact]
    public void EmptyGroup_IsNeverFullyReceived()
    {
        var group = new RequestPoGroup { Id = Guid.NewGuid(), Status = "WAITING_RECEIPT" };
        Assert.False(OperationalReceiptFacts.AreAllGroupItemsReceived(group));
        Assert.False(OperationalReceiptFacts.AreAllGroupItemsReceived(group, null));
    }

    [Fact]
    public void GroupWithOnlyDeletedItems_IsNeverFullyReceived()
    {
        var group = new RequestPoGroup { Id = Guid.NewGuid(), Status = "PAYMENT_COMPLETED" };
        var li = Item("RECEIVED"); li.IsDeleted = true;
        group.LineItems.Add(li);
        Assert.False(OperationalReceiptFacts.AreAllGroupItemsReceived(group));
    }

    [Fact]
    public void GroupWithAllActiveItemsReceived_IsFullyReceived()
    {
        var group = new RequestPoGroup { Id = Guid.NewGuid(), Status = "PAYMENT_COMPLETED" };
        group.LineItems.Add(Item("RECEIVED"));
        group.LineItems.Add(Item("RECEIVED"));
        Assert.True(OperationalReceiptFacts.AreAllGroupItemsReceived(group));
    }
}
