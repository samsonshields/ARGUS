using Argus.Core.Entities;
using Microsoft.EntityFrameworkCore;
namespace Argus.Infrastructure.Data;

public sealed class ArgusDbContext(DbContextOptions<ArgusDbContext> options) : DbContext(options)
{
    public DbSet<Investigation> Investigations => Set<Investigation>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
    public DbSet<SourceTextSnapshot> SourceTextSnapshots => Set<SourceTextSnapshot>();
    public DbSet<IntelligenceEntity> Entities => Set<IntelligenceEntity>();
    public DbSet<EntityMention> EntityMentions => Set<EntityMention>();
    public DbSet<Relationship> Relationships => Set<Relationship>();
    public DbSet<RelationshipEvidence> RelationshipEvidence => Set<RelationshipEvidence>();
    public DbSet<AnalysisEvent> Events => Set<AnalysisEvent>();
    public DbSet<EventEvidence> EventEvidence => Set<EventEvidence>();
    public DbSet<EventEntity> EventEntities => Set<EventEntity>();
    public DbSet<LocationProfile> Locations => Set<LocationProfile>();
    public DbSet<LocationEvidence> LocationEvidence => Set<LocationEvidence>();
    public DbSet<GeographicFocus> GeographicFocus => Set<GeographicFocus>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimEvidence> ClaimEvidence => Set<ClaimEvidence>();
    public DbSet<GeneratedReport> GeneratedReports => Set<GeneratedReport>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Investigation>();
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Title).IsRequired().HasMaxLength(200);
        entity.Property(x => x.Description).IsRequired().HasMaxLength(5000);
        entity.Property(x => x.Region).HasMaxLength(200);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        entity.Property(x => x.CreatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        entity.Property(x => x.UpdatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        entity.HasIndex(x => x.Status);
        entity.HasIndex(x => x.UpdatedAt);
        entity.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Investigations_Title", "length(trim(Title)) BETWEEN 1 AND 200");
            t.HasCheckConstraint("CK_Investigations_Dates", "EndDate IS NULL OR StartDate IS NULL OR EndDate >= StartDate");
            t.HasCheckConstraint("CK_Investigations_Status", "Status IN ('Active', 'Archived')");
        });

        var source = modelBuilder.Entity<Source>();
        source.HasKey(x => x.Id);
        source.Property(x => x.Title).IsRequired().HasMaxLength(200);
        source.Property(x => x.Publisher).HasMaxLength(200);
        source.Property(x => x.OriginalUrl).HasMaxLength(2048);
        source.Property(x => x.ContentHash).IsRequired().HasMaxLength(64);
        source.Property(x => x.SourceType).HasConversion<string>();
        source.Property(x => x.ProcessingStatus).HasConversion<string>();
        source.Property(x => x.Reliability).HasConversion<string>();
        source.Property(x => x.RetrievedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        source.Property(x => x.ExtractedAt).HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);
        source.HasIndex(x => new { x.InvestigationId, x.ContentHash }).IsUnique();
        source.HasIndex(x => x.ContentHash);
        source.HasIndex(x => x.PublishedAt);
        source.HasIndex(x => x.ProcessingStatus);
        source.HasOne<Investigation>().WithMany().HasForeignKey(x => x.InvestigationId).OnDelete(DeleteBehavior.Restrict);
        source.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Sources_Title", "length(trim(Title)) BETWEEN 1 AND 200");
            t.HasCheckConstraint("CK_Sources_Size", "FileSize > 0");
            t.HasCheckConstraint("CK_Sources_Hash", "length(ContentHash) = 64");
            t.HasCheckConstraint("CK_Sources_Reliability", "Reliability IN ('A','B','C','D','E')");
            t.HasCheckConstraint("CK_Sources_Status", "ProcessingStatus IN ('Ready','Failed','Pending','Processing')");
            t.HasCheckConstraint("CK_Sources_Type", "SourceType IN ('Text','Pdf','PastedText')");
        });
        var audit = modelBuilder.Entity<AuditEvent>();
        audit.HasKey(x => x.Id);
        audit.HasIndex(x => new { x.InvestigationId, x.OccurredAt });
        audit.HasOne<Investigation>().WithMany().HasForeignKey(x => x.InvestigationId).OnDelete(DeleteBehavior.Restrict);
        audit.Property(x => x.OccurredAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        IntelligenceSchema.Configure(modelBuilder);
        RelationshipSchema.Configure(modelBuilder);
        TemporalSchema.Configure(modelBuilder);
        AssessmentSchema.Configure(modelBuilder);
    }
}
