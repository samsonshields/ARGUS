using System.ComponentModel.DataAnnotations;
using Argus.Api.Services;
using Argus.Core.DTOs;
using Argus.Core.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Argus.Api.Controllers;

[ApiController, Route("api/v1")]
public sealed class TemporalController(TemporalService service, ProcessingJobService jobs) : ControllerBase
{
    [HttpGet("investigations/{id:guid}/events")]
    [HttpGet("investigations/{id:guid}/timeline")]
    public Task<PageResult<EventResponse>> Events(Guid id, [FromQuery] ReviewStatus? status = null, [FromQuery] EventType? type = null,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] bool includeUnknown = true, [FromQuery] bool allStatuses = false,
        [FromQuery, Range(1, 1000000)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 50, CancellationToken ct = default)
        => service.EventsAsync(id, allStatuses ? null : status ?? ReviewStatus.APPROVED, type, from, to, includeUnknown, page, pageSize, ct);
    [HttpGet("events/{id:guid}")] public Task<EventResponse> Event(Guid id, CancellationToken ct) => service.EventAsync(id, ct);
    [HttpPost("investigations/{id:guid}/events")]
    public Task<EventResponse> CreateEvent(Guid id, EventEditRequest request, CancellationToken ct) => service.SaveEventAsync(id, null, request, ct);
    [HttpPut("events/{id:guid}")]
    public async Task<EventResponse> EditEvent(Guid id, EventEditRequest request, CancellationToken ct)
        => await service.SaveEventAsync((await service.EventAsync(id, ct)).InvestigationId, id, request, ct);
    [HttpGet("investigations/{id:guid}/locations")]
    public Task<List<LocationResponse>> Locations(Guid id, CancellationToken ct) => service.LocationsAsync(id, ct);
    [HttpGet("locations/{id:guid}")] public Task<LocationResponse> Location(Guid id, CancellationToken ct) => service.LocationAsync(id, ct);
    [HttpPost("investigations/{id:guid}/locations")]
    public Task<LocationResponse> CreateLocation(Guid id, LocationEditRequest request, CancellationToken ct) => service.SaveLocationAsync(id, null, request, ct);
    [HttpPut("locations/{id:guid}")]
    public async Task<LocationResponse> EditLocation(Guid id, LocationEditRequest request, CancellationToken ct)
        => await service.SaveLocationAsync((await service.LocationAsync(id, ct)).InvestigationId, id, request, ct);
    [HttpPost("events/{id:guid}/approve")] public Task ApproveEvent(Guid id, ReviewTemporalRequest request, CancellationToken ct) => service.ReviewAsync(id, false, true, request, ct);
    [HttpPost("events/{id:guid}/reject")] public Task RejectEvent(Guid id, ReviewTemporalRequest request, CancellationToken ct) => service.ReviewAsync(id, false, false, request, ct);
    [HttpPost("locations/{id:guid}/approve")] public Task ApproveLocation(Guid id, ReviewTemporalRequest request, CancellationToken ct) => service.ReviewAsync(id, true, true, request, ct);
    [HttpPost("locations/{id:guid}/reject")] public Task RejectLocation(Guid id, ReviewTemporalRequest request, CancellationToken ct) => service.ReviewAsync(id, true, false, request, ct);
    [HttpPost("events/{id:guid}/remove")] public Task RemoveEvent(Guid id, ReviewTemporalRequest request, CancellationToken ct) => service.RemoveAsync(id, false, request, ct);
    [HttpPost("locations/{id:guid}/remove")] public Task RemoveLocation(Guid id, ReviewTemporalRequest request, CancellationToken ct) => service.RemoveAsync(id, true, request, ct);
    [HttpGet("sources/{id:guid}/passages")]
    public Task<PageResult<PassageResponse>> Passages(Guid id, [FromQuery, Range(1, 1000000)] int page = 1, CancellationToken ct = default) => service.PassagesAsync(id, page, ct);
    [HttpPost("events/{id:guid}/evidence")] public Task AddEventEvidence(Guid id, CitationRequest request, CancellationToken ct) => service.AddEvidenceAsync(id, false, request, ct);
    [HttpPost("locations/{id:guid}/evidence")] public Task AddLocationEvidence(Guid id, CitationRequest request, CancellationToken ct) => service.AddEvidenceAsync(id, true, request, ct);
    [HttpDelete("events/{id:guid}/evidence/{evidenceId:guid}")]
    public Task RemoveEventEvidence(Guid id, Guid evidenceId, [FromQuery] int revision, CancellationToken ct) => service.RemoveEvidenceAsync(id, false, evidenceId, revision, ct);
    [HttpDelete("locations/{id:guid}/evidence/{evidenceId:guid}")]
    public Task RemoveLocationEvidence(Guid id, Guid evidenceId, [FromQuery] int revision, CancellationToken ct) => service.RemoveEvidenceAsync(id, true, evidenceId, revision, ct);
    [HttpPost("sources/{id:guid}/process-events")]
    public async Task<IActionResult> Generate(Guid id, CancellationToken ct)
    { var job = await jobs.QueueAsync(id, ct, JobKind.EVENTS); return Accepted($"/api/v1/processing-jobs/{job.Id}", job); }
    [HttpGet("investigations/{id:guid}/map")]
    public Task<MapResponse> Map(Guid id, [FromQuery] EventType? type = null, [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null, [FromQuery] bool includeUnknown = true, CancellationToken ct = default) => service.MapAsync(id, type, from, to, includeUnknown, ct);
    [HttpPut("investigations/{id:guid}/map/focus")]
    public Task Focus(Guid id, MapFocusRequest request, CancellationToken ct) => service.SaveFocusAsync(id, request, ct);
}
