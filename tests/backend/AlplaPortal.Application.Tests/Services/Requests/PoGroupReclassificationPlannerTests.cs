using System;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

using Agg = RequestConstants.OperationInvoiceStatuses;
using Types = RequestConstants.SourceDocumentTypes;
using Reasons = PoGroupReclassificationBlockReasons;

/// <summary>
/// Release 4 Phase 1c: what a source-document reclassification means for the PO groups already
/// built over its lines. The conservative order being pinned: a disagreeing group blocks, an
/// agreeing group needs nothing, an active group blocks, and only then is a re-stamp allowed.
/// </summary>
public class PoGroupReclassificationPlannerTests
{
    private static readonly Guid GroupId = Guid.NewGuid();

    private static PoGroupReclassificationDecision Plan(
        string? currentType, string?[] contributingTypes, bool activity = false) =>
        Assert.Single(PoGroupReclassificationPlanner.Plan(new[]
        {
            new PoGroupReclassificationInput
            {
                GroupId = GroupId,
                CurrentSourceDocumentType = currentType,
                ContributingDocumentTypes = contributingTypes,
                HasPostPaymentActivity = activity
            }
        }));

    [Fact]
    public void A_proforma_group_whose_document_became_a_factura_is_restamped_to_not_required()
    {
        var d = Plan(Types.Proforma, new[] { Types.Invoice });

        Assert.Equal(PoGroupReclassificationAction.Restamp, d.Action);
        Assert.Equal(Types.Invoice, d.NewSourceDocumentType);
        Assert.False(d.Obligations!.RequiresOperationInvoice);
        Assert.Equal(Agg.NotRequired, d.Obligations.OperationInvoiceStatus);
    }

    [Fact]
    public void A_factura_group_corrected_to_proforma_owes_an_operation_invoice_again()
    {
        var d = Plan(Types.Invoice, new[] { Types.Proforma });

        Assert.Equal(PoGroupReclassificationAction.Restamp, d.Action);
        Assert.True(d.Obligations!.RequiresOperationInvoice);
        Assert.Equal(Agg.PendingUpload, d.Obligations.OperationInvoiceStatus);
    }

    [Fact]
    public void A_group_whose_documents_still_agree_with_its_stamp_needs_nothing()
    {
        var d = Plan(Types.Proforma, new[] { Types.Proforma, Types.Proforma });

        Assert.Equal(PoGroupReclassificationAction.NoChange, d.Action);
    }

    [Fact]
    public void Disagreeing_documents_break_the_grouping_key_and_block_the_change()
    {
        // Two PROFORMA docs shared one group; one becomes INVOICE — the group can no longer
        // satisfy Supplier+Currency+PaymentCondition+Plant+SourceDocumentType. Never silently
        // mutated into either type.
        var d = Plan(Types.Proforma, new[] { Types.Proforma, Types.Invoice });

        Assert.Equal(PoGroupReclassificationAction.Blocked, d.Action);
        Assert.Equal(Reasons.MixedDocumentTypes, d.BlockReasonCode);
        Assert.Null(d.NewSourceDocumentType);
    }

    [Fact]
    public void A_group_with_post_payment_activity_refuses_a_change_of_identity()
    {
        var d = Plan(Types.Proforma, new[] { Types.Invoice }, activity: true);

        Assert.Equal(PoGroupReclassificationAction.Blocked, d.Action);
        Assert.Equal(Reasons.PostPaymentActivityStarted, d.BlockReasonCode);
    }

    [Fact]
    public void Activity_does_not_block_when_the_documents_still_agree_with_the_stamp()
    {
        var d = Plan(Types.Proforma, new[] { Types.Proforma }, activity: true);

        Assert.Equal(PoGroupReclassificationAction.NoChange, d.Action);
    }

    [Fact]
    public void A_group_left_with_no_active_documents_is_undecidable_and_blocks()
    {
        var d = Plan(Types.Proforma, Array.Empty<string?>());

        Assert.Equal(PoGroupReclassificationAction.Blocked, d.Action);
        Assert.Equal(Reasons.MixedDocumentTypes, d.BlockReasonCode);
    }

    [Fact]
    public void The_legacy_final_invoice_alias_agrees_with_invoice()
    {
        // Normalization happens inside the planner, so FINAL_INVOICE and INVOICE are one type,
        // not a mixed group.
        var d = Plan(Types.Invoice, new[] { "FINAL_INVOICE", Types.Invoice });

        Assert.Equal(PoGroupReclassificationAction.NoChange, d.Action);
    }

    [Fact]
    public void Each_group_is_judged_independently()
    {
        var okGroup = Guid.NewGuid();
        var mixedGroup = Guid.NewGuid();

        var decisions = PoGroupReclassificationPlanner.Plan(new[]
        {
            new PoGroupReclassificationInput
            {
                GroupId = okGroup,
                CurrentSourceDocumentType = Types.Proforma,
                ContributingDocumentTypes = new[] { Types.Invoice }
            },
            new PoGroupReclassificationInput
            {
                GroupId = mixedGroup,
                CurrentSourceDocumentType = Types.Proforma,
                ContributingDocumentTypes = new[] { Types.Proforma, Types.Invoice }
            }
        });

        Assert.Equal(PoGroupReclassificationAction.Restamp,
            decisions.Single(x => x.GroupId == okGroup).Action);
        Assert.Equal(PoGroupReclassificationAction.Blocked,
            decisions.Single(x => x.GroupId == mixedGroup).Action);
    }
}
