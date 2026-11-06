using AlplaPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlplaPortal.Infrastructure.Data.Configurations;

/// <summary>Machine-card designs of the Operations TV boards (docs/OPERATIONS_VIANA3_PRODUCTION_MONITOR.md §11).</summary>
public class OperationsCardDesignConfiguration : IEntityTypeConfiguration<OperationsCardDesign>
{
    public void Configure(EntityTypeBuilder<OperationsCardDesign> builder)
    {
        builder.ToTable("OperationsCardDesigns");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.BoardKey).IsRequired().HasMaxLength(60);
        builder.Property(e => e.DesignJson).IsRequired();
        builder.HasIndex(e => new { e.BoardKey, e.MachineId }).IsUnique().HasDatabaseName("UX_OperationsCardDesigns_Board_Machine");
        builder.HasOne(e => e.UpdatedByUser).WithMany().HasForeignKey(e => e.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>Uploaded raster images of the card designs (two-phase deletion, hash reuse).</summary>
public class OperationsDesignAssetConfiguration : IEntityTypeConfiguration<OperationsDesignAsset>
{
    public void Configure(EntityTypeBuilder<OperationsDesignAsset> builder)
    {
        builder.ToTable("OperationsDesignAssets");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.BoardKey).IsRequired().HasMaxLength(60);
        builder.Property(e => e.FileName).IsRequired().HasMaxLength(255);
        builder.Property(e => e.ContentType).IsRequired().HasMaxLength(60);
        builder.Property(e => e.Extension).IsRequired().HasMaxLength(10);
        builder.Property(e => e.Sha256).IsRequired().HasMaxLength(64);
        builder.Property(e => e.StorageReference).IsRequired().HasMaxLength(120);
        builder.Property(e => e.LastPurgeError).HasMaxLength(500);
        // Identical content is stored once per board — among ACTIVE assets only (tombstones are excluded).
        builder.HasIndex(e => new { e.BoardKey, e.Sha256 })
            .IsUnique()
            .HasFilter("[DeletedAtUtc] IS NULL")
            .HasDatabaseName("UX_OperationsDesignAssets_Board_Sha256_Active");
        builder.HasIndex(e => new { e.BoardKey, e.DeletedAtUtc }).HasDatabaseName("IX_OperationsDesignAssets_Board_Deleted");
        builder.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
