using System.Data.Common;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Infrastructure.Data;
using Argus.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Argus.IntegrationTests;

public sealed class WorkspaceQualityTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "argus-quality-" + Guid.NewGuid());
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static async Task<T> Read<T>(HttpResponseMessage response)
    { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }
    private static async Task<(Guid Inv, SourceResponse Source, PassageResponse Passage)> Seed(HttpClient client)
    {
        var inv = await Read<InvestigationResponse>(await client.PostAsJsonAsync("/api/v1/investigations", new { title = "Fictional quality fixture" }));
        var source = await Read<SourceResponse>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv.Id}/sources/text", new PastedSourceRequest { Title = "Fictional preserved original", Text = "😀 Fictional membership and activity record." }, Json));
        var passage = Assert.Single((await client.GetFromJsonAsync<PageResult<PassageResponse>>($"/api/v1/sources/{source.Source.Id}/passages", Json))!.Items);
        return (inv.Id, source, passage);
    }
    [Fact]
    public async Task LiveBackupRestoresOriginalsEvidenceReviewsReportsAndQueueIntoANewWorkspace()
    {
        using var api = new TestApi(Path.Combine(directory, "original")); using var client = api.CreateClient(); var (inv, source, passage) = await Seed(client);
        var claim = await Read<ClaimDetail>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/claims", new ClaimEditRequest { Statement = "Fictional membership record", Reasoning = "Analyst compared the original statement." }, Json));
        (await client.PostAsJsonAsync($"/api/v1/claims/{claim.Claim.Id}/evidence", new ClaimEvidenceRequest { SnapshotId = passage.SnapshotId, Start = 3, End = 12, Revision = claim.Claim.Revision }, Json)).EnsureSuccessStatusCode();
        claim = (await client.GetFromJsonAsync<ClaimDetail>($"/api/v1/claims/{claim.Claim.Id}", Json))!;
        (await client.PostAsJsonAsync($"/api/v1/claims/{claim.Claim.Id}/approve", new ClaimReviewRequest(true, false, claim.Claim.Revision), Json)).EnsureSuccessStatusCode();
        var report = await Read<ReportResponse>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/reports", new ReportRequest(true), Json));
        var job = await Read<JobResponse>(await client.PostAsync($"/api/v1/sources/{source.Source.Id}/process-events", null));
        var response = await client.PostAsync("/api/v1/backups/export", null); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType); Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var archivePath = Path.Combine(directory, "snapshot.zip"); await File.WriteAllBytesAsync(archivePath, await response.Content.ReadAsByteArrayAsync());
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            Assert.Equal(3, archive.Entries.Count); Assert.NotNull(archive.GetEntry("database/argus.db"));
            await using var manifestStream = archive.GetEntry("manifest.json")!.Open();
            var manifest = (await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, Json))!;
            Assert.Equal(1, manifest.FormatVersion); Assert.Equal(7, manifest.Migrations.Count); Assert.Equal(DateTimeKind.Utc, manifest.CreatedAt.Kind);
        }
        var restoredPath = Path.Combine(directory, "restored"); var restored = await BackupArchive.RestoreAsync(archivePath, restoredPath, CancellationToken.None);
        Assert.Equal(1, restored.DocumentCount);
        using var restoredApi = new TestApi(restoredPath); using var restoredClient = restoredApi.CreateClient();
        Assert.Equal(await client.GetByteArrayAsync($"/api/v1/sources/{source.Source.Id}/original"), await restoredClient.GetByteArrayAsync($"/api/v1/sources/{source.Source.Id}/original"));
        var saved = (await restoredClient.GetFromJsonAsync<ClaimDetail>($"/api/v1/claims/{claim.Claim.Id}", Json))!;
        Assert.Equal(ReviewStatus.APPROVED, saved.Claim.Status); Assert.Equal("Fictional", Assert.Single(saved.Evidence).Quote);
        Assert.Equal(report.Markdown, (await restoredClient.GetFromJsonAsync<ReportResponse>($"/api/v1/reports/{report.Report.Id}", Json))!.Markdown);
        Assert.Equal(JobState.PENDING, (await restoredClient.GetFromJsonAsync<JobResponse>($"/api/v1/processing-jobs/{job.Id}", Json))!.State);
        Assert.Empty(Directory.GetFiles(Path.Combine(directory, "original", "exports")));
        await Assert.ThrowsAsync<InvalidDataException>(() => BackupArchive.RestoreAsync(archivePath, restoredPath, CancellationToken.None));
        Assert.Equal(report.Markdown, (await restoredClient.GetFromJsonAsync<ReportResponse>($"/api/v1/reports/{report.Report.Id}", Json))!.Markdown);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("changed")]
    public async Task BackupFailsClosedWhenOriginalsAreMissingOrChanged(string kind)
    {
        using var api = new TestApi(directory); using var client = api.CreateClient(); var (_, source, _) = await Seed(client);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>(); var stored = (await db.Sources.FindAsync(source.Source.Id))!;
            var path = scope.ServiceProvider.GetRequiredService<DocumentStorage>().Resolve(stored.FilePath);
            if (kind == "missing") File.Delete(path); else { var bytes = await File.ReadAllBytesAsync(path); bytes[0] ^= 1; await File.WriteAllBytesAsync(path, bytes); }
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync("/api/v1/backups/export", null)).StatusCode);
        Assert.Empty(Directory.GetFiles(Path.Combine(directory, "exports")));
    }
    [Theory]
    [InlineData("traversal")]
    [InlineData("duplicate")]
    [InlineData("checksum")]
    [InlineData("null_manifest")]
    [InlineData("symlink")]
    public async Task InvalidArchivesNeverInstallOrEscapeTheirStagingDirectory(string kind)
    {
        using var api = new TestApi(Path.Combine(directory, "data")); using var client = api.CreateClient(); await Seed(client);
        var bytes = await (await client.PostAsync("/api/v1/backups/export", null)).Content.ReadAsByteArrayAsync();
        var path = Path.Combine(directory, "bad.zip"); await File.WriteAllBytesAsync(path, bytes);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            if (kind == "traversal") { var entry = archive.CreateEntry("../escaped.txt"); using var writer = new StreamWriter(entry.Open()); writer.Write("Do not extract"); }
            else if (kind == "duplicate") archive.CreateEntry("manifest.json");
            else if (kind == "symlink") archive.GetEntry("database/argus.db")!.ExternalAttributes = 0xA000 << 16;
            else
            {
                var entry = archive.GetEntry("manifest.json")!; string text; using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd(); entry.Delete();
                using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
                if (kind == "null_manifest") writer.Write("null");
                else
                {
                    var manifest = JsonSerializer.Deserialize<BackupManifest>(text, Json)!;
                    var files = manifest.Files.ToList(); files[0] = files[0] with { Sha256 = new string('f', 64) };
                    writer.Write(JsonSerializer.Serialize(manifest with { Files = files }, Json));
                }
            }
        }
        var destination = Path.Combine(directory, "not-installed");
        await Assert.ThrowsAsync<InvalidDataException>(() => BackupArchive.RestoreAsync(path, destination, CancellationToken.None));
        Assert.False(Directory.Exists(destination)); Assert.False(File.Exists(Path.Combine(directory, "escaped.txt")));
        Assert.Empty(Directory.GetDirectories(directory, ".argus-restore-*"));
    }
    [Fact]
    public async Task CommonListsUseAFixedQueryBudgetAcrossFiftyRecordsAndKeepExactCitations()
    {
        var counter = new QueryCounter();
        using var api = new TestApi(directory, configure: services => services.AddDbContext<ArgusDbContext>(options => options.AddInterceptors(counter)));
        using var client = api.CreateClient(); var (inv, source, passage) = await Seed(client);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>(); var snapshot = (await db.SourceTextSnapshots.FindAsync(passage.SnapshotId))!;
            for (var i = 0; i < 50; i++)
            {
                var claim = new Claim(inv, new() { Statement = $"Fictional claim {i}" });
                var entity = new IntelligenceEntity(inv, new() { Name = $"Fictional place {i}", Type = EntityType.LOCATION });
                var location = new LocationProfile(inv, new() { EntityId = entity.Id, Precision = GeoPrecision.UNKNOWN });
                var item = new AnalysisEvent(inv, new EventEditRequest { Title = $"Fictional event {i}" }); item.Review(true, true);
                db.AddRange(claim, new ClaimEvidence(claim.Id, snapshot, new() { Start = 3, End = 12 }), entity, location,
                    new LocationEvidence(location.Id, snapshot, 3, 12), item, new EventEvidence(item.Id, snapshot, 3, 12));
            }
            await db.SaveChangesAsync();
        }
        counter.Count = 0; var claims = (await client.GetFromJsonAsync<PageResult<ClaimSummary>>($"/api/v1/investigations/{inv}/claims?pageSize=100", Json))!;
        Assert.Equal(50, claims.Total); Assert.InRange(counter.Count, 1, 5); Assert.All(claims.Items, x => Assert.Equal(1, x.SupportingCount));
        counter.Count = 0; var events = (await client.GetFromJsonAsync<PageResult<EventResponse>>($"/api/v1/investigations/{inv}/events?pageSize=100", Json))!;
        Assert.Equal(50, events.Total); Assert.InRange(counter.Count, 1, 5); Assert.All(events.Items, x => Assert.Equal("Fictional", Assert.Single(x.Evidence).Quote));
        counter.Count = 0; var locations = (await client.GetFromJsonAsync<List<LocationResponse>>($"/api/v1/investigations/{inv}/locations", Json))!;
        Assert.Equal(50, locations.Count); Assert.InRange(counter.Count, 1, 4); Assert.All(locations, x => Assert.Equal("Fictional", Assert.Single(x.Evidence).Quote));
    }
    [Fact]
    public async Task RequestDiagnosticsIncludeTraceAndRouteButNeverTheSearchText()
    {
        var recorder = new RecordingLogs(); using var api = new TestApi(directory, configure: services => services.AddLogging(builder => builder.AddProvider(recorder)));
        using var client = api.CreateClient(); var (inv, _, _) = await Seed(client); recorder.Messages.Clear();
        var response = await client.GetAsync($"/api/v1/investigations/{inv}/claims?search=PRIVATE_RESEARCH_QUERY");
        response.EnsureSuccessStatusCode(); Assert.NotEmpty(response.Headers.GetValues("X-Argus-TraceId"));
        Assert.Contains(recorder.Messages, message => message.Contains("HTTP GET api/v1/investigations/{id:guid}/claims", StringComparison.Ordinal));
        Assert.DoesNotContain(recorder.Messages, message => message.Contains("PRIVATE_RESEARCH_QUERY", StringComparison.Ordinal));
    }
    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Count); return ValueTask.FromResult(result); }
    }
    private sealed class RecordingLogs : ILoggerProvider
    {
        public readonly System.Collections.Concurrent.ConcurrentBag<string> Messages = [];
        public ILogger CreateLogger(string categoryName) => new Recorder(Messages);
        public void Dispose() { }
        private sealed class Recorder(System.Collections.Concurrent.ConcurrentBag<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => messages.Add(formatter(state, exception));
        }
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
