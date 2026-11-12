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
if (args.Length == 5 && args[0] == "--extract-pdf")
{
    Environment.ExitCode = TextExtraction.RunPdfWorker(args);
    return;
}
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SourceExceptionHandler>();
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
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.Run();
public partial class Program;
