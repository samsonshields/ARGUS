using Argus.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace Argus.Api.Middleware;

public sealed class SourceExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            await Results.Problem(statusCode: 409, title: "The record changed concurrently. Reload and try again.",
                extensions: new Dictionary<string, object?> { ["code"] = "concurrent_update" }).ExecuteAsync(context);
            return true;
        }
        if (exception is not SourceException error) return false;
        await Results.Problem(statusCode: error.StatusCode, title: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = $"request_{error.StatusCode}", ["existingSourceId"] = error.ExistingSourceId })
            .ExecuteAsync(context);
        return true;
    }
}
