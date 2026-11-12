using System.Text.Json;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Exceptions;
using Argus.Core.Services;
using Argus.Infrastructure.Data;
using Argus.Infrastructure.ExternalServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Argus.Api.Services;

public sealed class EntityReviewService(ArgusDbContext db, IOptions<IntelligenceOptions> options)
{
    public async Task<IntelligenceStatistics> StatisticsAsync(CancellationToken ct) => new(
        await db.EntityMentions.CountAsync(x => x.Status == ReviewStatus.PROPOSED, ct),
        await db.Entities.CountAsync(x => x.MergedIntoId == null, ct),
        await db.ProcessingJobs.CountAsync(x => x.State == JobState.PENDING || x.State == JobState.RUNNING, ct),
        await db.ProcessingJobs.CountAsync(x => x.State == JobState.FAILED, ct),
        await db.Relationships.CountAsync(x => x.RetiredReason == null && x.Status == ReviewStatus.PROPOSED, ct),
        await db.Relationships.CountAsync(x => x.RetiredReason == null && x.Status == ReviewStatus.APPROVED, ct));

    public async Task<PageResult<MentionResponse>> MentionsAsync(Guid investigationId, Guid? sourceId, Guid? entityId,
        ReviewStatus? status, int page, int pageSize, CancellationToken ct)
    {
        if (!await db.Investigations.AnyAsync(x => x.Id == investigationId, ct)) throw new SourceException(404, "Investigation not found.");
        var query = db.EntityMentions.AsNoTracking().Where(x => x.InvestigationId == investigationId);
        if (sourceId.HasValue) query = query.Where(x => x.SourceId == sourceId);
        if (entityId.HasValue) query = query.Where(x => x.EntityId == entityId);
        if (status.HasValue) query = query.Where(x => x.Status == status);
        var count = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.SourceId).ThenBy(x => x.Start).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var sourceIds = items.Select(x => x.SourceId).Distinct().ToArray();
        var titles = await db.Sources.Where(x => sourceIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Title, ct);
        return new(items.Select(x => x.ToResponse(titles[x.SourceId])).ToList(), count, page, pageSize);
    }
    public async Task<MentionEvidence> EvidenceAsync(Guid id, CancellationToken ct)
    {
        var mention = await FindMentionAsync(id, ct);
        var snapshot = await db.SourceTextSnapshots.SingleAsync(x => x.Id == mention.SnapshotId, ct);
        var start = Math.Max(0, mention.Start - 160);
        if (!EntityText.Boundary(snapshot.Text, start)) start--;
        var end = Math.Min(snapshot.Text.Length, mention.End + 160);
        if (!EntityText.Boundary(snapshot.Text, end)) end++;
        var title = await db.Sources.Where(x => x.Id == mention.SourceId).Select(x => x.Title).SingleAsync(ct);
        return new(mention.ToResponse(title), snapshot.Text[start..mention.Start], snapshot.Text[mention.Start..mention.End], snapshot.Text[mention.End..end], snapshot.TextHash);
    }
    public async Task<EntityResponse> ApproveAsync(Guid id, ApproveMentionRequest request, CancellationToken ct)
    {
        var mention = await FindMentionAsync(id, ct);
        if (mention.Status != ReviewStatus.PROPOSED) throw new SourceException(409, "This candidate has already been reviewed. Reload the review list.");
        IntelligenceEntity entity;
        if (request.EntityId.HasValue)
        {
            entity = await FindEntityAsync(request.EntityId.Value, ct);
            if (entity.InvestigationId != mention.InvestigationId || entity.MergedIntoId.HasValue)
                throw new SourceException(409, "Select an active entity in the same investigation.");
            if (IntelligenceEntity.Normalize(request.Name) != entity.NormalizedName || request.Type != entity.Type)
                throw new SourceException(409, "Selected entity metadata differs. Edit the entity separately or choose a new entity.");
        }
        else
        {
            entity = new(mention.InvestigationId, request);
            db.Entities.Add(entity);
        }
        mention.Approve(entity);
        entity.TouchForAssociation();
        Audit(mention.InvestigationId, "EntityMention", mention.Id, "Approved", new { mention.EntityId, mention.Status, mention.SnapshotId });
        await db.SaveChangesAsync(ct);
        return entity.ToResponse(await db.EntityMentions.CountAsync(x => x.EntityId == entity.Id, ct));
    }
    public async Task RejectAsync(Guid id, CancellationToken ct)
    {
        var mention = await FindMentionAsync(id, ct);
        if (mention.Status != ReviewStatus.PROPOSED) throw new SourceException(409, "This candidate has already been reviewed.");
        mention.Reject();
        Audit(mention.InvestigationId, "EntityMention", mention.Id, "Rejected", new { mention.Status, mention.SnapshotId });
        await db.SaveChangesAsync(ct);
    }
    public async Task<PageResult<EntityResponse>> EntitiesAsync(Guid investigationId, string? search, int page, int pageSize, CancellationToken ct)
    {
        if (!await db.Investigations.AnyAsync(x => x.Id == investigationId, ct)) throw new SourceException(404, "Investigation not found.");
        var query = db.Entities.AsNoTracking().Where(x => x.InvestigationId == investigationId && x.MergedIntoId == null);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = IntelligenceEntity.Normalize(search);
            query = query.Where(x => x.NormalizedName.Contains(term));
        }
        var count = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(await ResponsesAsync(items, ct), count, page, pageSize);
    }
    public async Task<EntityResponse> GetEntityAsync(Guid id, CancellationToken ct)
    {
        var entity = await FindEntityAsync(id, ct);
        return entity.ToResponse(await db.EntityMentions.CountAsync(x => x.EntityId == id, ct));
    }
    public async Task<List<EntitySuggestion>> SuggestionsAsync(Guid mentionId, CancellationToken ct)
    {
        var mention = await FindMentionAsync(mentionId, ct);
        var entities = await db.Entities.AsNoTracking().Where(x => x.InvestigationId == mention.InvestigationId &&
            x.MergedIntoId == null && x.Type == mention.OriginalType).ToListAsync(ct);
        var matches = entities.Select(entity => new
        {
            Entity = entity,
            Score = entity.Aliases().Append(entity.Name).Max(name => EntityText.Similarity(mention.Text, name))
        })
            .Where(x => x.Score >= options.Value.MatchThreshold).OrderByDescending(x => x.Score).ThenBy(x => x.Entity.Id).Take(10).ToList();
        var responses = (await ResponsesAsync(matches.Select(x => x.Entity).ToList(), ct)).ToDictionary(x => x.Id);
        return matches.Select(x => new EntitySuggestion(responses[x.Entity.Id], x.Score)).ToList();
    }
    public async Task<EntityResponse> UpdateAsync(Guid id, EntityEditRequest request, CancellationToken ct)
    {
        var entity = await FindEntityAsync(id, ct);
        if (entity.MergedIntoId.HasValue) throw new SourceException(409, "This entity has been merged. Edit its target entity.");
        var before = JsonSerializer.Serialize(entity.ToResponse(0));
        entity.Update(request);
        Audit(entity.InvestigationId, "Entity", entity.Id, "Corrected", entity.ToResponse(0), before);
        await db.SaveChangesAsync(ct);
        return await GetEntityAsync(id, ct);
    }
    public async Task<EntityResponse> MergeAsync(MergeEntitiesRequest request, CancellationToken ct)
    {
        if (!request.Confirm || request.SourceId == request.TargetId) throw new SourceException(400, "Confirm a merge of two different entities.");
        var source = await FindEntityAsync(request.SourceId, ct);
        var target = await FindEntityAsync(request.TargetId, ct);
        var before = JsonSerializer.Serialize(new { Source = source.ToResponse(0), Target = target.ToResponse(0) });
        try { source.MergeInto(target); }
        catch (InvalidOperationException) { throw new SourceException(409, "Merge requires active entities of the same investigation and type. Correct classifications first if needed."); }
        var mentions = await db.EntityMentions.Where(x => x.EntityId == source.Id).ToListAsync(ct);
        foreach (var mention in mentions) mention.MoveTo(target);
        var relationships = await db.Relationships.Where(x => x.SourceEntityId == source.Id || x.TargetEntityId == source.Id).ToListAsync(ct);
        foreach (var relationship in relationships)
        {
            var prior = JsonSerializer.Serialize(relationship);
            relationship.RedirectEntity(source.Id, target.Id);
            Audit(source.InvestigationId, "Relationship", relationship.Id, "EndpointsMerged", relationship, prior);
        }
        Audit(source.InvestigationId, "Entity", source.Id, "Merged", new { Source = source.ToResponse(0), Target = target.ToResponse(0) }, before);
        await db.SaveChangesAsync(ct);
        return await GetEntityAsync(target.Id, ct);
    }
    private async Task<List<EntityResponse>> ResponsesAsync(List<IntelligenceEntity> entities, CancellationToken ct)
    {
        var ids = entities.Select(x => x.Id).ToArray();
        var counts = await db.EntityMentions.Where(x => x.EntityId.HasValue && ids.Contains(x.EntityId.Value))
            .GroupBy(x => x.EntityId!.Value).Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return entities.Select(x => x.ToResponse(counts.GetValueOrDefault(x.Id))).ToList();
    }
    private async Task<EntityMention> FindMentionAsync(Guid id, CancellationToken ct)
        => await db.EntityMentions.FindAsync([id], ct) ?? throw new SourceException(404, "Entity mention not found.");
    private async Task<IntelligenceEntity> FindEntityAsync(Guid id, CancellationToken ct)
        => await db.Entities.FindAsync([id], ct) ?? throw new SourceException(404, "Entity not found.");
    private void Audit(Guid investigationId, string recordType, Guid recordId, string action, object after, string? before = null)
        => db.AuditEvents.Add(new AuditEvent
        {
            InvestigationId = investigationId,
            RecordType = recordType,
            RecordId = recordId,
            Action = action,
            BeforeJson = before,
            AfterJson = JsonSerializer.Serialize(after)
        });
}
