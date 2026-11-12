using Argus.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Argus.Infrastructure.Data;

internal static class IntelligenceSchema
{
    public static void Configure(ModelBuilder builder)
    {
        var job = builder.Entity<ProcessingJob>();
        job.HasKey(x => x.Id);
        job.Property(x => x.State).HasConversion<string>();
        job.Property(x => x.Kind).HasConversion<string>().HasDefaultValue(Argus.Core.Enums.JobKind.ENTITIES);
        job.Property(x => x.Revision).IsConcurrencyToken();
        job.Property(x => x.CreatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        job.Property(x => x.UpdatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        job.Property(x => x.NextAttemptAt).HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);
        job.HasIndex(x => new { x.State, x.NextAttemptAt });
        job.HasIndex(x => x.SourceId).IsUnique().HasFilter("State IN ('PENDING','RUNNING')");
        job.HasOne<Source>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        job.HasOne<Investigation>().WithMany().HasForeignKey(x => x.InvestigationId).OnDelete(DeleteBehavior.Restrict);
        job.HasOne<SourceTextSnapshot>().WithMany().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        job.ToTable(t => t.HasCheckConstraint("CK_Jobs_State", "State IN ('PENDING','RUNNING','COMPLETED','FAILED','CANCELLED')"));

        var snapshot = builder.Entity<SourceTextSnapshot>();
        snapshot.HasKey(x => x.Id);
        snapshot.HasAlternateKey(x => new { x.Id, x.SourceId });
        snapshot.HasIndex(x => new { x.SourceId, x.TextHash }).IsUnique();
        snapshot.HasOne<Source>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        snapshot.Property(x => x.CreatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        var entity = builder.Entity<IntelligenceEntity>();
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.Id, x.InvestigationId });
        entity.HasIndex(x => new { x.InvestigationId, x.NormalizedName });
        entity.Property(x => x.Name).HasMaxLength(200);
        entity.Property(x => x.Type).HasConversion<string>();
        entity.Property(x => x.Revision).IsConcurrencyToken();
        entity.HasOne<Investigation>().WithMany().HasForeignKey(x => x.InvestigationId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<IntelligenceEntity>().WithMany().HasForeignKey(x => new { x.MergedIntoId, x.InvestigationId })
            .HasPrincipalKey(x => new { x.Id, x.InvestigationId }).OnDelete(DeleteBehavior.Restrict);

        var mention = builder.Entity<EntityMention>();
        mention.HasKey(x => x.Id);
        mention.Property(x => x.Status).HasConversion<string>();
        mention.Property(x => x.OriginalType).HasConversion<string>();
        mention.Property(x => x.Revision).IsConcurrencyToken();
        mention.Property(x => x.ReviewedAt).HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);
        mention.HasIndex(x => new { x.SnapshotId, x.Start, x.End, x.OriginalType }).IsUnique();
        mention.HasIndex(x => new { x.InvestigationId, x.Status });
        mention.HasIndex(x => new { x.EntityId, x.Status });
        mention.HasOne<Source>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        mention.HasOne<SourceTextSnapshot>().WithMany().HasForeignKey(x => new { x.SnapshotId, x.SourceId })
            .HasPrincipalKey(x => new { x.Id, x.SourceId }).OnDelete(DeleteBehavior.Restrict);
        mention.HasOne<ProcessingJob>().WithMany().HasForeignKey(x => x.ProcessingJobId).OnDelete(DeleteBehavior.Restrict);
        mention.HasOne<IntelligenceEntity>().WithMany().HasForeignKey(x => new { x.EntityId, x.InvestigationId })
            .HasPrincipalKey(x => new { x.Id, x.InvestigationId }).OnDelete(DeleteBehavior.Restrict);
        mention.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Mentions_Span", "Start >= 0 AND End > Start");
            t.HasCheckConstraint("CK_Mentions_Review", "(Status = 'APPROVED' AND EntityId IS NOT NULL) OR (Status IN ('PROPOSED','REJECTED') AND EntityId IS NULL)");
        });
    }
}
