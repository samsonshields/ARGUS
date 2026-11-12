using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Infrastructure.Data;
using Argus.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.IntegrationTests;

public sealed class AssessmentApiTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "argus-assessment-tests-" + Guid.NewGuid());
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private TestApi Factory() => new(directory);
    private static async Task<T> Read<T>(HttpResponseMessage response)
    { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }
    private static async Task<(Guid Inv, List<PassageResponse> Passages)> Seed(HttpClient client)
    {
        var inv = await Read<InvestigationResponse>(await client.PostAsJsonAsync("/api/v1/investigations", new { title = "Fictional source-backed assessment", description = "Compare the date of a fabricated meeting." }));
        var passages = new List<PassageResponse>();
        foreach (var (title, text, reliability) in new[] { ("Supporting release", "😀 Fictional meeting occurred on 2026-02-10. <script>alert(1)</script> [bad](javascript:alert(1))", SourceReliability.A),
                     ("Contradictory report", "A fictional report instead gives the date as 2026-02-11.", SourceReliability.D), ("Context note", "The fictional training organization uses local dates.", SourceReliability.C) })
        {
            var source = await Read<SourceResponse>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv.Id}/sources/text", new PastedSourceRequest { Title = title, Text = text, Reliability = reliability }, Json));
            var page = (await client.GetFromJsonAsync<PageResult<PassageResponse>>($"/api/v1/sources/{source.Source.Id}/passages", Json))!; passages.Add(Assert.Single(page.Items));
        }
        return (inv.Id, passages);
    }
    private static Task<ClaimDetail> Get(HttpClient client, Guid id) => client.GetFromJsonAsync<ClaimDetail>($"/api/v1/claims/{id}", Json)!;
    private static async Task<ClaimDetail> Create(HttpClient client, Guid inv, string statement = "The fictional meeting date remains disputed.")
        => await Read<ClaimDetail>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/claims", new ClaimEditRequest { Statement = statement, Reasoning = "Compare independent statements; their dates conflict. This is analyst interpretation.", ConfidenceLevel = ConfidenceLevel.MODERATE }, Json));
    private static async Task<ClaimDetail> Attach(HttpClient client, ClaimDetail item, PassageResponse passage, EvidenceRelationship kind)
    {
        (await client.PostAsJsonAsync($"/api/v1/claims/{item.Claim.Id}/evidence", new ClaimEvidenceRequest
        {
            SnapshotId = passage.SnapshotId,
            Start = passage.Start,
            End = passage.End,
            Relationship = kind,
            Notes = "Analyst classified this exact passage.",
            Revision = item.Claim.Revision
        }, Json)).EnsureSuccessStatusCode();
        return await Get(client, item.Claim.Id);
    }
    private static async Task<ClaimDetail> Approved(HttpClient client, Guid inv, List<PassageResponse> passages)
    {
        var item = await Create(client, inv); item = await Attach(client, item, passages[0], EvidenceRelationship.SUPPORTS);
        item = await Attach(client, item, passages[1], EvidenceRelationship.CONTRADICTS); item = await Attach(client, item, passages[2], EvidenceRelationship.CONTEXT);
        (await client.PostAsJsonAsync($"/api/v1/claims/{item.Claim.Id}/approve", new ClaimReviewRequest(true, true, item.Claim.Revision), Json)).EnsureSuccessStatusCode(); return await Get(client, item.Claim.Id);
    }
    [Fact]
    public async Task ReviewRetainsSourceRelationshipsConfidenceContradictionsAndRestartPersistence()
    {
        Guid claimId, inv;
        using (var api = Factory()) using (var client = api.CreateClient())
        {
            (inv, var passages) = await Seed(client); var item = await Create(client, inv); claimId = item.Claim.Id;
            Assert.Equal(ReviewStatus.PROPOSED, item.Claim.Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/claims/{claimId}/approve", new ClaimReviewRequest(true, false, item.Claim.Revision), Json)).StatusCode);
            item = await Attach(client, item, passages[0], EvidenceRelationship.SUPPORTS); item = await Attach(client, item, passages[1], EvidenceRelationship.CONTRADICTS);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/claims/{claimId}/approve", new ClaimReviewRequest(true, false, item.Claim.Revision), Json)).StatusCode);
            (await client.PostAsJsonAsync($"/api/v1/claims/{claimId}/approve", new ClaimReviewRequest(true, true, item.Claim.Revision), Json)).EnsureSuccessStatusCode();
            item = await Get(client, claimId); Assert.True(item.Claim.UnresolvedContradictions); Assert.Equal(ConfidenceLevel.MODERATE, item.Claim.ConfidenceLevel);
            Assert.Equal(SourceReliability.A, item.Evidence.Single(x => x.Relationship == EvidenceRelationship.SUPPORTS).Reliability);
            Assert.Equal(SourceReliability.D, item.Evidence.Single(x => x.Relationship == EvidenceRelationship.CONTRADICTS).Reliability);
            var summary = (await client.GetFromJsonAsync<AssessmentSummary>($"/api/v1/investigations/{inv}/assessments/summary", Json))!; Assert.Equal(1, summary.ApprovedWithUnresolvedContradictions);
        }
        using (var api = Factory()) using (var client = api.CreateClient())
        {
            var item = await Get(client, claimId); Assert.Equal(ReviewStatus.APPROVED, item.Claim.Status); Assert.StartsWith("😀", item.Evidence[0].Quote);
            Assert.Single((await client.GetFromJsonAsync<PageResult<ClaimSummary>>($"/api/v1/investigations/{inv}/claims?unresolvedOnly=true&confidence=MODERATE", Json))!.Items);
        }
    }
    [Fact]
    public async Task StaleUpdatesFailAndEvidenceRelabelingClearsResolutionWithoutLosingHistory()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, passages) = await Seed(client); var item = await Approved(client, inv, passages);
        var id = item.Claim.Id; var oldRevision = item.Claim.Revision;
        item = await Read<ClaimDetail>(await client.PutAsJsonAsync($"/api/v1/claims/{id}", new ClaimEditRequest
        {
            Statement = item.Claim.Statement,
            Reasoning = item.Reasoning,
            ConfidenceLevel = ConfidenceLevel.LOW,
            ContradictionsResolved = true,
            ContradictionResolution = "Source dates reflect separate local dates; retained as an analyst explanation.",
            Revision = oldRevision
        }, Json));
        Assert.False(item.Claim.UnresolvedContradictions); Assert.Equal(ReviewStatus.PROPOSED, item.Claim.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/claims/{id}/approve", new ClaimReviewRequest(true, true, oldRevision), Json)).StatusCode);
        item = await Attach(client, item, passages[1], EvidenceRelationship.CONTEXT);
        Assert.False(item.ContradictionsResolved); Assert.Empty(item.ContradictionResolution); Assert.Equal(3, item.Evidence.Count);
        item = await Attach(client, item, passages[1], EvidenceRelationship.CONTRADICTS); Assert.True(item.Claim.UnresolvedContradictions);
        var evidenceId = item.Evidence.Single(x => x.SourceId == passages[1].SourceId).Id;
        (await client.DeleteAsync($"/api/v1/claims/{id}/evidence/{evidenceId}?revision={item.Claim.Revision}")).EnsureSuccessStatusCode();
        using var scope = api.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
        Assert.False((await db.ClaimEvidence.FindAsync(evidenceId))!.Active); Assert.NotEmpty(await db.AuditEvents.Where(x => x.RecordId == id && x.BeforeJson != null).ToListAsync());
    }
    [Fact]
    public async Task CrossInvestigationAndMalformedPassagesAreRejectedWithoutChangingReview()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, passages) = await Seed(client); var (other, _) = await Seed(client); var item = await Create(client, other);
        var request = new ClaimEvidenceRequest { SnapshotId = passages[0].SnapshotId, Start = 0, End = passages[0].End, Revision = item.Claim.Revision };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/claims/{item.Claim.Id}/evidence", request, Json)).StatusCode);
        item = await Create(client, inv); request.Revision = item.Claim.Revision; request.Start = 1;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/claims/{item.Claim.Id}/evidence", request, Json)).StatusCode);
        Assert.Empty((await Get(client, item.Claim.Id)).Evidence);
        request.Start = 3; request.End = 12;
        (await client.PostAsJsonAsync($"/api/v1/claims/{item.Claim.Id}/evidence", request, Json)).EnsureSuccessStatusCode();
        Assert.Equal("Fictional", Assert.Single((await Get(client, item.Claim.Id)).Evidence).Quote);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/investigations/{inv}/claims?confidence=999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/investigations/{inv}/claims?page=0")).StatusCode);
    }
    [Fact]
    public async Task FinalReportIncludesApprovedClaimsReferencesContradictionsAndEmptyOptionalSectionsOnly()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, passages) = await Seed(client); var item = await Approved(client, inv, passages);
        await Create(client, inv, "UNAPPROVED_SECRET must not be exported");
        var rejected = await Create(client, inv, "REJECTED_SECRET must not be exported");
        (await client.PostAsJsonAsync($"/api/v1/claims/{rejected.Claim.Id}/reject", new ClaimReviewRequest(true, false, rejected.Claim.Revision), Json)).EnsureSuccessStatusCode();
        var validation = (await client.GetFromJsonAsync<ReportValidation>($"/api/v1/investigations/{inv}/reports/validation", Json))!;
        Assert.True(validation.IsValid, string.Join(";", validation.Errors)); Assert.Equal(1, validation.ClaimCount); Assert.Equal(3, validation.SourceCount); Assert.Equal(0, validation.EventCount);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/reports", new ReportRequest(false), Json)).StatusCode);
        var report = await Read<ReportResponse>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/reports", new ReportRequest(true), Json));
        Assert.DoesNotContain("UNAPPROVED_SECRET", report.Markdown); Assert.DoesNotContain("REJECTED_SECRET", report.Markdown);
        Assert.Contains("UNRESOLVED CONTRADICTION", report.Markdown); Assert.Contains("MODERATE", report.Markdown); Assert.Contains("SUPPORTS", report.Markdown); Assert.Contains("CONTRADICTS", report.Markdown);
        Assert.Contains("No approved events", report.Markdown); Assert.Contains("No approved geographic", report.Markdown); Assert.Contains("No approved entity", report.Markdown);
        Assert.Contains("&lt;script&gt;", report.Markdown); Assert.DoesNotContain("<script>", report.Markdown); Assert.Contains("\\[bad\\]", report.Markdown);
        foreach (var section in new[] { "Executive Summary", "Scope and Research Questions", "Key Findings", "Relevant Entities", "Chronological Events", "Geographic Context", "Source Evaluation", "Conflicting Information", "Analytical Limitations", "References" }) Assert.Contains("## " + section, report.Markdown);
        foreach (var passage in passages) { Assert.Contains(passage.TextHash, report.Markdown); Assert.Contains(passage.SourceId.ToString(), report.Markdown); }
        Assert.Equal(DateTimeKind.Utc, report.Report.GeneratedAt.Kind); Assert.Contains(report.Report.Id.ToString(), report.Markdown);
        var bytes = await client.GetByteArrayAsync($"/api/v1/reports/{report.Report.Id}/markdown"); Assert.Equal(report.Markdown, Encoding.UTF8.GetString(bytes));
        Assert.Equal(report.Report.ContentHash, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
    [Fact]
    public async Task GeneratedSnapshotsRemainImmutableAcrossCorrectionsRemovalAndRestart()
    {
        Guid reportId; string markdown;
        using (var api = Factory()) using (var client = api.CreateClient())
        {
            var (inv, passages) = await Seed(client); var claim = await Approved(client, inv, passages);
            var report = await Read<ReportResponse>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/reports", new ReportRequest(true), Json)); reportId = report.Report.Id; markdown = report.Markdown;
            (await client.PostAsJsonAsync($"/api/v1/claims/{claim.Claim.Id}/remove", new ReviewTemporalRequest(true, claim.Claim.Revision), Json)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/claims/{claim.Claim.Id}")).StatusCode);
            Assert.False((await client.GetFromJsonAsync<ReportValidation>($"/api/v1/investigations/{inv}/reports/validation", Json))!.IsValid);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/reports", new ReportRequest(true), Json)).StatusCode);
            Assert.Single((await client.GetFromJsonAsync<PageResult<ReportSummary>>($"/api/v1/investigations/{inv}/reports", Json))!.Items);
        }
        using (var api = Factory()) using (var client = api.CreateClient())
            Assert.Equal(markdown, (await client.GetFromJsonAsync<ReportResponse>($"/api/v1/reports/{reportId}", Json))!.Markdown);
    }
    [Theory]
    [InlineData("snapshot")]
    [InlineData("missing_original")]
    [InlineData("changed_original")]
    public async Task InvalidProvenanceBlocksFinalReportsWithoutCreatingArtifacts(string failure)
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, passages) = await Seed(client); await Approved(client, inv, passages);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
            if (failure == "snapshot") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SourceTextSnapshots SET TextHash = {new string('f', 64)} WHERE Id = {passages[0].SnapshotId}");
            else
            {
                var source = await db.Sources.FindAsync(passages[0].SourceId); var path = scope.ServiceProvider.GetRequiredService<DocumentStorage>().Resolve(source!.FilePath);
                if (failure == "missing_original") File.Delete(path); else { var bytes = await File.ReadAllBytesAsync(path); bytes[0] ^= 1; await File.WriteAllBytesAsync(path, bytes); }
            }
        }
        var validation = (await client.GetFromJsonAsync<ReportValidation>($"/api/v1/investigations/{inv}/reports/validation", Json))!; Assert.False(validation.IsValid); Assert.NotEmpty(validation.Errors);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/reports", new ReportRequest(true), Json)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<PageResult<ReportSummary>>($"/api/v1/investigations/{inv}/reports", Json))!.Items);
    }
    [Fact]
    public async Task OptionalApprovedEventsLocationsAndEntitiesRetainPrecisionAndProvenance()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, passages) = await Seed(client); await Approved(client, inv, passages);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>(); var source = (await db.Sources.FindAsync(passages[0].SourceId))!; var snapshot = (await db.SourceTextSnapshots.FindAsync(passages[0].SnapshotId))!;
            var entity = new IntelligenceEntity(inv, new() { Name = "Fictional port", Type = EntityType.LOCATION }); var job = new ProcessingJob(source); job.Start(); job.AttachSnapshot(snapshot.Id); job.Complete(1);
            var start = source.ExtractedText.IndexOf("Fictional", StringComparison.Ordinal); var mention = new EntityMention(source, snapshot, job, new("Fictional", EntityType.LOCATION, start, start + 9, null), new("test", "1", "3.8")); mention.Approve(entity);
            var location = new LocationProfile(inv, new() { EntityId = entity.Id, Precision = GeoPrecision.APPROXIMATE, Latitude = 35, Longitude = 30, UncertaintyMeters = 25000, ConfirmCoordinates = true, Notes = "Explicit fictional region center." }); location.Review(true, true);
            var item = new AnalysisEvent(inv, new EventEditRequest { Title = "Fictional month event", DatePrecision = DatePrecision.MONTH, DateValue = "2026-02", LocationId = location.Id }); item.Review(true, true);
            db.AddRange(entity, job, mention, location, item, new LocationEvidence(location.Id, snapshot, start, start + 9), new EventEvidence(item.Id, snapshot, start, start + 9)); await db.SaveChangesAsync();
        }
        var report = await Read<ReportResponse>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/reports", new ReportRequest(true), Json));
        Assert.Equal(1, report.Validation.EntityCount); Assert.Equal(1, report.Validation.EventCount); Assert.Equal(1, report.Validation.LocationCount);
        Assert.Contains("2026-02 (month only)", report.Markdown); Assert.Contains("25000 metres", report.Markdown); Assert.Contains("EXPLICIT_SELECTION", report.Markdown);
    }
    [Fact]
    public async Task OversizedApprovedCollectionsFailBeforeExportWithoutTruncation()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, _) = await Seed(client);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
            for (var i = 0; i < 501; i++)
            {
                var item = new Claim(inv, new() { Statement = $"Fictional bounded export claim {i}", Reasoning = "Test collection boundary" });
                item.Review(true, new(true, false, item.Revision), [EvidenceRelationship.SUPPORTS]); db.Claims.Add(item);
            }
            await db.SaveChangesAsync();
        }
        var validation = (await client.GetFromJsonAsync<ReportValidation>($"/api/v1/investigations/{inv}/reports/validation", Json))!;
        Assert.False(validation.IsValid); Assert.Equal(501, validation.ClaimCount); Assert.Contains(validation.Errors, x => x.Contains("no data was truncated", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/reports", new ReportRequest(true), Json)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<PageResult<ReportSummary>>($"/api/v1/investigations/{inv}/reports", Json))!.Items);
    }
    [Fact]
    public async Task ArtifactTamperingBlocksReadAndDownload()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, passages) = await Seed(client); await Approved(client, inv, passages);
        var report = await Read<ReportResponse>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/reports", new ReportRequest(true), Json));
        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ArgusDbContext>().Database.ExecuteSqlInterpolatedAsync($"UPDATE GeneratedReports SET Markdown = {"Changed contents"} WHERE Id = {report.Report.Id}");
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/v1/reports/{report.Report.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/v1/reports/{report.Report.Id}/markdown")).StatusCode);
    }
    [Fact]
    public async Task PhaseFiveUpgradePreservesTemporalDataAndOpenApiContainsOnlyAuthorizedAdditions()
    {
        Directory.CreateDirectory(directory); var options = new DbContextOptionsBuilder<ArgusDbContext>().UseSqlite($"Data Source={Path.Combine(directory, "argus.db")}").Options;
        var inv = new Investigation(); inv.Update(new() { Title = "Existing Phase 5 workspace" });
        await using (var db = new ArgusDbContext(options))
        { await db.GetService<IMigrator>().MigrateAsync("20261112024255_TimelineGeography"); db.AddRange(inv, new AnalysisEvent(inv.Id, new EventEditRequest { Title = "Preserved unknown event" })); await db.SaveChangesAsync(); }
        using var api = Factory(); using var client = api.CreateClient();
        Assert.Single((await client.GetFromJsonAsync<PageResult<EventResponse>>($"/api/v1/investigations/{inv.Id}/events?status=PROPOSED", Json))!.Items);
        Assert.Empty((await client.GetFromJsonAsync<PageResult<ClaimSummary>>($"/api/v1/investigations/{inv.Id}/claims", Json))!.Items);
        var openApi = await client.GetStringAsync("/openapi/v1.json"); Assert.Contains("/api/v1/reports/{id}/markdown", openApi); Assert.Contains("/api/v1/claims/{id}/approve", openApi); Assert.DoesNotContain("/collaboration", openApi);
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
