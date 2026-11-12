using System.Diagnostics;

namespace Argus.Api.Middleware;

public sealed class RequestTelemetry(RequestDelegate next, ILogger<RequestTelemetry> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        context.Response.Headers["X-Argus-TraceId"] = traceId;
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["TraceId"] = traceId });
        try { await next(context); }
        finally
        {
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            logger.LogInformation("HTTP {Method} {Route} completed {StatusCode} in {ElapsedMilliseconds} ms", context.Request.Method, route,
                context.Response.StatusCode, Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1));
        }
    }
}
