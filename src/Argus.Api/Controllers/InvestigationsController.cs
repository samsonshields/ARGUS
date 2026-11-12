using Argus.Api.Services;
using Argus.Core.DTOs;
using Argus.Core.Enums;
using Microsoft.AspNetCore.Mvc;
namespace Argus.Api.Controllers;

[ApiController]
[Route("api/v1/investigations")]
public sealed class InvestigationsController(InvestigationService service, ILogger<InvestigationsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InvestigationResponse>>> List(
        [FromQuery] string? search, [FromQuery] InvestigationStatus? status, CancellationToken ct)
    {
        if (status.HasValue && !Enum.IsDefined(status.Value)) ModelState.AddModelError(nameof(status), "Unknown status.");
        if (search?.Length > 200) ModelState.AddModelError(nameof(search), "Search must be at most 200 characters.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Ok(await service.ListAsync(search, status, ct));
    }
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvestigationResponse>> Get(Guid id, CancellationToken ct)
        => await service.GetAsync(id, ct) is { } result ? Ok(result) : Problem(statusCode: 404, title: "Investigation not found.");
    [HttpPost]
    public async Task<ActionResult<InvestigationResponse>> Create(InvestigationRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        logger.LogInformation("Created investigation {InvestigationId}", result.Id);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InvestigationResponse>> Update(Guid id, InvestigationRequest request, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        if (result is null) return Problem(statusCode: 404, title: "Investigation not found.");
        logger.LogInformation("Updated investigation {InvestigationId}", id);
        return Ok(result);
    }
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool confirm, CancellationToken ct)
    {
        if (!confirm) return Problem(statusCode: 400, title: "Deletion requires confirm=true.");
        if (!await service.DeleteAsync(id, ct)) return Problem(statusCode: 404, title: "Investigation not found.");
        logger.LogInformation("Deleted investigation {InvestigationId}", id);
        return NoContent();
    }
}
