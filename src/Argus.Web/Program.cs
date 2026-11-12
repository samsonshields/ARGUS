using Argus.Web.Components;
using Argus.Web.Services;
using Microsoft.Extensions.Options;
var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole(options => { options.UseUtcTimestamp = true; options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ"; options.IncludeScopes = true; });
builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 16 * 1024 * 1024);
builder.Services.AddOptions<ApiOptions>().Bind(builder.Configuration.GetSection("Api"))
    .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https", "Api:BaseUrl must be an HTTP URL.").ValidateOnStart();
builder.Services.AddHttpClient<InvestigationClient>((services, client) =>
{
    client.BaseAddress = new Uri(services.GetRequiredService<IOptions<ApiOptions>>().Value.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient<SourceClient>((services, client) =>
{
    client.BaseAddress = new Uri(services.GetRequiredService<IOptions<ApiOptions>>().Value.BaseUrl);
    client.Timeout = TimeSpan.FromMinutes(3);
});
builder.Services.AddHttpClient<IntelligenceApiClient>((services, client) =>
{
    client.BaseAddress = new Uri(services.GetRequiredService<IOptions<ApiOptions>>().Value.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(40);
});
builder.Services.AddHttpClient<GraphApiClient>((services, client) =>
{
    client.BaseAddress = new Uri(services.GetRequiredService<IOptions<ApiOptions>>().Value.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(40);
});
builder.Services.AddHttpClient<TemporalApiClient>((services, client) =>
{
    client.BaseAddress = new Uri(services.GetRequiredService<IOptions<ApiOptions>>().Value.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(40);
});
builder.Services.AddHttpClient<AssessmentApiClient>((services, client) =>
{
    client.BaseAddress = new Uri(services.GetRequiredService<IOptions<ApiOptions>>().Value.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddHttpClient<BackupApiClient>((services, client) =>
{
    client.BaseAddress = new Uri(services.GetRequiredService<IOptions<ApiOptions>>().Value.BaseUrl);
    client.Timeout = TimeSpan.FromMinutes(3);
});
var app = builder.Build();
app.UseExceptionHandler("/Error", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapGet("/downloads/sources/{id:guid}", async Task<IResult> (Guid id, SourceClient client, HttpContext context, CancellationToken ct) =>
{
    try
    {
        var file = await client.DownloadAsync(id, ct);
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(file.Content, "application/octet-stream", file.Name);
    }
    catch (HttpRequestException ex) { return Results.Problem(statusCode: (int?)ex.StatusCode ?? 502, title: "Unable to download original source."); }
    catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Results.Problem(statusCode: 504, title: "Source download timed out."); }
});
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapGet("/downloads/backups", async (BackupApiClient client, HttpContext context, CancellationToken ct) =>
{
    try
    {
        using var response = await client.ExportAsync(ct);
        context.Response.ContentType = "application/zip"; context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.ContentDisposition = response.Content.Headers.ContentDisposition?.ToString();
        await using var stream = await response.Content.ReadAsStreamAsync(ct); await stream.CopyToAsync(context.Response.Body, ct);
    }
    catch (HttpRequestException ex) when (!context.Response.HasStarted)
    { await Results.Problem(statusCode: (int?)ex.StatusCode ?? 502, title: "Backup export failed. Check the API and preserved originals.").ExecuteAsync(context); }
    catch (TaskCanceledException) when (!context.Response.HasStarted && !ct.IsCancellationRequested)
    { await Results.Problem(statusCode: 504, title: "Backup export timed out.").ExecuteAsync(context); }
});
app.MapGet("/downloads/reports/{id:guid}", async Task<IResult> (Guid id, AssessmentApiClient client, HttpContext context, CancellationToken ct) =>
{
    try
    {
        var file = await client.DownloadAsync(id, ct); context.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(file.Content, "text/markdown; charset=utf-8", file.Name);
    }
    catch (HttpRequestException ex) { return Results.Problem(statusCode: (int?)ex.StatusCode ?? 502, title: "Unable to download report."); }
    catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Results.Problem(statusCode: 504, title: "Report download timed out."); }
});
app.Run();
