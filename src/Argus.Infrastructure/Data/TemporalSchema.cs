using Argus.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Argus.Infrastructure.Data;

internal static class TemporalSchema
{
    public static void Configure(ModelBuilder builder)
    {
        var item = builder.Entity<AnalysisEvent>();
        item.HasKey(x => x.Id);
        item.HasAlternateKey(x => new { x.Id, x.InvestigationId });
        item.Property(x => x.Revision).IsConcurrencyToken();
        item.Property(x => x.Status).HasConversion<string>();
        item.Property(x => x.DatePrecision).HasConversion<string>();
        item.Property(x => x.Type).HasConversion<string>();
        item.Property(x => x.CreatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        item.Property(x => x.DeletedAt).HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);
        item.HasIndex(x => x.OriginKey).IsUnique();
        item.HasIndex(x => new { x.InvestigationId, x.Status, x.StartAt });
        item.HasOne<Investigation>().WithMany().HasForeignKey(x => x.InvestigationId).OnDelete(DeleteBehavior.Restrict);
        item.HasOne<ProcessingJob>().WithMany().HasForeignKey(x => x.ProcessingJobId).OnDelete(DeleteBehavior.Restrict);
        item.HasOne<LocationProfile>().WithMany().HasForeignKey(x => new { x.LocationId, x.InvestigationId })
            .HasPrincipalKey(x => new { x.Id, x.InvestigationId }).OnDelete(DeleteBehavior.Restrict);
        item.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Events_Title", "length(trim(Title)) BETWEEN 1 AND 200");
            t.HasCheckConstraint("CK_Events_Dates", "(DatePrecision = 'UNKNOWN' AND StartAt IS NULL AND EndAt IS NULL) OR (DatePrecision IN ('EXACT','MONTH','YEAR') AND StartAt IS NOT NULL AND EndAt IS NOT NULL AND EndAt >= StartAt)");
            t.HasCheckConstraint("CK_Events_Status", "Status IN ('PROPOSED','APPROVED','REJECTED')");
            t.HasCheckConstraint("CK_Events_Type", "Type IN ('OTHER','MEETING','MOVEMENT','PUBLICATION','CUSTOM')");
        });
        var location = builder.Entity<LocationProfile>();
        location.HasKey(x => x.Id);
        location.HasAlternateKey(x => new { x.Id, x.InvestigationId });
        location.Property(x => x.Revision).IsConcurrencyToken();
        location.Property(x => x.Status).HasConversion<string>();
        location.Property(x => x.Precision).HasConversion<string>();
        location.Property(x => x.Basis).HasConversion<string>();
        location.Property(x => x.CreatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        location.Property(x => x.DeletedAt).HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);
        location.HasIndex(x => x.EntityId).IsUnique().HasFilter("DeletedAt IS NULL");
        location.HasOne<Investigation>().WithMany().HasForeignKey(x => x.InvestigationId).OnDelete(DeleteBehavior.Restrict);
        location.HasOne<IntelligenceEntity>().WithMany().HasForeignKey(x => new { x.EntityId, x.InvestigationId })
            .HasPrincipalKey(x => new { x.Id, x.InvestigationId }).OnDelete(DeleteBehavior.Restrict);
        location.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Locations_Coordinates", "(Precision = 'UNKNOWN' AND Latitude IS NULL AND Longitude IS NULL AND UncertaintyMeters IS NULL) OR (Precision IN ('EXACT','APPROXIMATE') AND Latitude IS NOT NULL AND Longitude IS NOT NULL AND Latitude BETWEEN -90 AND 90 AND Longitude BETWEEN -180 AND 180 AND ((Precision = 'EXACT' AND (UncertaintyMeters IS NULL OR UncertaintyMeters = 0)) OR (Precision = 'APPROXIMATE' AND UncertaintyMeters IS NOT NULL AND UncertaintyMeters > 0 AND UncertaintyMeters <= 20000000)))");
            t.HasCheckConstraint("CK_Locations_Status", "Status IN ('PROPOSED','APPROVED','REJECTED')");
            t.HasCheckConstraint("CK_Locations_Basis", "Basis IN ('EXPLICIT_SELECTION','VERIFIED_SOURCE_METADATA')");
        });
        var link = builder.Entity<EventEntity>();
        link.HasKey(x => new { x.EventId, x.EntityId });
        link.HasOne<AnalysisEvent>().WithMany().HasForeignKey(x => new { x.EventId, x.InvestigationId })
            .HasPrincipalKey(x => new { x.Id, x.InvestigationId }).OnDelete(DeleteBehavior.Restrict);
        link.HasOne<IntelligenceEntity>().WithMany().HasForeignKey(x => new { x.EntityId, x.InvestigationId })
            .HasPrincipalKey(x => new { x.Id, x.InvestigationId }).OnDelete(DeleteBehavior.Restrict);
        var eventEvidence = builder.Entity<EventEvidence>();
        eventEvidence.HasKey(x => x.Id);
        eventEvidence.Property(x => x.CreatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        eventEvidence.HasIndex(x => new { x.EventId, x.SnapshotId, x.Start, x.End }).IsUnique();
        eventEvidence.HasIndex(x => new { x.EventId, x.Active });
        eventEvidence.HasOne<AnalysisEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        eventEvidence.HasOne<SourceTextSnapshot>().WithMany().HasForeignKey(x => new { x.SnapshotId, x.SourceId })
            .HasPrincipalKey(x => new { x.Id, x.SourceId }).OnDelete(DeleteBehavior.Restrict);
        eventEvidence.ToTable(t => t.HasCheckConstraint("CK_EventEvidence_Span", "Start >= 0 AND End > Start AND End - Start <= 8000"));
        var locationEvidence = builder.Entity<LocationEvidence>();
        locationEvidence.HasKey(x => x.Id);
        locationEvidence.Property(x => x.CreatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        locationEvidence.HasIndex(x => new { x.LocationId, x.SnapshotId, x.Start, x.End }).IsUnique();
        locationEvidence.HasIndex(x => new { x.LocationId, x.Active });
        locationEvidence.HasOne<LocationProfile>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        locationEvidence.HasOne<SourceTextSnapshot>().WithMany().HasForeignKey(x => new { x.SnapshotId, x.SourceId })
            .HasPrincipalKey(x => new { x.Id, x.SourceId }).OnDelete(DeleteBehavior.Restrict);
        locationEvidence.ToTable(t => t.HasCheckConstraint("CK_LocationEvidence_Span", "Start >= 0 AND End > Start AND End - Start <= 8000"));
        var focus = builder.Entity<GeographicFocus>();
        focus.HasKey(x => x.InvestigationId);
        focus.HasOne<Investigation>().WithOne().HasForeignKey<GeographicFocus>(x => x.InvestigationId).OnDelete(DeleteBehavior.Cascade);
        focus.ToTable(t => t.HasCheckConstraint("CK_Focus_Range", "Latitude BETWEEN -90 AND 90 AND Longitude BETWEEN -180 AND 180 AND Zoom BETWEEN 1 AND 18"));
    }
}
