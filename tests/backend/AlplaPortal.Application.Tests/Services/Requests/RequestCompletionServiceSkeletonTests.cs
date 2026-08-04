using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Release 1 acceptance tests for the two-phase completion service.
///
/// The point of these tests is to prove the OPPOSITE of what a normal service test proves:
/// that while the feature flag is false the service does nothing at all, and that the
/// evaluation logic is deliberately NOT activated. The architecture (phase split, transaction
/// contract, ambient-transaction guard) is present and verifiable; the behaviour is not.
///
/// Model-shape assertions use a SQL Server context that is never connected — EF builds the
/// model without opening a connection, so no database is required.
/// </summary>
public class RequestCompletionServiceSkeletonTests
{
    private const string NonConnectingConnectionString =
        "Server=alpla-r1-tests.invalid;Database=AlplaPortal_ModelOnly_DoNotConnect;" +
        "Trusted_Connection=True;TrustServerCertificate=True";

    private static ApplicationDbContext ModelOnlyContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(NonConnectingConnectionString)
            .Options);

    private static IRequestCompletionService Service(ApplicationDbContext context, bool enabled) =>
        new RequestCompletionService(
            context,
            Options.Create(new PostPaymentCompletionOptions
            {
                Enabled = enabled,
                EffectiveDateUtc = enabled
                    ? new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc)
                    : DateTime.MaxValue
            }),
            NullLogger<RequestCompletionService>.Instance);

    // ── Disabled: complete no-op ──

    [Fact]
    public async Task Group_phase_is_a_no_op_while_the_feature_is_disabled()
    {
        using var context = ModelOnlyContext();
        var service = Service(context, enabled: false);

        var result = await service.EvaluateGroupCompletionAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result.AnyGroupCompleted);
        Assert.Empty(result.CompletedGroupIds);
        Assert.False(result.ParentEvaluationRequired);
        Assert.Null(result.ErrorMessage);

        // No entity was tracked: the disabled path must not query or materialise anything.
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Parent_phase_is_a_no_op_while_the_feature_is_disabled()
    {
        using var context = ModelOnlyContext();
        var service = Service(context, enabled: false);

        var result = await service.EvaluateParentCompletionAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result.RequestCompleted);
        Assert.False(result.AlreadyCompleted);
        Assert.False(result.ConflictUnresolved);
        Assert.Null(result.CompletionCycleId);
        Assert.Null(result.ErrorMessage);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Disabled_evaluation_never_touches_the_database()
    {
        // The context points at a non-routable server: any attempt to query would throw.
        using var context = ModelOnlyContext();
        var service = Service(context, enabled: false);

        await service.EvaluateGroupCompletionAsync(Guid.NewGuid(), null, Guid.NewGuid());
        await service.EvaluateParentCompletionAsync(Guid.NewGuid(), Guid.NewGuid());
    }

    // ── Enabled: represented, not activated ──

    [Fact]
    public async Task Group_phase_is_not_activated_in_release_1()
    {
        using var context = ModelOnlyContext();
        var service = Service(context, enabled: true);

        var ex = await Assert.ThrowsAsync<NotImplementedException>(
            () => service.EvaluateGroupCompletionAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        Assert.Contains("Release 4", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parent_phase_is_not_activated_in_release_1()
    {
        using var context = ModelOnlyContext();
        var service = Service(context, enabled: true);

        var ex = await Assert.ThrowsAsync<NotImplementedException>(
            () => service.EvaluateParentCompletionAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Contains("Release 4", ex.Message, StringComparison.Ordinal);
    }

    // ── Contract surface ──

    [Fact]
    public void Result_contracts_expose_the_completion_identity_and_conflict_outcome()
    {
        // Phase 2 must be able to report the PERSISTED completion identity back to a loser or a
        // retry — that is what keeps REQUEST_COMPLETED to exactly one row per transition.
        var parent = new ParentCompletionResult
        {
            RequestCompleted = true,
            CompletionCycleId = Guid.NewGuid()
        };

        Assert.NotNull(parent.CompletionCycleId);
        Assert.False(ParentCompletionResult.NoOp.RequestCompleted);
        Assert.False(GroupCompletionResult.NoOp.AnyGroupCompleted);
    }

    // ── Model shape: the Release 1 foundation actually reached the model ──

    [Fact]
    public void Row_version_is_a_concurrency_token_on_request_and_po_group()
    {
        using var context = ModelOnlyContext();

        foreach (var clr in new[] { typeof(Request), typeof(RequestPoGroup) })
        {
            var property = context.Model.FindEntityType(clr)!.FindProperty("RowVersion")!;

            Assert.True(property.IsConcurrencyToken, $"{clr.Name}.RowVersion must be a concurrency token.");
            Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
            Assert.Equal("rowversion", property.GetColumnType());
        }
    }

    [Fact]
    public void Request_exposes_a_nullable_persisted_completion_cycle_id()
    {
        using var context = ModelOnlyContext();
        var property = context.Model.FindEntityType(typeof(Request))!.FindProperty("CompletionCycleId")!;

        Assert.True(property.IsNullable);
        Assert.Equal(typeof(Guid?), property.ClrType);
    }

    [Fact]
    public void Po_group_operation_invoice_status_defaults_to_unclassified_in_the_database()
    {
        using var context = ModelOnlyContext();
        var property = context.Model.FindEntityType(typeof(RequestPoGroup))!.FindProperty("OperationInvoiceStatus")!;

        Assert.False(property.IsNullable);
        Assert.Equal(RequestConstants.OperationInvoiceStatuses.Unclassified, property.GetDefaultValue());
    }

    [Fact]
    public void History_idempotency_key_is_nullable_and_uniquely_indexed_when_present()
    {
        using var context = ModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(RequestStatusHistory))!;

        var property = entity.FindProperty("IdempotencyKey")!;
        Assert.True(property.IsNullable);
        Assert.Equal(256, property.GetMaxLength());

        var index = entity.GetIndexes().Single(i => i.Properties.Any(p => p.Name == "IdempotencyKey"));
        Assert.True(index.IsUnique);
        Assert.Equal("[IdempotencyKey] IS NOT NULL", index.GetFilter());
    }

    [Fact]
    public void Waiting_fiscal_receipt_status_is_seeded_but_unused()
    {
        using var context = ModelOnlyContext();

        // Seed data lives on the design-time model, not the runtime read-optimized one.
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;
        var seeded = designTimeModel.FindEntityType(typeof(RequestStatus))!
            .GetSeedData()
            .Any(row => (string?)row["Code"] == RequestConstants.Statuses.WaitingFiscalReceipt);

        Assert.True(seeded, "WAITING_FISCAL_RECEIPT must be seeded by Release 1.");

        // ...and deliberately absent from every finalization helper array until Release 4.
        Assert.DoesNotContain(RequestConstants.PoGroupStatuses.WaitingFiscalReceipt,
            RequestConstants.PoGroupStatuses.ReadyToFinalize);
        Assert.DoesNotContain(RequestConstants.PoGroupStatuses.WaitingFiscalReceipt,
            RequestConstants.PoGroupStatuses.BlocksFinalization);
    }

    [Fact]
    public void Operation_invoice_reconciliation_table_exists_in_the_model()
    {
        using var context = ModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(OperationInvoiceReconciliation));

        Assert.NotNull(entity);
        Assert.Equal("OperationInvoiceReconciliations", entity!.GetTableName());
    }

    [Fact]
    public void Legacy_receipt_attachment_type_is_untouched_and_the_new_codes_are_distinct()
    {
        // TYPE_RECEIPT is semantically ambiguous (fiscal receipt vs delivery note) and historical
        // rows keep it exactly as recorded — rule R18. It must never collide with a new code.
        Assert.Equal("RECEIPT", RequestAttachment.TYPE_RECEIPT);
        Assert.Equal("FISCAL_RECEIPT", RequestAttachment.TYPE_FISCAL_RECEIPT);

        // Release 3 renamed FINAL_INVOICE to OPERATION_INVOICE ("final" wrongly implied
        // terminality) and added the PAYMENT origin code. Nothing carried the old value.
        Assert.Equal("OPERATION_INVOICE", RequestAttachment.TYPE_OPERATION_INVOICE);
        Assert.Equal("PAYMENT_SOURCE_DOCUMENT", RequestAttachment.TYPE_PAYMENT_SOURCE_DOCUMENT);

        Assert.NotEqual(RequestAttachment.TYPE_RECEIPT, RequestAttachment.TYPE_FISCAL_RECEIPT);
        Assert.NotEqual(RequestAttachment.TYPE_OPERATION_INVOICE, RequestAttachment.TYPE_PAYMENT_SOURCE_DOCUMENT);
    }
}

/// <summary>
/// Verifies the ambient-transaction guard of the post-commit phase. Requires the isolated
/// integration database; skips cleanly when it is unavailable (CI sets
/// ALPLA_INTEGRATION_TESTS_REQUIRED=true to make the absence fail loudly instead).
/// </summary>
[Collection("IntegrationTests")]
public class RequestCompletionServiceTransactionGuardTests
{
    [Fact]
    public async Task Parent_phase_refuses_to_run_inside_the_callers_transaction()
    {
        if (!IntegrationTestDatabase.CanConnect()) return;

        using var context = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());

        var service = new RequestCompletionService(
            context,
            Options.Create(new PostPaymentCompletionOptions
            {
                Enabled = true,
                EffectiveDateUtc = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc)
            }),
            NullLogger<RequestCompletionService>.Instance);

        await using var tx = await context.Database.BeginTransactionAsync();

        // Running the parent evaluation inside the caller's transaction is exactly the mistake
        // that leaves a multi-group request permanently open. It must fail loudly, not degrade.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EvaluateParentCompletionAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Contains("committed", ex.Message, StringComparison.OrdinalIgnoreCase);

        await tx.RollbackAsync();
    }
}
