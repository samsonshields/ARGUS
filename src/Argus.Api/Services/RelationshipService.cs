using System.Text.Json;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Exceptions;
using Argus.Core.Interfaces;
using Argus.Core.Services;
using Argus.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Argus.Api.Services;

public sealed class RelationshipService(ArgusDbContext db, IRelationshipClient client, ProcessingJobService jobs)
{
    public async Task<GenerationQueueResult> QueueInvestigationAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Investigations.AnyAsync(x => x.Id == id, ct)) throw new SourceException(404, "Investigation not found.");
        var sources = await db.Sources.Where(x => x.InvestigationId == id && db.EntityMentions.Where(m => m.SourceId == x.Id
            && m.Status == ReviewStatus.APPROVED).Select(m => m.EntityId).Distinct().Count() >= 2).OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
        if (sources.Count > 1000) throw new SourceException(413, "Queue at most 1000 sources per request; use source processing for larger workspaces.");
        var queued = new List<JobResponse>(); var busy = 0;
        foreach (var source in sources)
        {
            if (await jobs.HasActiveAsync(source.Id, ct)) { busy++; continue; }
            queued.Add(jobs.AddForImport(source, JobKind.RELATIONSHIPS).ToResponse());
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 })
        { throw new SourceException(409, "A source was queued concurrently. Reload and retry generation."); }
        return new(queued, busy);
    }
    public async Task<PageResult<RelationshipSummary>> ForEntityAsync(Guid id, int page, int pageSize, CancellationToken ct)
    {
        var entity = await db.Entities.FindAsync([id], ct) ?? throw new SourceException(404, "Entity not found.");
        return await ListAsync(entity.InvestigationId, id, null, null, page, pageSize, ct);
    }
    // Stage every result first. The runner commits proposals and job completion in one transaction.
    public async Task<int> PrepareForSourceAsync(Source source, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            var snapshotIds = await db.EntityMentions.Where(x => x.SourceId == source.Id && x.Status == ReviewStatus.APPROVED)
                .Select(x => x.SnapshotId).Distinct().OrderBy(x => x).Take(51).ToListAsync(deadline.Token);
            if (snapshotIds.Count > 50) throw new ProcessingException("relationship_limit", "Too many evidence versions for one relationship-generation job.");
            var count = 0;
            foreach (var snapshotId in snapshotIds)
            {
                var snapshot = await db.SourceTextSnapshots.SingleAsync(x => x.Id == snapshotId, deadline.Token);
                var mentions = await db.EntityMentions.AsNoTracking().Where(x => x.SnapshotId == snapshotId && x.Status == ReviewStatus.APPROVED)
                    .OrderBy(x => x.Start).ThenBy(x => x.Id).ToListAsync(deadline.Token);
                if (mentions.Select(x => x.EntityId).Distinct().Count() < 2) continue;
                if (mentions.Count > 5000) throw new ProcessingException("relationship_limit", "Too many approved mentions in one snapshot.");
                var request = new RelationshipProcessingRequest("1", source.Id, snapshot.TextHash, snapshot.Text,
                    mentions.Select(x => new RelationshipMention(x.Id, x.EntityId!.Value, x.Text, x.Start, x.End)).ToList());
                var response = await client.ProcessAsync(request, deadline.Token);
                RelationshipContract.Validate(request, response);
                count += response.Relationships.Count;
                if (count > 5000) throw new ProcessingException("relationship_limit", "Co-mention generation exceeds 5000 passages per job.");
                // Entity merges may have occurred during HTTP processing. Re-read canonical links.
                var currentMentions = await db.EntityMentions.AsNoTracking().Where(x => x.SnapshotId == snapshotId && x.Status == ReviewStatus.APPROVED)
                    .ToDictionaryAsync(x => x.Id, deadline.Token);
                var existingKeys = (await db.RelationshipEvidence.Where(x => x.SnapshotId == snapshotId).Select(x => x.OriginKey).ToListAsync(deadline.Token)).ToHashSet();
                var relationships = await db.Relationships.Where(x => x.InvestigationId == source.InvestigationId && x.RetiredReason == null
                    && x.Type == RelationshipType.MENTIONED_WITH && x.Status != ReviewStatus.REJECTED).ToListAsync(deadline.Token);
                foreach (var candidate in response.Relationships)
                {
                    var key = RelationshipEvidence.Key(snapshot.Id, candidate);
                    if (!existingKeys.Add(key)) continue;
                    var first = currentMentions[candidate.SourceMentionId].EntityId!.Value;
                    var second = currentMentions[candidate.TargetMentionId].EntityId!.Value;
                    if (first == second) continue;
                    var endpoints = Relationship.Pair(first, second);
                    var entities = await db.Entities.Where(x => (x.Id == first || x.Id == second) && x.MergedIntoId == null).ToListAsync(deadline.Token);
                    if (entities.Count != 2) throw new ProcessingException("concurrent_update", "Entity records changed during generation. Retry the job.", true);
                    foreach (var entity in entities) entity.TouchForAssociation();
                    var relationship = relationships.FirstOrDefault(x => x.SourceEntityId == endpoints.Source && x.TargetEntityId == endpoints.Target);
                    if (relationship is null)
                    {
                        relationship = new(source.InvestigationId, endpoints.Source, endpoints.Target);
                        db.Relationships.Add(relationship); relationships.Add(relationship);
                    }
                    relationship.Touch();
                    db.RelationshipEvidence.Add(new(relationship, snapshot, candidate));
                }
            }
            return count;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ProcessingException("relationship_timeout", "Relationship generation exceeded its total deadline.", true); }
    }

    public async Task<PageResult<RelationshipSummary>> ListAsync(Guid investigationId, Guid? entityId, ReviewStatus? status,
        RelationshipType? type, int page, int pageSize, CancellationToken ct)
    {
        if (!await db.Investigations.AnyAsync(x => x.Id == investigationId, ct)) throw new SourceException(404, "Investigation not found.");
        var query = db.Relationships.AsNoTracking().Where(x => x.InvestigationId == investigationId && x.RetiredReason == null);
        if (entityId.HasValue) query = query.Where(x => x.SourceEntityId == entityId || x.TargetEntityId == entityId);
        if (status.HasValue) query = query.Where(x => x.Status == status);
        if (type.HasValue) query = query.Where(x => x.Type == type);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(await SummariesAsync(items, ct), total, page, pageSize);
    }
    public async Task<RelationshipDetail> GetAsync(Guid id, CancellationToken ct, int page = 1)
    {
        var relationship = await FindAsync(id, ct);
        var query = db.RelationshipEvidence.AsNoTracking().Where(x => x.RelationshipId == id);
        var total = await query.CountAsync(ct);
        var evidence = await query.OrderBy(x => x.SourceId).ThenBy(x => x.Start).ThenBy(x => x.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        var snapshotIds = evidence.Select(x => x.SnapshotId).Distinct().ToArray(); var sourceIds = evidence.Select(x => x.SourceId).Distinct().ToArray();
        var snapshots = await db.SourceTextSnapshots.Where(x => snapshotIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var titles = await db.Sources.Where(x => sourceIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Title, ct);
        return new((await SummariesAsync([relationship], ct)).Single(), evidence.Select(x => new RelationshipEvidenceResponse(x.Id, x.SourceId,
            titles[x.SourceId], x.SnapshotId, x.SourceMentionId, x.TargetMentionId, x.Start, x.End,
            snapshots[x.SnapshotId].Text[x.Start..x.End], snapshots[x.SnapshotId].TextHash, x.Status, x.Method)).ToList(), total, page, 20);
    }
    public async Task<RelationshipDetail> ApproveAsync(Guid id, RelationshipReviewRequest request, CancellationToken ct)
    {
        var relationship = await FindAsync(id, ct);
        var before = JsonSerializer.Serialize(relationship);
        var evidence = await db.RelationshipEvidence.Where(x => x.RelationshipId == id && request.EvidenceIds.Contains(x.Id)).ToListAsync(ct);
        if (evidence.Count != request.EvidenceIds.Distinct().Count() || evidence.Any(x => x.Status == ReviewStatus.REJECTED))
            throw new SourceException(400, "Select reviewable evidence belonging to this relationship.");
        try { relationship.Approve(request); }
        catch (InvalidOperationException ex) { throw new SourceException(409, ex.Message); }
        foreach (var item in evidence) item.Approve();
        Audit(relationship, "Approved", before);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }
    public async Task RejectAsync(Guid id, CancellationToken ct)
    {
        var relationship = await FindAsync(id, ct); var before = JsonSerializer.Serialize(relationship);
        try { relationship.Reject(); }
        catch (InvalidOperationException ex) { throw new SourceException(409, ex.Message); }
        foreach (var evidence in await db.RelationshipEvidence.Where(x => x.RelationshipId == id).ToListAsync(ct)) evidence.Reject();
        Audit(relationship, "Rejected", before);
        await db.SaveChangesAsync(ct);
    }
    public async Task<List<RelationshipSummary>> SummariesAsync(List<Relationship> relationships, CancellationToken ct)
    {
        var ids = relationships.Select(x => x.Id).ToArray();
        var entityIds = relationships.SelectMany(x => new[] { x.SourceEntityId, x.TargetEntityId }).Distinct().ToArray();
        var names = await db.Entities.Where(x => entityIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var counts = await db.RelationshipEvidence.Where(x => ids.Contains(x.RelationshipId)).GroupBy(x => new { x.RelationshipId, x.Status })
            .Select(x => new { x.Key.RelationshipId, x.Key.Status, Count = x.Count() }).ToDictionaryAsync(x => (x.RelationshipId, x.Status), x => x.Count, ct);
        return relationships.Select(x => new RelationshipSummary(x.Id, x.InvestigationId, x.SourceEntityId, names[x.SourceEntityId],
            x.TargetEntityId, names[x.TargetEntityId], x.Type, x.Status, x.CustomLabel, x.Notes, x.Origin, x.RetiredReason,
            counts.GetValueOrDefault((x.Id, ReviewStatus.APPROVED)), counts.GetValueOrDefault((x.Id, ReviewStatus.PROPOSED)))).ToList();
    }
    private async Task<Relationship> FindAsync(Guid id, CancellationToken ct) => await db.Relationships.FindAsync([id], ct)
        ?? throw new SourceException(404, "Relationship not found.");
    private void Audit(Relationship relationship, string action, string before) => db.AuditEvents.Add(new AuditEvent
    {
        InvestigationId = relationship.InvestigationId,
        RecordType = "Relationship",
        RecordId = relationship.Id,
        Action = action,
        BeforeJson = before,
        AfterJson = JsonSerializer.Serialize(relationship)
    });
}
