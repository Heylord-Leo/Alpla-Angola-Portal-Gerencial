using AlplaPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlplaPortal.Infrastructure.Data.Configurations;

/// <summary>v2.249.4 — server-held P.O. document extraction evidence (docs/PO_DOCUMENT_VALIDATION_v2.249.4.md §3).</summary>
public class PoDocumentExtractionConfiguration : IEntityTypeConfiguration<PoDocumentExtraction>
{
    public void Configure(EntityTypeBuilder<PoDocumentExtraction> builder)
    {
        builder.ToTable("PoDocumentExtractions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.FileHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.FileName).IsRequired().HasMaxLength(255);
        builder.Property(e => e.ProviderStatusCode).HasMaxLength(40);
        builder.Property(e => e.Verdict).HasMaxLength(40);
        builder.Property(e => e.ReasonCode).HasMaxLength(60);
        builder.Property(e => e.Reference).HasMaxLength(100);
        builder.Property(e => e.Family).HasMaxLength(10);
        builder.Property(e => e.DocumentNumber).HasMaxLength(200);
        builder.Property(e => e.DocumentTitle).HasMaxLength(300);
        builder.Property(e => e.ClassificationType).HasMaxLength(40);
        builder.Property(e => e.ClassificationConfidence).HasPrecision(5, 4);
        builder.Property(e => e.SupplierName).HasMaxLength(255);
        builder.Property(e => e.SupplierTaxId).HasMaxLength(50);
        builder.Property(e => e.CurrencyCode).HasMaxLength(10);
        builder.Property(e => e.GrandTotal).HasPrecision(18, 2);
        builder.HasOne(e => e.Request).WithMany().HasForeignKey(e => e.RequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.RequestId, e.FileHash, e.CreatedAtUtc }).HasDatabaseName("IX_PoDocumentExtractions_Request_Hash_Created");
    }
}
