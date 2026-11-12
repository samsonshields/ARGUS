using Argus.Web.Components;
using Argus.Web.Services;
using Microsoft.Extensions.Options;
var builder = WebApplication.CreateBuilder(args);
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
app.Run();
