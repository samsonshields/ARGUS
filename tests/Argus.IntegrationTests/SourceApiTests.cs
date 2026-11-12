using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Argus.Core.DTOs;
using Argus.Core.Enums;
using Argus.Core.Interfaces;
using Argus.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Argus.IntegrationTests;

public sealed class SourceApiTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "argus-source-tests-" + Guid.NewGuid());
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static async Task<Guid> CreateInvestigation(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/investigations", new { title = "Fictional Meridian" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InvestigationResponse>(Json))!.Id;
    }
    private static async Task<HttpResponseMessage> Upload(HttpClient client, Guid investigationId, byte[] bytes,
        string name = "report.txt", string title = "Fictional report", string? date = "2026-01-15")
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(title), "Title");
        if (date is not null) form.Add(new StringContent(date), "PublishedAt");
        form.Add(new ByteArrayContent(bytes), "File", name);
        return await client.PostAsync($"/api/v1/investigations/{investigationId}/sources", form);
    }
    private static async Task<SourceResponse> ReadSource(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SourceResponse>(Json))!;
    }
    private static byte[] Pdf(bool text = true, int pages = 1)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 0; i < pages; i++)
        {
            var page = builder.AddPage(PageSize.A4);
            if (text) page.AddText("Fictional Meridian maritime training report", 12, new PdfPoint(25, 700), font);
        }
        return builder.Build();
    }

    [Fact]
    public async Task UploadSearchEditDownloadAndPersistencePreserveOriginal()
    {
        Guid investigationId, sourceId;
        var original = Encoding.UTF8.GetBytes("Meridian\r\nFictional unique maritime passage <script>alert(1)</script>");
        using (var factory = new TestApi(directory))
        using (var client = factory.CreateClient())
        {
            investigationId = await CreateInvestigation(client);
            var response = await Upload(client, investigationId, original, "../../report.TXT");
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var result = await ReadSource(response);
            sourceId = result.Source.Id;
            Assert.Equal("report.TXT", result.Source.OriginalFileName);
            Assert.Equal(SourceProcessingStatus.Ready, result.Source.ProcessingStatus);
            Assert.Equal(SourceReliability.E, result.Source.Reliability);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(original)), result.ContentHash);
            Assert.Contains("Meridian\nFictional", result.ExtractedText);
            Assert.EndsWith(sourceId.ToString(), response.Headers.Location!.ToString());
            var listed = await client.GetFromJsonAsync<List<SourceSummary>>($"/api/v1/investigations/{investigationId}/sources?search=UNIQUE%20MARITIME&from=2026-01-01&to=2026-01-31", Json);
            Assert.Single(listed!);
            var excluded = await client.GetFromJsonAsync<List<SourceSummary>>($"/api/v1/investigations/{investigationId}/sources?from=2026-02-01", Json);
            Assert.Empty(excluded!);
            var changed = await client.PutAsJsonAsync($"/api/v1/sources/{sourceId}", new SourceMetadataRequest
            {
                Title = "Updated title",
                Publisher = "Fictional publisher",
                OriginalUrl = "https://example.invalid/report",
                PublishedAt = new(2026, 1, 16),
                Reliability = SourceReliability.C
            }, Json);
            var updated = await ReadSource(changed);
            Assert.Equal(SourceReliability.C, updated.Source.Reliability);
            Assert.Equal(result.ContentHash, updated.ContentHash);
            var retried = await ReadSource(await client.PostAsync($"/api/v1/sources/{sourceId}/extract-text", null));
            Assert.Equal(result.ExtractedText, retried.ExtractedText);
            Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/v1/investigations/{investigationId}?confirm=true")).StatusCode);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
            Assert.Equal(3, await db.AuditEvents.CountAsync());
            Assert.All(await db.AuditEvents.ToListAsync(), audit => Assert.Equal(sourceId, audit.RecordId));
        }
        using (var factory = new TestApi(directory))
        using (var client = factory.CreateClient())
        {
            var result = (await client.GetFromJsonAsync<SourceResponse>($"/api/v1/sources/{sourceId}", Json))!;
            Assert.Equal("Updated title", result.Source.Title);
            Assert.Equal(SourceReliability.C, result.Source.Reliability);
            Assert.Equal(DateTimeKind.Utc, result.Source.RetrievedAt.Kind);
            using var download = await client.GetAsync($"/api/v1/sources/{sourceId}/original");
            Assert.Equal(original, await download.Content.ReadAsByteArrayAsync());
            Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Single(Directory.GetFiles(Path.Combine(directory, "documents")));
        }
    }

    [Fact]
    public async Task DuplicatesArePerInvestigationAndDoNotLeaveExtraFiles()
    {
        using var factory = new TestApi(directory);
        using var client = factory.CreateClient();
        var first = await CreateInvestigation(client);
        var bytes = Encoding.UTF8.GetBytes("Exact original bytes");
        var source = await ReadSource(await Upload(client, first, bytes));
        var duplicate = await Upload(client, first, bytes, "renamed.txt", "Different title");
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var problem = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(source.Source.Id, problem.GetProperty("existingSourceId").GetGuid());
        Assert.Single(Directory.GetFiles(Path.Combine(directory, "documents")));
        var second = await CreateInvestigation(client);
        Assert.Equal(HttpStatusCode.Created, (await Upload(client, second, bytes)).StatusCode);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(directory, "documents")).Length);
    }

    [Fact]
    public async Task ConcurrentDuplicatesHaveOneDatabaseRowAndOriginal()
    {
        using var factory = new TestApi(directory);
        using var client = factory.CreateClient();
        var id = await CreateInvestigation(client);
        var bytes = Encoding.UTF8.GetBytes("Concurrent duplicate");
        var responses = await Task.WhenAll(Upload(client, id, bytes), Upload(client, id, bytes));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Single(Directory.GetFiles(Path.Combine(directory, "documents")));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
        Assert.Equal(1, await db.Sources.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task PdfTextExtractionUsesRealParserAndPreservesPdf()
    {
        using var factory = new TestApi(directory);
        using var client = factory.CreateClient();
        var id = await CreateInvestigation(client);
        var bytes = Pdf();
        var result = await ReadSource(await Upload(client, id, bytes, "report.pdf"));
        Assert.Equal(SourceType.Pdf, result.Source.SourceType);
        Assert.Equal(SourceProcessingStatus.Ready, result.Source.ProcessingStatus);
        Assert.Contains("Fictional Meridian maritime", result.ExtractedText);
        var original = await client.GetByteArrayAsync($"/api/v1/sources/{result.Source.Id}/original");
        Assert.Equal(bytes, original);
    }

    [Theory]
    [InlineData("malformed-pdf")]
    [InlineData("empty-pdf")]
    [InlineData("binary-text")]
    [InlineData("invalid-utf8")]
    [InlineData("whitespace-text")]
    public async Task InvalidExtractionRetainsOriginalAndVisibleSafeError(string kind)
    {
        using var factory = new TestApi(directory);
        using var client = factory.CreateClient();
        var id = await CreateInvestigation(client);
        var bytes = kind switch
        {
            "empty-pdf" => Pdf(false),
            "binary-text" => new byte[] { 0, 1, 2 },
            "invalid-utf8" => new byte[] { 0xC3, 0x28 },
            "whitespace-text" => Encoding.UTF8.GetBytes(" \r\n"),
            _ => Encoding.UTF8.GetBytes("%PDF-1.7 PRIVATE_PARSER_PAYLOAD")
        };
        var result = await ReadSource(await Upload(client, id, bytes, kind.Contains("pdf") ? "bad.pdf" : "bad.txt"));
        Assert.Equal(SourceProcessingStatus.Failed, result.Source.ProcessingStatus);
        Assert.NotNull(result.Source.ProcessingError);
        Assert.DoesNotContain("PRIVATE_PARSER_PAYLOAD", result.Source.ProcessingError);
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/v1/sources/{result.Source.Id}/original"));
        Assert.Equal(SourceProcessingStatus.Failed, (await ReadSource(await client.PostAsync($"/api/v1/sources/{result.Source.Id}/extract-text", null))).Source.ProcessingStatus);
    }

    [Fact]
    public async Task FileTextAndPageLimitsAreEnforced()
    {
        using var factory = new TestApi(directory, new() { ["Documents:MaxFileBytes"] = "2048", ["Documents:MaxExtractedCharacters"] = "100", ["Documents:MaxPdfPages"] = "1" });
        using var client = factory.CreateClient();
        var id = await CreateInvestigation(client);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Upload(client, id, new byte[2049])).StatusCode);
        var text = await ReadSource(await Upload(client, id, Encoding.UTF8.GetBytes(new string('x', 101))));
        Assert.Equal(SourceProcessingStatus.Failed, text.Source.ProcessingStatus);
        var pdf = Pdf(pages: 2);
        Assert.True(pdf.Length < 2048);
        Assert.Equal(SourceProcessingStatus.Failed, (await ReadSource(await Upload(client, id, pdf, "pages.pdf"))).Source.ProcessingStatus);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(directory, "documents")).Length);
    }

    [Fact]
    public async Task UnsupportedEmptyInvalidMetadataAndUnknownParentsFailWithoutWrites()
    {
        using var factory = new TestApi(directory);
        using var client = factory.CreateClient();
        var id = await CreateInvestigation(client);
        var bytes = Encoding.UTF8.GetBytes("Report");
        using (var isolation = factory.Services.CreateScope())
            Assert.Equal(Path.Combine(directory, "argus.db"), isolation.ServiceProvider.GetRequiredService<ArgusDbContext>().Database.GetDbConnection().DataSource);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Upload(client, id, bytes, "evil.txt.exe")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, id, [])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, id, bytes, title: " ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, id, bytes, title: new('x', 201))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, id, bytes, date: "not-a-date")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(client, Guid.NewGuid(), bytes)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/sources/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/investigations/{id}/sources?from=2026-02-01&to=2026-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/investigations/{id}/sources?search={new string('x', 201)}")).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ArgusDbContext>().Sources.ToListAsync());
        Assert.False(Directory.Exists(Path.Combine(directory, "documents")));
    }

    [Fact]
    public async Task PastedTextIsStoredAsUtf8AndAttributionIsNotFetched()
    {
        using var factory = new TestApi(directory);
        using var client = factory.CreateClient();
        var id = await CreateInvestigation(client);
        var response = await client.PostAsJsonAsync($"/api/v1/investigations/{id}/sources/text", new PastedSourceRequest
        {
            Title = "Pasted source",
            Text = "Fictional \u03A9 report",
            OriginalUrl = "https://example.invalid/never-fetch",
            Reliability = SourceReliability.B
        }, Json);
        var source = await ReadSource(response);
        Assert.Equal(SourceType.PastedText, source.Source.SourceType);
        Assert.Equal(Encoding.UTF8.GetBytes("Fictional \u03A9 report"), await client.GetByteArrayAsync($"/api/v1/sources/{source.Source.Id}/original"));
        var stats = await client.GetFromJsonAsync<SourceStatistics>("/api/v1/source-statistics", Json);
        Assert.Equal(new SourceStatistics(1, 1, 0), stats);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/sources/{source.Source.Id}", new { title = "Test", originalUrl = "javascript:alert(1)" })).StatusCode);
    }

    [Fact]
    public async Task LongUnicodePastedTextAndConfiguredByteLimitAreHandled()
    {
        using var factory = new TestApi(directory, new() { ["Documents:MaxFileBytes"] = "100000" });
        using var client = factory.CreateClient();
        var id = await CreateInvestigation(client);
        var text = new string('\u03A9', 40000);
        var request = new PastedSourceRequest { Title = "Long Unicode paste", Text = text };
        var result = await ReadSource(await client.PostAsJsonAsync($"/api/v1/investigations/{id}/sources/text", request, Json));
        Assert.Equal(text, result.ExtractedText);
        Assert.Equal(80000, result.Source.FileSize);
        request.Text = new string('\u03A9', 50001);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,
            (await client.PostAsJsonAsync($"/api/v1/investigations/{id}/sources/text", request, Json)).StatusCode);
    }

    [Fact]
    public async Task RetryTimeoutAndMissingOriginalDoNotDestroyPriorText()
    {
        Guid sourceId;
        using (var factory = new TestApi(directory))
        using (var client = factory.CreateClient())
        {
            var id = await CreateInvestigation(client);
            sourceId = (await ReadSource(await Upload(client, id, Encoding.UTF8.GetBytes("Preserved evidence")))).Source.Id;
        }
        using (var factory = new TestApi(directory, configure: services =>
        {
            services.RemoveAll<IDocumentExtractor>(); services.AddSingleton<IDocumentExtractor, TimeoutExtractor>();
        }))
        using (var client = factory.CreateClient())
        {
            var result = await ReadSource(await client.PostAsync($"/api/v1/sources/{sourceId}/extract-text", null));
            Assert.Equal(SourceProcessingStatus.Failed, result.Source.ProcessingStatus);
            Assert.Contains("timed out", result.Source.ProcessingError);
            Assert.Equal("Preserved evidence", result.ExtractedText);
            Assert.NotNull(result.ExtractedAt);
        }
        // Simulate external removal of the temporary test original.
        File.Delete(Directory.GetFiles(Path.Combine(directory, "documents")).Single());
        using (var factory = new TestApi(directory))
        using (var client = factory.CreateClient())
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/sources/{sourceId}/original")).StatusCode);
            var result = await ReadSource(await client.PostAsync($"/api/v1/sources/{sourceId}/extract-text", null));
            Assert.Equal("Preserved evidence", result.ExtractedText);
            Assert.Equal(SourceProcessingStatus.Failed, result.Source.ProcessingStatus);
        }
    }

    [Fact]
    public async Task ForeignKeysAndUniqueHashAreDatabaseConstraints()
    {
        using var factory = new TestApi(directory);
        using var client = factory.CreateClient();
        var id = await CreateInvestigation(client);
        await ReadSource(await Upload(client, id, Encoding.UTF8.GetBytes("FK preserved source")));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Investigations WHERE Id = {id}"));
        var entity = await db.Sources.SingleAsync();
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Sources SET InvestigationId = {Guid.NewGuid()} WHERE Id = {entity.Id}"));
    }

    [Fact]
    public async Task PhaseOneDatabaseUpgradesWithoutLosingInvestigation()
    {
        Directory.CreateDirectory(directory);
        var options = new DbContextOptionsBuilder<ArgusDbContext>().UseSqlite($"Data Source={Path.Combine(directory, "argus.db")}").Options;
        var investigation = new Argus.Core.Entities.Investigation();
        investigation.Update(new() { Title = "Existing Phase 1 workspace" });
        await using (var db = new ArgusDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync("20261111214031_InitialInvestigations");
            db.Investigations.Add(investigation);
            await db.SaveChangesAsync();
        }
        using var factory = new TestApi(directory);
        using var client = factory.CreateClient();
        var record = await client.GetFromJsonAsync<InvestigationResponse>($"/api/v1/investigations/{investigation.Id}", Json);
        Assert.Equal("Existing Phase 1 workspace", record!.Title);
        Assert.Equal(HttpStatusCode.Created, (await Upload(client, investigation.Id, Encoding.UTF8.GetBytes("New Phase 2 source"))).StatusCode);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
    private sealed class TimeoutExtractor : IDocumentExtractor
    {
        public Task<string> ExtractAsync(string path, bool pdf, CancellationToken ct) => throw new OperationCanceledException();
    }
}
