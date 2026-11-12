using Argus.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Argus.Api.Controllers;

[ApiController, Route("api/v1/backups")]
public sealed class BackupsController(BackupService backups) : ControllerBase
{
    [HttpPost("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var archive = await backups.ExportAsync(ct); Response.Headers.CacheControl = "no-store"; Response.Headers.XContentTypeOptions = "nosniff";
        return File(archive.Stream, "application/zip", archive.FileName);
    }
}
