using Argus.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Argus.Infrastructure.Data;

internal static class AssessmentSchema
{
    public static void Configure(ModelBuilder builder)
    {
        var claim = builder.Entity<Claim>(); claim.HasKey(x => x.Id);
        claim.HasIndex(x => new { x.InvestigationId, x.Status, x.UpdatedAt });
        claim.Property(x => x.Revision).IsConcurrencyToken();
        claim.Property(x => x.Status).HasConversion<string>(); claim.Property(x => x.ConfidenceLevel).HasConversion<string>();
        claim.Property(x => x.CreatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        claim.Property(x => x.UpdatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        claim.Property(x => x.DeletedAt).HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);
        claim.HasOne<Investigation>().WithMany().HasForeignKey(x => x.InvestigationId).OnDelete(DeleteBehavior.Restrict);
        claim.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Claims_Statement", "length(trim(Statement)) BETWEEN 1 AND 2000");
            t.HasCheckConstraint("CK_Claims_Status", "Status IN ('PROPOSED','APPROVED','REJECTED')");
            t.HasCheckConstraint("CK_Claims_Confidence", "ConfidenceLevel IN ('LOW','MODERATE','HIGH','INSUFFICIENT_INFORMATION')");
            t.HasCheckConstraint("CK_Claims_Resolution", "(ContradictionsResolved = 0 AND ContradictionResolution = '') OR (ContradictionsResolved = 1 AND length(trim(ContradictionResolution)) BETWEEN 1 AND 5000)");
        });
        var evidence = builder.Entity<ClaimEvidence>(); evidence.HasKey(x => x.Id);
        evidence.HasIndex(x => new { x.ClaimId, x.SnapshotId, x.Start, x.End }).IsUnique();
        evidence.HasIndex(x => new { x.ClaimId, x.Active, x.Relationship });
        evidence.Property(x => x.Relationship).HasConversion<string>();
        evidence.Property(x => x.CreatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        evidence.HasOne<Claim>().WithMany().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Restrict);
        evidence.HasOne<SourceTextSnapshot>().WithMany().HasForeignKey(x => new { x.SnapshotId, x.SourceId })
            .HasPrincipalKey(x => new { x.Id, x.SourceId }).OnDelete(DeleteBehavior.Restrict);
        evidence.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ClaimEvidence_Span", "Start >= 0 AND End > Start AND End - Start <= 8000");
            t.HasCheckConstraint("CK_ClaimEvidence_Relationship", "Relationship IN ('SUPPORTS','CONTRADICTS','CONTEXT')");
        });
        var report = builder.Entity<GeneratedReport>(); report.HasKey(x => x.Id);
        report.HasIndex(x => new { x.InvestigationId, x.GeneratedAt });
        report.Property(x => x.GeneratedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        report.HasOne<Investigation>().WithMany().HasForeignKey(x => x.InvestigationId).OnDelete(DeleteBehavior.Restrict);
        report.ToTable(t => t.HasCheckConstraint("CK_Reports_Hash", "length(ContentHash) = 64"));
    }
}
