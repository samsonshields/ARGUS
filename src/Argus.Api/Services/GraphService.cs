using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Exceptions;
using Argus.Core.Services;
using Argus.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Argus.Api.Services;

public sealed class GraphService(ArgusDbContext db, RelationshipService relationships)
{
    public async Task<GraphResponse> GetAsync(Guid investigationId, EntityType? entityType, RelationshipType? relationshipType,
        bool includeProposed, string? search, CancellationToken ct)
    {
        if (!await db.Investigations.AnyAsync(x => x.Id == investigationId, ct)) throw new SourceException(404, "Investigation not found.");
        var query = db.Entities.AsNoTracking().Where(x => x.InvestigationId == investigationId && x.MergedIntoId == null);
        if (entityType.HasValue) query = query.Where(x => x.Type == entityType);
        if (!string.IsNullOrWhiteSpace(search)) { var normalized = IntelligenceEntity.Normalize(search); query = query.Where(x => x.NormalizedName.Contains(normalized)); }
        var entities = await query.OrderBy(x => x.Id).Take(2001).ToListAsync(ct);
        if (entities.Count > 2000) throw new SourceException(413, "Graph exceeds 2000 entities. Apply a name or entity-type filter.");
        var ids = entities.Select(x => x.Id).ToArray();
        var edgesQuery = db.Relationships.AsNoTracking().Where(x => x.InvestigationId == investigationId && x.RetiredReason == null
            && ids.Contains(x.SourceEntityId) && ids.Contains(x.TargetEntityId) && (x.Status == ReviewStatus.APPROVED || (includeProposed && x.Status == ReviewStatus.PROPOSED)));
        if (relationshipType.HasValue) edgesQuery = edgesQuery.Where(x => x.Type == relationshipType);
        var edges = await edgesQuery.OrderBy(x => x.Id).Take(10001).ToListAsync(ct);
        if (edges.Count > 10000) throw new SourceException(413, "Graph exceeds 10000 relationships. Apply relationship filters.");
        var summaries = await relationships.SummariesAsync(edges, ct);
        var graphEdges = summaries.Where(x => x.ApprovedEvidenceCount > 0 || (x.Status == ReviewStatus.PROPOSED && x.ProposedEvidenceCount > 0))
            .Select(x => new GraphEdge(x.Id, x.SourceEntityId, x.TargetEntityId, x.Type, x.Status, x.CustomLabel ?? x.Type.ToString(),
                Relationship.Directed(x.Type), x.ApprovedEvidenceCount, x.ProposedEvidenceCount)).ToList();
        return GraphAlgorithms.Measure(entities.Select(x => new GraphNode(x.Id, x.Name, x.Type, 0, 0, 0, 0, 0)).ToList(), graphEdges, includeProposed);
    }
    public async Task<GraphPath> PathAsync(Guid id, Guid sourceId, Guid targetId, EntityType? entityType, RelationshipType? relationshipType,
        bool includeProposed, bool respectDirection, string? search, CancellationToken ct)
    {
        var graph = await GetAsync(id, entityType, relationshipType, includeProposed, search, ct);
        if (!graph.Nodes.Any(x => x.Id == sourceId) || !graph.Nodes.Any(x => x.Id == targetId))
            throw new SourceException(404, "Path endpoints must belong to the filtered investigation graph.");
        return GraphAlgorithms.ShortestPath(graph, sourceId, targetId, respectDirection);
    }
}
