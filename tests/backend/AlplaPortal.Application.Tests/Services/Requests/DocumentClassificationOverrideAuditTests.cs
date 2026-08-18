using System.Linq;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// The persistence guarantees behind the override audit.
///
/// <para>The rules that decide whether an override is admissible are covered in
/// <see cref="DocumentClassificationOverrideRecorderTests"/>. What is asserted here is the part
/// those rules depend on but cannot enforce alone: that the database will actually refuse a second
/// row for the same decision, and that an audit row cannot be destroyed by deleting the thing it
/// describes.</para>
///
/// <para>Model-shape assertions use a SQL Server context that is never connected — EF builds the
/// model without opening a connection, so no database is required.</para>
/// </summary>
public class DocumentClassificationOverrideAuditTests
{
    private const string NonConnectingConnectionString =
        "Server=alpla-dc-tests.invalid;Database=AlplaPortal_ModelOnly_DoNotConnect;" +
        "Trusted_Connection=True;TrustServerCertificate=True";

    private static ApplicationDbContext ModelOnlyContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(NonConnectingConnectionString)
            .Options);

    private static IEntityType Entity()
    {
        using var context = ModelOnlyContext();
        return context.Model.FindEntityType(typeof(DocumentClassificationOverride))!;
    }

    [Fact]
    public void The_audit_table_is_part_of_the_model()
    {
        Assert.NotNull(Entity());
    }

    [Fact]
    public void A_repeated_save_cannot_duplicate_history()
    {
        // This index — not the application-level existence check — is what makes the guarantee.
        // Two concurrent saves can both pass the check; only one can pass this.
        var index = Entity().GetIndexes()
            .Single(i => i.Properties.Count == 1 &&
                         i.Properties[0].Name == nameof(DocumentClassificationOverride.IdempotencyKey));

        Assert.True(index.IsUnique);
        Assert.Equal("UX_DocumentClassificationOverride_IdempotencyKey", index.GetDatabaseName());

        // Unlike RequestStatusHistory, every row here has a key by construction, so the index is
        // not filtered — a row without one must be impossible, not merely exempt.
        Assert.Null(index.GetFilter());
        Assert.False(Entity().FindProperty(nameof(DocumentClassificationOverride.IdempotencyKey))!.IsNullable);
    }

    [Fact]
    public void The_key_column_is_wide_enough_for_every_key_the_builder_can_produce()
    {
        var maxLength = Entity()
            .FindProperty(nameof(DocumentClassificationOverride.IdempotencyKey))!
            .GetMaxLength();

        Assert.Equal(PostPaymentIdempotencyKeys.MaxLength, maxLength);
    }

    [Fact]
    public void Deleting_the_thing_described_never_deletes_the_explanation()
    {
        // An audit row justifying an override must survive the removal of the quotation or request
        // it referred to — it is a record of a decision, not a property of an object.
        foreach (var fk in Entity().GetForeignKeys())
        {
            Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior);
        }
    }

    [Fact]
    public void Every_field_the_audit_promises_is_actually_stored()
    {
        var entity = Entity();

        foreach (var name in new[]
                 {
                     nameof(DocumentClassificationOverride.Context),
                     nameof(DocumentClassificationOverride.RequestId),
                     nameof(DocumentClassificationOverride.QuotationId),
                     nameof(DocumentClassificationOverride.AttachmentId),
                     nameof(DocumentClassificationOverride.SuggestedType),
                     nameof(DocumentClassificationOverride.Confidence),
                     nameof(DocumentClassificationOverride.TitleFound),
                     nameof(DocumentClassificationOverride.EvidenceJson),
                     nameof(DocumentClassificationOverride.ConflictingEvidenceJson),
                     nameof(DocumentClassificationOverride.SuggestionSource),
                     nameof(DocumentClassificationOverride.SelectedType),
                     nameof(DocumentClassificationOverride.Acknowledged),
                     nameof(DocumentClassificationOverride.Justification),
                     nameof(DocumentClassificationOverride.ActorUserId),
                     nameof(DocumentClassificationOverride.CreatedAtUtc)
                 })
        {
            Assert.True(entity.FindProperty(name) != null, name);
        }
    }

    [Fact]
    public void A_quotation_override_is_still_anchored_to_a_request()
    {
        var entity = Entity();

        // RequestId is required even for a quotation decision, so every override is reachable from
        // the request timeline; QuotationId is the optional dimension.
        Assert.False(entity.FindProperty(nameof(DocumentClassificationOverride.RequestId))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DocumentClassificationOverride.QuotationId))!.IsNullable);
    }

    [Fact]
    public void Confidence_is_stored_with_the_precision_the_thresholds_rely_on()
    {
        var columnType = Entity()
            .FindProperty(nameof(DocumentClassificationOverride.Confidence))!
            .GetColumnType();

        // 0.50 and 0.70 are decision boundaries; silent truncation would move them.
        Assert.Equal("decimal(5,4)", columnType);
    }
}
