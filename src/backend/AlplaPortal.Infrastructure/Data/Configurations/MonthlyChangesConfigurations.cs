using AlplaPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlplaPortal.Infrastructure.Data.Configurations;

// ─── HR Monthly Changes Middleware ─────────────────────────────────────────────
// Entities: MCProcessingRun, MCAttendanceSnapshot, MCMonthlyChangeItem,
//           MCPrimaveraCodeMapping, MCDetectionThreshold, MCExportBatch,
//           MCExportRow, MCProcessingLog
// ────────────────────────────────────────────────────────────────────────────────

public class MCProcessingRunConfiguration : IEntityTypeConfiguration<MCProcessingRun>
{
    public void Configure(EntityTypeBuilder<MCProcessingRun> builder)
    {
        builder.ToTable("MCProcessingRuns");
        builder.HasKey(r => r.Id);

        // ─── Scope fields ───
        builder.Property(r => r.EntityName).IsRequired().HasMaxLength(100);
        builder.Property(r => r.StatusCode).IsRequired().HasMaxLength(30);
        builder.Property(r => r.ErrorMessage).HasMaxLength(2000);

        // Prevent duplicate active runs for the same entity+month.
        // Business logic enforces "only one DRAFT/SYNCING/NEEDS_REVIEW per entity+month",
        // but the DB can have multiple CLOSED/EXPORTED runs for history.
        // An application-level check handles this; the index aids performance.
        builder.HasIndex(r => new { r.EntityId, r.Year, r.Month });
        builder.HasIndex(r => r.StatusCode);
        builder.HasIndex(r => r.CreatedAtUtc);

        // ─── CreatedByUser ───
        // NoAction: avoids SQL Server multiple cascade path.
        // Application layer guards user deletion.
        builder.HasOne(r => r.CreatedByUser)
            .WithMany()
            .HasForeignKey(r => r.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        // ─── Child collections ───
        builder.HasMany(r => r.Snapshots)
            .WithOne(s => s.ProcessingRun)
            .HasForeignKey(s => s.ProcessingRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Items)
            .WithOne(i => i.ProcessingRun)
            .HasForeignKey(i => i.ProcessingRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Exports)
            .WithOne(e => e.ProcessingRun)
            .HasForeignKey(e => e.ProcessingRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Logs)
            .WithOne(l => l.ProcessingRun)
            .HasForeignKey(l => l.ProcessingRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class MCAttendanceSnapshotConfiguration : IEntityTypeConfiguration<MCAttendanceSnapshot>
{
    public void Configure(EntityTypeBuilder<MCAttendanceSnapshot> builder)
    {
        builder.ToTable("MCAttendanceSnapshots");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.EmployeeCode).IsRequired().HasMaxLength(50);
        builder.Property(s => s.EmployeeName).IsRequired().HasMaxLength(200);
        builder.Property(s => s.ScheduleCode).HasMaxLength(50);
        builder.Property(s => s.ScheduleSigla).HasMaxLength(20);
        builder.Property(s => s.FirstEntry).HasMaxLength(10);
        builder.Property(s => s.FirstExit).HasMaxLength(10);
        builder.Property(s => s.AnomalyDescription).HasMaxLength(1000);
        builder.Property(s => s.Justification).HasMaxLength(1000);

        // High-volume: composite indexes for detection engine queries.
        builder.HasIndex(s => new { s.ProcessingRunId, s.InnuxEmployeeId, s.Date })
            .IsUnique()
            .HasDatabaseName("IX_MCAttendanceSnapshots_Run_Employee_Date");
        builder.HasIndex(s => new { s.ProcessingRunId, s.Date });
    }
}

public class MCMonthlyChangeItemConfiguration : IEntityTypeConfiguration<MCMonthlyChangeItem>
{
    public void Configure(EntityTypeBuilder<MCMonthlyChangeItem> builder)
    {
        builder.ToTable("MCMonthlyChangeItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.EmployeeCode).IsRequired().HasMaxLength(50);
        builder.Property(i => i.EmployeeName).IsRequired().HasMaxLength(200);
        builder.Property(i => i.OccurrenceType).IsRequired().HasMaxLength(50);
        builder.Property(i => i.DetectionRule).IsRequired().HasMaxLength(500);
        builder.Property(i => i.StatusCode).IsRequired().HasMaxLength(30);
        builder.Property(i => i.ScheduleCode).HasMaxLength(50);
        builder.Property(i => i.PrimaveraCode).HasMaxLength(50);
        builder.Property(i => i.PrimaveraCodeDescription).HasMaxLength(200);
        builder.Property(i => i.CostCenter).HasMaxLength(50);
        builder.Property(i => i.OriginalPrimaveraCode).HasMaxLength(50);
        builder.Property(i => i.OverrideBy).HasMaxLength(200);
        builder.Property(i => i.OverrideReason).HasMaxLength(500);
        builder.Property(i => i.AnomalyReason).HasMaxLength(500);
        builder.Property(i => i.ResolutionNote).HasMaxLength(500);
        builder.Property(i => i.ResolvedBy).HasMaxLength(200);
        builder.Property(i => i.ExclusionReason).HasMaxLength(500);
        builder.Property(i => i.ExcludedBy).HasMaxLength(200);

        builder.Property(i => i.Hours).HasColumnType("decimal(9,2)");
        builder.Property(i => i.OriginalHours).HasColumnType("decimal(9,2)");

        // ─── Snapshot FK ───
        // Restrict: preserve traceability — snapshot must exist for audit.
        builder.HasOne(i => i.Snapshot)
            .WithMany()
            .HasForeignKey(i => i.SnapshotId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes for review UI queries
        builder.HasIndex(i => new { i.ProcessingRunId, i.StatusCode });
        builder.HasIndex(i => new { i.ProcessingRunId, i.EmployeeCode });
        builder.HasIndex(i => new { i.ProcessingRunId, i.Date });
        builder.HasIndex(i => i.IsAnomaly).HasFilter("[IsAnomaly] = 1")
            .HasDatabaseName("IX_MCMonthlyChangeItems_Anomalies");
    }
}

public class MCPrimaveraCodeMappingConfiguration : IEntityTypeConfiguration<MCPrimaveraCodeMapping>
{
    public void Configure(EntityTypeBuilder<MCPrimaveraCodeMapping> builder)
    {
        builder.ToTable("MCPrimaveraCodeMappings");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.OccurrenceType).IsRequired().HasMaxLength(50);
        builder.Property(m => m.PrimaveraCode).IsRequired().HasMaxLength(50);
        builder.Property(m => m.PrimaveraCodeDescription).IsRequired().HasMaxLength(200);
        builder.Property(m => m.DefaultHoursFormula).HasMaxLength(100);
        builder.Property(m => m.ScheduleCode).HasMaxLength(50);
        builder.Property(m => m.Notes).HasMaxLength(500);
        builder.Property(m => m.UpdatedBy).HasMaxLength(200);

        builder.HasIndex(m => new { m.OccurrenceType, m.IsActive, m.Priority })
            .HasDatabaseName("IX_MCPrimaveraCodeMappings_Lookup");
    }
}

public class MCDetectionThresholdConfiguration : IEntityTypeConfiguration<MCDetectionThreshold>
{
    public void Configure(EntityTypeBuilder<MCDetectionThreshold> builder)
    {
        builder.ToTable("MCDetectionThresholds");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.ThresholdType).IsRequired().HasMaxLength(50);
        builder.Property(t => t.ScheduleCode).HasMaxLength(50);
        builder.Property(t => t.Notes).HasMaxLength(500);
        builder.Property(t => t.UpdatedBy).HasMaxLength(200);

        builder.HasIndex(t => new { t.ThresholdType, t.IsActive })
            .HasDatabaseName("IX_MCDetectionThresholds_Lookup");
    }
}

public class MCExportBatchConfiguration : IEntityTypeConfiguration<MCExportBatch>
{
    public void Configure(EntityTypeBuilder<MCExportBatch> builder)
    {
        builder.ToTable("MCExportBatches");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.StatusCode).IsRequired().HasMaxLength(30);
        builder.Property(e => e.FileName).IsRequired().HasMaxLength(255);
        builder.Property(e => e.GeneratedByEmail).IsRequired().HasMaxLength(200);
        builder.Property(e => e.ImportConfirmedBy).HasMaxLength(200);
        builder.Property(e => e.Notes).HasMaxLength(1000);

        // Config audit snapshot (Amendment §5).
        // Column type nvarchar(max) for large JSON payloads.
        builder.Property(e => e.ConfigSnapshotJson).HasColumnType("nvarchar(max)");
        builder.Property(e => e.ConfigSnapshotHash).HasMaxLength(64); // SHA256 hex = 64 chars

        builder.HasIndex(e => e.ProcessingRunId);
        builder.HasIndex(e => e.GeneratedAtUtc);

        // ─── Child rows ───
        builder.HasMany(e => e.Rows)
            .WithOne(r => r.ExportBatch)
            .HasForeignKey(r => r.ExportBatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class MCExportRowConfiguration : IEntityTypeConfiguration<MCExportRow>
{
    public void Configure(EntityTypeBuilder<MCExportRow> builder)
    {
        builder.ToTable("MCExportRows");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.EmployeeCode).IsRequired().HasMaxLength(50);
        builder.Property(r => r.EmployeeName).IsRequired().HasMaxLength(200);
        builder.Property(r => r.PrimaveraCode).IsRequired().HasMaxLength(50);
        builder.Property(r => r.CostCenter).HasMaxLength(50);
        builder.Property(r => r.Hours).HasColumnType("decimal(9,2)");

        // Traceability back to the source item.
        // Restrict: export row must always reference the originating item.
        builder.HasOne(r => r.MonthlyChangeItem)
            .WithMany()
            .HasForeignKey(r => r.MonthlyChangeItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.ExportBatchId);
    }
}

public class MCProcessingLogConfiguration : IEntityTypeConfiguration<MCProcessingLog>
{
    public void Configure(EntityTypeBuilder<MCProcessingLog> builder)
    {
        builder.ToTable("MCProcessingLogs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.EventType).IsRequired().HasMaxLength(50);
        builder.Property(l => l.Message).IsRequired().HasMaxLength(2000);
        builder.Property(l => l.Details).HasColumnType("nvarchar(max)");
        builder.Property(l => l.Actor).HasMaxLength(200);

        builder.HasIndex(l => new { l.ProcessingRunId, l.OccurredAtUtc });
        builder.HasIndex(l => l.EventType);
    }
}
