using Argus.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace Argus.Api.Middleware;

public sealed class SourceExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not SourceException error) return false;
        await Results.Problem(statusCode: error.StatusCode, title: error.Message,
            extensions: error.ExistingSourceId.HasValue ? new Dictionary<string, object?> { ["existingSourceId"] = error.ExistingSourceId } : null)
            .ExecuteAsync(context);
        return true;
    }
}
