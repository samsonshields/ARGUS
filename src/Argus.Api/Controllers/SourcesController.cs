using System.ComponentModel.DataAnnotations;
using System.Text;
using Argus.Api.Services;
using Argus.Core.DTOs;
using Argus.Core.Exceptions;
using Argus.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Argus.Api.Controllers;

public sealed class SourceUploadRequest : SourceMetadataRequest
{
    [Required] public IFormFile File { get; set; } = null!;
}

[ApiController]
[Route("api/v1")]
public sealed class SourcesController(SourceService service, IOptions<DocumentOptions> options) : ControllerBase
{
    [HttpGet("source-statistics")]
    public async Task<SourceStatistics> Statistics(CancellationToken ct) => await service.StatisticsAsync(ct);

    [HttpGet("import-settings")]
    public ImportSettings Settings() => new(options.Value.MaxFileBytes);

    [HttpGet("investigations/{id:guid}/sources")]
    public async Task<ActionResult<List<SourceSummary>>> List(Guid id, [FromQuery, StringLength(200)] string? search,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (from.HasValue && to.HasValue && from > to) return Problem(statusCode: 400, title: "From date must not follow to date.");
        return Ok(await service.ListAsync(id, search, from, to, ct));
    }

    [HttpPost("investigations/{id:guid}/sources")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<SourceResponse>> Upload(Guid id, [FromForm] SourceUploadRequest request, CancellationToken ct)
    {
        if (request.File.Length > options.Value.MaxFileBytes) throw new SourceException(413, "Document exceeds the configured file size limit.");
        await using var stream = request.File.OpenReadStream();
        var result = await service.ImportAsync(id, request, request.File.FileName, stream, false, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Source.Id }, result);
    }

    [HttpPost("investigations/{id:guid}/sources/text")]
    public async Task<ActionResult<SourceResponse>> Paste(Guid id, PastedSourceRequest request, CancellationToken ct)
    {
        await using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(request.Text));
        var result = await service.ImportAsync(id, request, "pasted-text.txt", stream, true, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Source.Id }, result);
    }

    [HttpGet("sources/{id:guid}")]
    public async Task<ActionResult<SourceResponse>> Get(Guid id, CancellationToken ct) => Ok(await service.GetAsync(id, ct));

    [HttpPut("sources/{id:guid}")]
    public async Task<ActionResult<SourceResponse>> Update(Guid id, SourceMetadataRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));

    // Text extraction only. NLP and processing jobs are not part of Phase 2.
    [HttpPost("sources/{id:guid}/extract-text")]
    public async Task<ActionResult<SourceResponse>> Extract(Guid id, CancellationToken ct) => Ok(await service.ReextractAsync(id, ct));

    [HttpGet("sources/{id:guid}/original")]
    public async Task<IActionResult> Original(Guid id, CancellationToken ct)
    {
        var original = await service.OpenOriginalAsync(id, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(original.Content, "application/octet-stream", original.Name);
    }
}
