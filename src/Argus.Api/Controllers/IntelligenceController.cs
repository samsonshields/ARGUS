using System.ComponentModel.DataAnnotations;
using Argus.Api.Services;
using Argus.Core.DTOs;
using Argus.Core.Enums;
using Argus.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Argus.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class IntelligenceController(ProcessingJobService jobs, EntityReviewService review, IIntelligenceClient intelligence) : ControllerBase
{
    [HttpGet("intelligence/health")]
    public async Task<IActionResult> Health(CancellationToken ct) => await intelligence.IsReadyAsync(ct)
        ? Ok(new { status = "ready" }) : Problem(statusCode: 503, title: "Start the local NLP service with its trained English model.");
    [HttpGet("intelligence/statistics")]
    public Task<IntelligenceStatistics> Statistics(CancellationToken ct) => review.StatisticsAsync(ct);
    [HttpPost("sources/{id:guid}/process")]
    public async Task<IActionResult> Queue(Guid id, CancellationToken ct)
    {
        var job = await jobs.QueueAsync(id, ct);
        return AcceptedAtAction(nameof(Job), new { id = job.Id }, job);
    }
    [HttpGet("processing-jobs/{id:guid}")]
    public Task<JobResponse> Job(Guid id, CancellationToken ct) => jobs.GetAsync(id, ct);
    [HttpGet("processing-jobs")]
    public Task<PageResult<JobResponse>> Jobs([FromQuery] Guid? investigationId, [FromQuery] Guid? sourceId,
        [FromQuery, Range(1, 1000000)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 50, CancellationToken ct = default)
        => jobs.ListAsync(investigationId, sourceId, page, pageSize, ct);
    [HttpPost("processing-jobs/{id:guid}/cancel")]
    public Task<JobResponse> Cancel(Guid id, CancellationToken ct) => jobs.CancelAsync(id, ct);
    [HttpPost("processing-jobs/{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        var job = await jobs.RetryAsync(id, ct);
        return AcceptedAtAction(nameof(Job), new { id = job.Id }, job);
    }
    [HttpGet("investigations/{id:guid}/mentions")]
    public async Task<ActionResult<PageResult<MentionResponse>>> Mentions(Guid id, [FromQuery] Guid? sourceId,
        [FromQuery] Guid? entityId, [FromQuery] ReviewStatus? status, [FromQuery, Range(1, 1000000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 50, CancellationToken ct = default,
        [FromQuery] EntityType? type = null, [FromQuery, StringLength(200)] string? search = null)
    {
        if (status.HasValue && !Enum.IsDefined(status.Value)) return Problem(statusCode: 400, title: "Invalid review status.");
        if (type.HasValue && !Enum.IsDefined(type.Value)) return Problem(statusCode: 400, title: "Invalid entity type.");
        return Ok(await review.MentionsAsync(id, sourceId, entityId, status, page, pageSize, ct, type, search));
    }
    [HttpGet("mentions/{id:guid}/evidence")]
    public Task<MentionEvidence> Evidence(Guid id, CancellationToken ct) => review.EvidenceAsync(id, ct);
    [HttpGet("investigations/{id:guid}/mentions/bulk-approval-preview")]
    public async Task<ActionResult<BulkMentionPreview>> BulkPreview(Guid id, [FromQuery] Guid? sourceId,
        [FromQuery] EntityType? type, [FromQuery, StringLength(200)] string? search, CancellationToken ct)
    {
        if (type.HasValue && !Enum.IsDefined(type.Value)) return Problem(statusCode: 400, title: "Invalid entity type.");
        return Ok(await review.PreviewBulkApprovalAsync(id, sourceId, type, search, ct));
    }
    [HttpPost("investigations/{id:guid}/mentions/approve-bulk")]
    public Task<BulkMentionApprovalResult> BulkApprove(Guid id, BulkApproveMentionsRequest request, CancellationToken ct)
        => review.BulkApproveAsync(id, request, ct);
    [HttpGet("mentions/{id:guid}/suggestions")]
    public Task<List<EntitySuggestion>> Suggestions(Guid id, CancellationToken ct) => review.SuggestionsAsync(id, ct);
    [HttpPost("mentions/{id:guid}/approve")]
    public Task<EntityResponse> Approve(Guid id, ApproveMentionRequest request, CancellationToken ct) => review.ApproveAsync(id, request, ct);
    [HttpPost("mentions/{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct) { await review.RejectAsync(id, ct); return NoContent(); }
    [HttpGet("investigations/{id:guid}/entities")]
    public Task<PageResult<EntityResponse>> Entities(Guid id, [FromQuery, StringLength(200)] string? search,
        [FromQuery, Range(1, 1000000)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 50, CancellationToken ct = default)
        => review.EntitiesAsync(id, search, page, pageSize, ct);
    [HttpGet("entities/{id:guid}")]
    public Task<EntityResponse> Entity(Guid id, CancellationToken ct) => review.GetEntityAsync(id, ct);
    [HttpPut("entities/{id:guid}")]
    public Task<EntityResponse> Update(Guid id, EntityEditRequest request, CancellationToken ct) => review.UpdateAsync(id, request, ct);
    [HttpPost("entities/merge")]
    public Task<EntityResponse> Merge(MergeEntitiesRequest request, CancellationToken ct) => review.MergeAsync(request, ct);
}
