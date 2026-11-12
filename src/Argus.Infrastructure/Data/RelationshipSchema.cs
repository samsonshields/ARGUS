using Argus.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Argus.Infrastructure.Data;

internal static class RelationshipSchema
{
    public static void Configure(ModelBuilder builder)
    {
        var relationship = builder.Entity<Relationship>();
        relationship.HasKey(x => x.Id);
        relationship.Property(x => x.Type).HasConversion<string>();
        relationship.Property(x => x.Status).HasConversion<string>();
        relationship.Property(x => x.Revision).IsConcurrencyToken();
        relationship.HasIndex(x => new { x.InvestigationId, x.Status });
        relationship.HasIndex(x => x.SourceEntityId);
        relationship.HasIndex(x => x.TargetEntityId);
        relationship.HasOne<Investigation>().WithMany().HasForeignKey(x => x.InvestigationId).OnDelete(DeleteBehavior.Restrict);
        relationship.HasOne<IntelligenceEntity>().WithMany().HasForeignKey(x => new { x.SourceEntityId, x.InvestigationId })
            .HasPrincipalKey(x => new { x.Id, x.InvestigationId }).OnDelete(DeleteBehavior.Restrict);
        relationship.HasOne<IntelligenceEntity>().WithMany().HasForeignKey(x => new { x.TargetEntityId, x.InvestigationId })
            .HasPrincipalKey(x => new { x.Id, x.InvestigationId }).OnDelete(DeleteBehavior.Restrict);
        relationship.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Relationships_Endpoints", "SourceEntityId <> TargetEntityId OR RetiredReason IS NOT NULL");
            t.HasCheckConstraint("CK_Relationships_Status", "Status IN ('PROPOSED','APPROVED','REJECTED')");
            t.HasCheckConstraint("CK_Relationships_Type", "Type IN ('MEMBER_OF','ASSOCIATED_WITH','LOCATED_IN','PARTICIPATED_IN','MENTIONED_WITH','CUSTOM')");
        });
        var evidence = builder.Entity<RelationshipEvidence>();
        evidence.HasKey(x => x.Id);
        evidence.HasIndex(x => x.OriginKey).IsUnique();
        evidence.Property(x => x.Status).HasConversion<string>();
        evidence.HasOne<Relationship>().WithMany().HasForeignKey(x => x.RelationshipId).OnDelete(DeleteBehavior.Restrict);
        evidence.HasOne<Source>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        evidence.HasOne<SourceTextSnapshot>().WithMany().HasForeignKey(x => new { x.SnapshotId, x.SourceId })
            .HasPrincipalKey(x => new { x.Id, x.SourceId }).OnDelete(DeleteBehavior.Restrict);
        evidence.HasOne<EntityMention>().WithMany().HasForeignKey(x => x.SourceMentionId).OnDelete(DeleteBehavior.Restrict);
        evidence.HasOne<EntityMention>().WithMany().HasForeignKey(x => x.TargetMentionId).OnDelete(DeleteBehavior.Restrict);
        evidence.ToTable(t =>
        {
            t.HasCheckConstraint("CK_RelationshipEvidence_Span", "Start >= 0 AND End > Start AND End - Start <= 8000");
            t.HasCheckConstraint("CK_RelationshipEvidence_Status", "Status IN ('PROPOSED','APPROVED','REJECTED')");
        });
    }
}
