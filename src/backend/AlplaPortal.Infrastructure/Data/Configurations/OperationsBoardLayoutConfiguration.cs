using AlplaPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlplaPortal.Infrastructure.Data.Configurations;

/// <summary>Shared TV board screen layouts (docs/OPERATIONS_VIANA3_PRODUCTION_MONITOR.md §9).</summary>
public class OperationsBoardLayoutConfiguration : IEntityTypeConfiguration<OperationsBoardLayout>
{
    public void Configure(EntityTypeBuilder<OperationsBoardLayout> builder)
    {
        builder.ToTable("OperationsBoardLayouts");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.BoardKey).IsRequired().HasMaxLength(60);
        builder.Property(e => e.ScreenKey).IsRequired().HasMaxLength(40);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(80);
        builder.Property(e => e.LayoutJson).IsRequired();
        builder.HasIndex(e => new { e.BoardKey, e.ScreenKey }).IsUnique().HasDatabaseName("UX_OperationsBoardLayouts_Board_Screen");
        builder.HasOne(e => e.UpdatedByUser).WithMany().HasForeignKey(e => e.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
