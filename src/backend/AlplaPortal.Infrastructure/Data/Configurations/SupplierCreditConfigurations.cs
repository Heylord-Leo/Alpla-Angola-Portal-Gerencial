using AlplaPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlplaPortal.Infrastructure.Data.Configurations;

/// <summary>Supplier account credit — docs/SUPPLIER_CREDIT_PO_COVERAGE.md. One account per supplier × company × currency.</summary>
public class SupplierCreditAccountConfiguration : IEntityTypeConfiguration<SupplierCreditAccount>
{
    public void Configure(EntityTypeBuilder<SupplierCreditAccount> b)
    {
        b.ToTable("SupplierCreditAccounts");
        b.HasKey(e => e.Id);
        b.Property(e => e.CurrencyCode).IsRequired().HasMaxLength(10);
        b.Property(e => e.AccountNumber).IsRequired().HasMaxLength(20);
        b.Property(e => e.Status).IsRequired().HasMaxLength(16);
        b.Property(e => e.Notes).HasMaxLength(2000);
        b.Property(e => e.RowVersion).IsRowVersion();
        b.HasIndex(e => new { e.SupplierId, e.CompanyId, e.CurrencyCode }).IsUnique().HasDatabaseName("UX_SupplierCreditAccounts_Supplier_Company_Currency");
        b.HasIndex(e => e.AccountNumber).IsUnique().HasDatabaseName("UX_SupplierCreditAccounts_AccountNumber");
        b.HasOne(e => e.Supplier).WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SupplierCreditEntryConfiguration : IEntityTypeConfiguration<SupplierCreditEntry>
{
    public void Configure(EntityTypeBuilder<SupplierCreditEntry> b)
    {
        b.ToTable("SupplierCreditEntries");
        b.HasKey(e => e.Id);
        b.Property(e => e.EntryType).IsRequired().HasMaxLength(20);
        b.Property(e => e.SignedAmount).HasColumnType("decimal(18,2)");
        b.Property(e => e.BalanceAfter).HasColumnType("decimal(18,2)");
        b.Property(e => e.Reference).HasMaxLength(200);
        b.Property(e => e.Reason).IsRequired().HasMaxLength(1000);
        b.Property(e => e.Stage).HasMaxLength(10);
        b.Property(e => e.RequestNumberSnapshot).HasMaxLength(50);
        b.Property(e => e.PurchaseOrderNumberSnapshot).HasMaxLength(100);
        b.Property(e => e.IdempotencyKey).HasMaxLength(256);
        b.HasIndex(e => e.AccountId).HasDatabaseName("IX_SupplierCreditEntries_AccountId");
        b.HasIndex(e => e.RequestPoGroupId).HasDatabaseName("IX_SupplierCreditEntries_RequestPoGroupId");
        b.HasIndex(e => e.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL").HasDatabaseName("UX_SupplierCreditEntries_IdempotencyKey");
        b.HasOne(e => e.Account).WithMany(a => a.Entries).HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.Reservation).WithMany().HasForeignKey(e => e.ReservationId).OnDelete(DeleteBehavior.NoAction);
    }
}

public class SupplierCreditReservationConfiguration : IEntityTypeConfiguration<SupplierCreditReservation>
{
    public void Configure(EntityTypeBuilder<SupplierCreditReservation> b)
    {
        b.ToTable("SupplierCreditReservations");
        b.HasKey(e => e.Id);
        b.Property(e => e.Amount).HasColumnType("decimal(18,2)");
        b.Property(e => e.AdvanceCreditAmount).HasColumnType("decimal(18,2)");
        b.Property(e => e.BalanceCreditAmount).HasColumnType("decimal(18,2)");
        b.Property(e => e.AppliedAmount).HasColumnType("decimal(18,2)");
        b.Property(e => e.ReleasedHoldAmount).HasColumnType("decimal(18,2)");
        b.Property(e => e.ReversedAppliedAmount).HasColumnType("decimal(18,2)");
        b.Property(e => e.Status).IsRequired().HasMaxLength(20);
        b.Property(e => e.ReversalKind).HasMaxLength(24);
        b.Property(e => e.ReleaseReason).HasMaxLength(32);
        b.Property(e => e.ReleaseNote).HasMaxLength(1000);
        b.Property(e => e.ReversalReason).HasMaxLength(1000);
        b.Property(e => e.RowVersion).IsRowVersion();
        b.HasIndex(e => e.AccountId).HasDatabaseName("IX_SupplierCreditReservations_AccountId");
        b.HasIndex(e => e.RequestId).HasDatabaseName("IX_SupplierCreditReservations_RequestId");
        // One LIVE reservation per group — the last barrier against a duplicate hold or a double application.
        b.HasIndex(e => e.RequestPoGroupId).IsUnique()
            .HasFilter("[Status] IN ('RESERVED','PARTIALLY_APPLIED','APPLIED')")
            .HasDatabaseName("UX_SupplierCreditReservations_LiveGroup");
        b.HasOne(e => e.Account).WithMany(a => a.Reservations).HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.RequestPoGroup).WithMany().HasForeignKey(e => e.RequestPoGroupId).OnDelete(DeleteBehavior.Restrict);
    }
}
