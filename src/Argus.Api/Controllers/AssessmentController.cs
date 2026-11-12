using System.ComponentModel.DataAnnotations;
using System.Text;
using Argus.Api.Services;
using Argus.Core.DTOs;
using Argus.Core.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Argus.Api.Controllers;

[ApiController, Route("api/v1")]
public sealed class AssessmentController(ClaimService claims, ReportService reports) : ControllerBase
{
    [HttpGet("investigations/{id:guid}/claims")]
    public Task<PageResult<ClaimSummary>> List(Guid id, [FromQuery] ReviewStatus? status, [FromQuery] ConfidenceLevel? confidence,
        [FromQuery] bool unresolvedOnly = false, [FromQuery, StringLength(200)] string? search = null,
        [FromQuery, Range(1, 1000000)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken ct = default)
        => claims.ListAsync(id, status, confidence, unresolvedOnly, search, page, pageSize, ct);
    [HttpGet("investigations/{id:guid}/assessments/summary")]
    public Task<AssessmentSummary> Summary(Guid id, CancellationToken ct) => claims.SummaryAsync(id, ct);
    [HttpGet("claims/{id:guid}")] public Task<ClaimDetail> Detail(Guid id, CancellationToken ct) => claims.GetAsync(id, ct);
    [HttpPost("investigations/{id:guid}/claims")]
    public Task<ClaimDetail> Create(Guid id, ClaimEditRequest request, CancellationToken ct) => claims.SaveAsync(id, null, request, ct);
    [HttpPut("claims/{id:guid}")]
    public async Task<ClaimDetail> Edit(Guid id, ClaimEditRequest request, CancellationToken ct) => await claims.SaveAsync((await claims.GetAsync(id, ct)).Claim.InvestigationId, id, request, ct);
    [HttpPost("claims/{id:guid}/evidence")]
    public Task Evidence(Guid id, ClaimEvidenceRequest request, CancellationToken ct) => claims.AddEvidenceAsync(id, request, ct);
    [HttpDelete("claims/{id:guid}/evidence/{evidenceId:guid}")]
    public Task RemoveEvidence(Guid id, Guid evidenceId, [FromQuery] int revision, CancellationToken ct) => claims.RemoveEvidenceAsync(id, evidenceId, revision, ct);
    [HttpPost("claims/{id:guid}/approve")]
    public Task Approve(Guid id, ClaimReviewRequest request, CancellationToken ct) => claims.ReviewAsync(id, true, request, ct);
    [HttpPost("claims/{id:guid}/reject")]
    public Task Reject(Guid id, ClaimReviewRequest request, CancellationToken ct) => claims.ReviewAsync(id, false, request, ct);
    [HttpPost("claims/{id:guid}/remove")]
    public Task Remove(Guid id, ReviewTemporalRequest request, CancellationToken ct) => claims.RemoveAsync(id, request, ct);
    [HttpGet("investigations/{id:guid}/reports/validation")]
    public Task<ReportValidation> Validation(Guid id, CancellationToken ct) => reports.ValidateAsync(id, ct);
    [HttpPost("investigations/{id:guid}/reports")]
    public Task<ReportResponse> Generate(Guid id, ReportRequest request, CancellationToken ct) => reports.GenerateAsync(id, request, ct);
    [HttpGet("investigations/{id:guid}/reports")]
    public Task<PageResult<ReportSummary>> Reports(Guid id, [FromQuery, Range(1, 1000000)] int page = 1, CancellationToken ct = default) => reports.ListAsync(id, page, ct);
    [HttpGet("reports/{id:guid}")] public Task<ReportResponse> Report(Guid id, CancellationToken ct) => reports.GetAsync(id, ct);
    [HttpGet("reports/{id:guid}/markdown")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var report = await reports.GetAsync(id, ct); Response.Headers.XContentTypeOptions = "nosniff";
        return File(Encoding.UTF8.GetBytes(report.Markdown), "text/markdown; charset=utf-8", report.Report.FileName);
    }
}
