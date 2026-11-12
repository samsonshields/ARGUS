using System.ComponentModel.DataAnnotations;
using Argus.Api.Services;
using Argus.Core.DTOs;
using Argus.Core.Enums;
using Argus.Core.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Argus.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class GraphController(GraphService graph, RelationshipService relationships, ProcessingJobService jobs) : ControllerBase
{
    [HttpGet("investigations/{id:guid}/graph")]
    public Task<GraphResponse> Graph(Guid id, [FromQuery] EntityType? entityType, [FromQuery] RelationshipType? relationshipType,
        [FromQuery] bool includeProposed = true, [FromQuery, StringLength(200)] string? search = null, CancellationToken ct = default)
    { Validate(entityType, relationshipType, null); return graph.GetAsync(id, entityType, relationshipType, includeProposed, search, ct); }
    [HttpGet("investigations/{id:guid}/graph/path")]
    public Task<GraphPath> Path(Guid id, [FromQuery] Guid sourceId, [FromQuery] Guid targetId, [FromQuery] EntityType? entityType,
        [FromQuery] RelationshipType? relationshipType, [FromQuery] bool includeProposed = true, [FromQuery] bool respectDirection = false,
        [FromQuery, StringLength(200)] string? search = null, CancellationToken ct = default)
    { Validate(entityType, relationshipType, null); return graph.PathAsync(id, sourceId, targetId, entityType, relationshipType, includeProposed, respectDirection, search, ct); }
    [HttpGet("investigations/{id:guid}/relationships")]
    public Task<PageResult<RelationshipSummary>> List(Guid id, [FromQuery] ReviewStatus? status, [FromQuery] RelationshipType? type,
        [FromQuery, Range(1, 1000000)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 50, CancellationToken ct = default)
    { Validate(null, type, status); return relationships.ListAsync(id, null, status, type, page, pageSize, ct); }
    [HttpGet("entities/{id:guid}/relationships")]
    public async Task<PageResult<RelationshipSummary>> Neighbors(Guid id, [FromQuery, Range(1, 1000000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 50, CancellationToken ct = default)
    {
        return await relationships.ForEntityAsync(id, page, pageSize, ct);
    }
    [HttpGet("relationships/{id:guid}")]
    public Task<RelationshipDetail> Detail(Guid id, [FromQuery, Range(1, 1000000)] int evidencePage = 1, CancellationToken ct = default) => relationships.GetAsync(id, ct, evidencePage);
    [HttpPost("relationships/{id:guid}/approve")]
    public Task<RelationshipDetail> Approve(Guid id, RelationshipReviewRequest request, CancellationToken ct) => relationships.ApproveAsync(id, request, ct);
    [HttpPost("relationships/{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct) { await relationships.RejectAsync(id, ct); return NoContent(); }
    [HttpPost("sources/{id:guid}/process-relationships")]
    public async Task<IActionResult> GenerateSource(Guid id, CancellationToken ct)
    { var job = await jobs.QueueAsync(id, ct, JobKind.RELATIONSHIPS); return Accepted($"/api/v1/processing-jobs/{job.Id}", job); }
    [HttpPost("investigations/{id:guid}/relationships/generate")]
    public async Task<IActionResult> Generate(Guid id, CancellationToken ct)
    {
        return Accepted(await relationships.QueueInvestigationAsync(id, ct));
    }
    private static void Validate(EntityType? entity, RelationshipType? relationship, ReviewStatus? review)
    {
        if ((entity.HasValue && !Enum.IsDefined(entity.Value)) || (relationship.HasValue && !Enum.IsDefined(relationship.Value))
            || (review.HasValue && !Enum.IsDefined(review.Value))) throw new SourceException(400, "Unknown graph filter value.");
    }
}
