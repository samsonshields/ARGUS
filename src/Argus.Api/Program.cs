using System.Text.Json.Serialization;
using Argus.Api.Services;
using Argus.Core.Interfaces;
using Argus.Infrastructure.Data;
using Argus.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Argus.Api.Middleware;
using Argus.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.Features;
using Argus.Infrastructure.ExternalServices;
using Argus.Api.BackgroundJobs;
if (args.Length == 5 && args[0] == "--extract-pdf")
{
    Environment.ExitCode = TextExtraction.RunPdfWorker(args);
    return;
}
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context => new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
        new Microsoft.AspNetCore.Mvc.ValidationProblemDetails(context.ModelState) { Status = 400, Extensions = { ["code"] = "validation_failed" } });
});
builder.Services.AddExceptionHandler<SourceExceptionHandler>();
builder.Services.AddOptions<IntelligenceOptions>().Bind(builder.Configuration.GetSection("Intelligence"))
    .Validate(x => Uri.TryCreate(x.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
        && x.TimeoutSeconds is >= 1 and <= 120 && x.MaxAttempts is >= 1 and <= 5 && x.RetryDelaySeconds is >= 0 and <= 60
        && x.MaxTextCharacters is > 0 and <= 2_000_000 && x.MaxCandidates is > 0 and <= 5000
        && x.MatchThreshold is >= 0.5 and <= 1, "Invalid intelligence configuration.").ValidateOnStart();
builder.Services.AddHttpClient<IIntelligenceClient, IntelligenceClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<IntelligenceOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
builder.Services.AddScoped<ProcessingJobService>();
builder.Services.AddScoped<ProcessingRunner>();
builder.Services.AddScoped<EntityReviewService>();
builder.Services.AddScoped<RelationshipService>();
builder.Services.AddScoped<GraphService>();
builder.Services.AddScoped<TemporalService>();
builder.Services.AddScoped<ClaimService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddHttpClient<IEventClient, IntelligenceClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<IntelligenceOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
builder.Services.AddHttpClient<IRelationshipClient, IntelligenceClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<IntelligenceOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
builder.Services.AddHostedService<IntelligenceWorker>();
builder.Services.AddOptions<DocumentOptions>().Bind(builder.Configuration.GetSection("Documents"))
    .Validate(x => x.MaxFileBytes is > 0 and <= 100 * 1024 * 1024 && x.MaxExtractedCharacters is > 0 and <= 2_000_000
        && x.MaxPdfPages is > 0 and <= 5000 && x.ExtractionTimeoutSeconds is > 0 and <= 120, "Invalid document limits.").ValidateOnStart();
var requestLimit = builder.Configuration.GetValue<long?>("Documents:MaxFileBytes") ?? 10 * 1024 * 1024;
var textLimit = builder.Configuration.GetValue<int?>("Documents:MaxExtractedCharacters") ?? 2_000_000;
// JSON may encode each UTF-16 character as six bytes; still enforce original-byte limits in the service.
builder.WebHost.ConfigureKestrel(x => x.Limits.MaxRequestBodySize = Math.Max(requestLimit + 131072, (long)textLimit * 6 + 65536));
builder.Services.Configure<FormOptions>(x => { x.MultipartBodyLengthLimit = requestLimit + 65536; x.ValueLengthLimit = 16384; });
builder.Services.AddSingleton<ApplicationDataRoot>(services =>
{
    var config = services.GetRequiredService<IConfiguration>();
    var environment = services.GetRequiredService<IHostEnvironment>();
    var directory = Path.GetFullPath(config["ARGUS_DATA_DIRECTORY"] ?? Path.Combine(environment.ContentRootPath, "data"));
    Directory.CreateDirectory(directory);
    return new(directory);
});
builder.Services.AddSingleton(services => new DocumentStorageRoot(Path.Combine(services.GetRequiredService<ApplicationDataRoot>().Path, "documents")));
builder.Services.AddSingleton<DocumentStorage>();
builder.Services.AddSingleton<IDocumentExtractor, DocumentExtractor>();
builder.Services.AddScoped<SourceService>();
builder.Services.AddDbContext<ArgusDbContext>((services, options) => options.UseSqlite(new SqliteConnectionStringBuilder
{
    DataSource = Path.Combine(services.GetRequiredService<ApplicationDataRoot>().Path, "argus.db"),
    ForeignKeys = true
}.ToString()));
builder.Services.AddScoped<IInvestigationRepository, InvestigationRepository>();
builder.Services.AddScoped<InvestigationService>();
var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<ArgusDbContext>().Database.MigrateAsync();
app.MapControllers();
app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.Run();
public partial class Program;
