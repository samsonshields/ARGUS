using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Argus.Api.Services;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Exceptions;
using Argus.Core.Interfaces;
using Argus.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Argus.IntegrationTests;

public sealed class TemporalApiTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "argus-temporal-tests-" + Guid.NewGuid());
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly EventsPipeline pipeline = new();
    private TestApi Factory() => new(directory, new() { ["Intelligence:RetryDelaySeconds"] = "0" }, services =>
    { services.RemoveAll<IEventClient>(); services.AddSingleton<IEventClient>(pipeline); });
    private static async Task<T> Read<T>(HttpResponseMessage response)
    { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }
    private static async Task<(Guid Inv, Guid Source, Guid Entity, PassageResponse Passage)> Seed(TestApi api, HttpClient client)
    {
        var inv = await Read<InvestigationResponse>(await client.PostAsJsonAsync("/api/v1/investigations", new { title = "Fictional timeline workspace" }));
        var source = await Read<SourceResponse>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv.Id}/sources/text", new PastedSourceRequest { Title = "Fictional dated report", Text = "😀 On 2026-02-10, Aster met at Port Meridian. Last Tuesday another meeting was reported." }, Json));
        using var scope = api.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
        var entity = new IntelligenceEntity(inv.Id, new() { Name = "Port Meridian", Type = EntityType.LOCATION });
        db.Entities.Add(entity); await db.SaveChangesAsync();
        var passages = (await client.GetFromJsonAsync<PageResult<PassageResponse>>($"/api/v1/sources/{source.Source.Id}/passages", Json))!;
        return (inv.Id, source.Source.Id, entity.Id, Assert.Single(passages.Items));
    }
    private static async Task Run(TestApi api) { using var scope = api.Services.CreateScope(); Assert.True(await scope.ServiceProvider.GetRequiredService<ProcessingRunner>().RunNextAsync(CancellationToken.None)); }
    private static async Task<EventResponse> GetEvent(HttpClient client, Guid id) => (await client.GetFromJsonAsync<EventResponse>($"/api/v1/events/{id}", Json))!;
    private static async Task<LocationResponse> GetLocation(HttpClient client, Guid id) => (await client.GetFromJsonAsync<LocationResponse>($"/api/v1/locations/{id}", Json))!;
    private static async Task ApproveEvent(HttpClient client, EventResponse item) => (await client.PostAsJsonAsync($"/api/v1/events/{item.Id}/approve", new ReviewTemporalRequest(true, item.Revision), Json)).EnsureSuccessStatusCode();
    private static async Task<LocationResponse> CreateLocation(HttpClient client, Guid inv, Guid entity, PassageResponse passage)
    {
        var item = await Read<LocationResponse>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/locations", new LocationEditRequest
        { EntityId = entity, Precision = GeoPrecision.APPROXIMATE, Latitude = 35, Longitude = 30, UncertaintyMeters = 25000, ConfirmCoordinates = true, Notes = "Explicit fictional region center; approximate extent." }, Json));
        (await client.PostAsJsonAsync($"/api/v1/locations/{item.Id}/evidence", new CitationRequest(passage.SnapshotId, passage.Start, passage.End, item.Revision), Json)).EnsureSuccessStatusCode();
        item = await GetLocation(client, item.Id);
        (await client.PostAsJsonAsync($"/api/v1/locations/{item.Id}/approve", new ReviewTemporalRequest(true, item.Revision), Json)).EnsureSuccessStatusCode();
        return await GetLocation(client, item.Id);
    }
    [Fact]
    public async Task GenerationReviewCorrectionRemovalDeduplicationAndRestartPreserveEvidence()
    {
        Guid inv, source, eventId; string hash;
        using (var api = Factory()) using (var client = api.CreateClient())
        {
            (inv, source, _, var passage) = await Seed(api, client); hash = passage.TextHash;
            var job = await Read<JobResponse>(await client.PostAsync($"/api/v1/sources/{source}/process-events", null));
            Assert.Equal(JobKind.EVENTS, job.Kind); await Run(api);
            Assert.Equal(JobState.COMPLETED, (await client.GetFromJsonAsync<JobResponse>($"/api/v1/processing-jobs/{job.Id}", Json))!.State);
            var all = (await client.GetFromJsonAsync<PageResult<EventResponse>>($"/api/v1/investigations/{inv}/events?status=PROPOSED", Json))!;
            Assert.Equal(2, all.Total); var item = all.Items.First(); eventId = item.Id;
            Assert.StartsWith("😀", Assert.Single(item.Evidence).Quote); Assert.Equal(hash, item.Evidence[0].TextHash);
            Assert.Equal(DatePrecision.UNKNOWN, all.Items.Last().DatePrecision); Assert.Null(all.Items.Last().StartAt);
            await ApproveEvent(client, item); item = await GetEvent(client, eventId);
            var stale = await client.PutAsJsonAsync($"/api/v1/events/{eventId}", new EventEditRequest { Title = "Stale", Revision = item.Revision - 1 }, Json);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            item = await Read<EventResponse>(await client.PutAsJsonAsync($"/api/v1/events/{eventId}", new EventEditRequest { Title = "Corrected event", DatePrecision = DatePrecision.MONTH, DateValue = "2026-03", Revision = item.Revision }, Json));
            Assert.Equal(ReviewStatus.PROPOSED, item.Status); Assert.Equal("2026-02-10", item.OriginalDateText); await ApproveEvent(client, item);
            await client.PostAsync($"/api/v1/sources/{source}/process-events", null); await Run(api);
            all = (await client.GetFromJsonAsync<PageResult<EventResponse>>($"/api/v1/investigations/{inv}/events?allStatuses=true", Json))!;
            Assert.Equal(2, all.Total); Assert.Equal(ReviewStatus.APPROVED, (await GetEvent(client, eventId)).Status);
            var unresolved = all.Items.Single(x => x.Id != eventId);
            (await client.PostAsJsonAsync($"/api/v1/events/{unresolved.Id}/remove", new ReviewTemporalRequest(true, unresolved.Revision), Json)).EnsureSuccessStatusCode();
            await client.PostAsync($"/api/v1/sources/{source}/process-events", null); await Run(api);
            Assert.Equal(1, (await client.GetFromJsonAsync<PageResult<EventResponse>>($"/api/v1/investigations/{inv}/events?allStatuses=true", Json))!.Total);
        }
        using (var api = Factory()) using (var client = api.CreateClient())
        { var item = await GetEvent(client, eventId); Assert.Equal("Corrected event", item.Title); Assert.Equal(DatePrecision.MONTH, item.DatePrecision); Assert.Equal(hash, Assert.Single(item.Evidence).TextHash); Assert.NotEmpty(await client.GetByteArrayAsync($"/api/v1/sources/{source}/original")); }
    }
    [Fact]
    public async Task ManualEventRequiresCitationAndTimelineUsesPartialDateOverlapAndUnknownOrdering()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, _, entity, passage) = await Seed(api, client);
        foreach (var (precision, value) in new[] { (DatePrecision.UNKNOWN, (string?)null), (DatePrecision.MONTH, "2026-02"), (DatePrecision.YEAR, "2025"), (DatePrecision.EXACT, "2026-01-02") })
        {
            var item = await Read<EventResponse>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/events", new EventEditRequest { Title = precision.ToString(), DatePrecision = precision, DateValue = value, EntityIds = [entity] }, Json));
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/events/{item.Id}/approve", new ReviewTemporalRequest(true, item.Revision), Json)).StatusCode);
            (await client.PostAsJsonAsync($"/api/v1/events/{item.Id}/evidence", new CitationRequest(passage.SnapshotId, passage.Start, passage.End, item.Revision), Json)).EnsureSuccessStatusCode();
            item = await GetEvent(client, item.Id); await ApproveEvent(client, item);
        }
        var timeline = (await client.GetFromJsonAsync<PageResult<EventResponse>>($"/api/v1/investigations/{inv}/timeline", Json))!;
        Assert.Equal(new[] { DatePrecision.YEAR, DatePrecision.EXACT, DatePrecision.MONTH, DatePrecision.UNKNOWN }, timeline.Items.Select(x => x.DatePrecision));
        var filtered = (await client.GetFromJsonAsync<PageResult<EventResponse>>($"/api/v1/investigations/{inv}/events?from=2026-02-15&to=2026-02-16&includeUnknown=false", Json))!;
        var month = Assert.Single(filtered.Items); Assert.Equal(DatePrecision.MONTH, month.DatePrecision);
        var citation = Assert.Single(month.Evidence);
        (await client.DeleteAsync($"/api/v1/events/{month.Id}/evidence/{citation.Id}?revision={month.Revision}")).EnsureSuccessStatusCode();
        var changed = await GetEvent(client, month.Id); Assert.Empty(changed.Evidence); Assert.Equal(ReviewStatus.PROPOSED, changed.Status);
        using var scope = api.Services.CreateScope(); Assert.False((await scope.ServiceProvider.GetRequiredService<ArgusDbContext>().EventEvidence.FindAsync(citation.Id))!.Active);
    }
    [Fact]
    public async Task MapRequiresReviewedCoordinatesAndShowsAssociatedEventsWithCitationAndSavedFocus()
    {
        Guid inv, locationId, eventId;
        using (var api = Factory()) using (var client = api.CreateClient())
        {
            (inv, _, var entity, var passage) = await Seed(api, client);
            var location = await CreateLocation(client, inv, entity, passage); locationId = location.Id;
            var item = await Read<EventResponse>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/events", new EventEditRequest { Title = "Fictional port meeting", Type = EventType.MEETING, DatePrecision = DatePrecision.EXACT, DateValue = "2026-02-10", LocationId = locationId, EntityIds = [entity] }, Json)); eventId = item.Id;
            (await client.PostAsJsonAsync($"/api/v1/events/{eventId}/evidence", new CitationRequest(passage.SnapshotId, 0, passage.End, item.Revision), Json)).EnsureSuccessStatusCode();
            await ApproveEvent(client, await GetEvent(client, eventId));
            var map = (await client.GetFromJsonAsync<MapResponse>($"/api/v1/investigations/{inv}/map", Json))!;
            Assert.Equal(25000, Assert.Single(map.Locations).UncertaintyMeters); Assert.Single(map.Events); Assert.NotEmpty(map.Events[0].Evidence);
            Assert.Empty((await client.GetFromJsonAsync<MapResponse>($"/api/v1/investigations/{inv}/map?type=MOVEMENT", Json))!.Events);
            Assert.Empty((await client.GetFromJsonAsync<MapResponse>($"/api/v1/investigations/{inv}/map?from=2027-01-01", Json))!.Events);
            (await client.PutAsJsonAsync($"/api/v1/investigations/{inv}/map/focus", new MapFocusRequest(34, 29, 7), Json)).EnsureSuccessStatusCode();
            location = await GetLocation(client, locationId);
            (await client.PostAsJsonAsync($"/api/v1/locations/{locationId}/remove", new ReviewTemporalRequest(true, location.Revision), Json)).EnsureSuccessStatusCode();
            map = (await client.GetFromJsonAsync<MapResponse>($"/api/v1/investigations/{inv}/map", Json))!; Assert.Empty(map.Locations); Assert.Empty(map.Events);
            Assert.Equal(locationId, (await GetEvent(client, eventId)).LocationId);
        }
        using (var api = Factory()) using (var client = api.CreateClient())
        { var map = (await client.GetFromJsonAsync<MapResponse>($"/api/v1/investigations/{inv}/map", Json))!; Assert.Equal(new(34, 29, 7), map.Focus); }
    }
    [Fact]
    public async Task CrossInvestigationCitationsEntitiesAndInvalidCoordinatesAreRejected()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, _, entity, passage) = await Seed(api, client);
        var (other, _, otherEntity, _) = await Seed(api, client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/events", new EventEditRequest { Title = "Cross entity", EntityIds = [otherEntity] }, Json)).StatusCode);
        var item = await Read<EventResponse>(await client.PostAsJsonAsync($"/api/v1/investigations/{other}/events", new EventEditRequest { Title = "Other workspace" }, Json));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/events/{item.Id}/evidence", new CitationRequest(passage.SnapshotId, 0, passage.End, item.Revision), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/events/{item.Id}/evidence", new CitationRequest(passage.SnapshotId, 1, 2, item.Revision), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/locations", new LocationEditRequest { EntityId = entity, Precision = GeoPrecision.APPROXIMATE, Latitude = 0, Longitude = 0, ConfirmCoordinates = true, Notes = "Missing radius" }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/investigations/{inv}/events?from=2027-01-01&to=2026-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/investigations/{inv}/map?type=999")).StatusCode);
    }
    [Fact]
    public async Task RetryCancelRecoveryAndMalformedResultsNeverPublishCandidates()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, source, _, _) = await Seed(api, client);
        pipeline.Failure = true;
        var job = await Read<JobResponse>(await client.PostAsync($"/api/v1/sources/{source}/process-events", null));
        for (var i = 0; i < 3; i++) await Run(api);
        Assert.Equal(JobState.FAILED, (await client.GetFromJsonAsync<JobResponse>($"/api/v1/processing-jobs/{job.Id}", Json))!.State);
        pipeline.Failure = false; pipeline.Forge = true;
        job = await Read<JobResponse>(await client.PostAsync($"/api/v1/processing-jobs/{job.Id}/retry", null)); await Run(api);
        Assert.Equal("invalid_contract", (await client.GetFromJsonAsync<JobResponse>($"/api/v1/processing-jobs/{job.Id}", Json))!.ErrorCode);
        Assert.Empty((await client.GetFromJsonAsync<PageResult<EventResponse>>($"/api/v1/investigations/{inv}/events?allStatuses=true", Json))!.Items);
        pipeline.Forge = false;
        job = await Read<JobResponse>(await client.PostAsync($"/api/v1/sources/{source}/process-events", null));
        (await client.PostAsync($"/api/v1/processing-jobs/{job.Id}/cancel", null)).EnsureSuccessStatusCode();
        Assert.Equal(JobState.CANCELLED, (await client.GetFromJsonAsync<JobResponse>($"/api/v1/processing-jobs/{job.Id}", Json))!.State);
        job = await Read<JobResponse>(await client.PostAsync($"/api/v1/processing-jobs/{job.Id}/retry", null));
        using (var scope = api.Services.CreateScope())
        { var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>(); (await db.ProcessingJobs.FindAsync(job.Id))!.Start(); await db.SaveChangesAsync(); await scope.ServiceProvider.GetRequiredService<ProcessingJobService>().RecoverAsync(CancellationToken.None); }
        await Run(api); Assert.Equal(JobState.COMPLETED, (await client.GetFromJsonAsync<JobResponse>($"/api/v1/processing-jobs/{job.Id}", Json))!.State);
    }
    [Fact]
    public async Task UnknownLocationIsReviewableButNeverMappedAndLateCancellationDiscardsCandidates()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, source, entity, passage) = await Seed(api, client);
        var location = await Read<LocationResponse>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/locations", new LocationEditRequest
        { EntityId = entity, Precision = GeoPrecision.UNKNOWN, Notes = "Source gives a place name only; coordinates remain unknown." }, Json));
        (await client.PostAsJsonAsync($"/api/v1/locations/{location.Id}/evidence", new CitationRequest(passage.SnapshotId, passage.Start, passage.End, location.Revision), Json)).EnsureSuccessStatusCode();
        location = await GetLocation(client, location.Id);
        (await client.PostAsJsonAsync($"/api/v1/locations/{location.Id}/approve", new ReviewTemporalRequest(true, location.Revision), Json)).EnsureSuccessStatusCode();
        Assert.Empty((await client.GetFromJsonAsync<MapResponse>($"/api/v1/investigations/{inv}/map", Json))!.Locations);
        var job = await Read<JobResponse>(await client.PostAsync($"/api/v1/sources/{source}/process-events", null));
        pipeline.During = async () => (await client.PostAsync($"/api/v1/processing-jobs/{job.Id}/cancel", null)).EnsureSuccessStatusCode();
        await Run(api);
        Assert.Equal(JobState.CANCELLED, (await client.GetFromJsonAsync<JobResponse>($"/api/v1/processing-jobs/{job.Id}", Json))!.State);
        Assert.Empty((await client.GetFromJsonAsync<PageResult<EventResponse>>($"/api/v1/investigations/{inv}/events?allStatuses=true", Json))!.Items);
        Assert.Equal(SourceProcessingStatus.Ready, (await client.GetFromJsonAsync<SourceResponse>($"/api/v1/sources/{source}", Json))!.Source.ProcessingStatus);
    }
    [Fact]
    public async Task FirstEventJobStagesItsSnapshotAtomicallyAndDiscardsItOnCancellation()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, source, _, _) = await Seed(api, client);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
            db.SourceTextSnapshots.RemoveRange(await db.SourceTextSnapshots.Where(x => x.SourceId == source).ToListAsync()); await db.SaveChangesAsync();
        }
        var job = await Read<JobResponse>(await client.PostAsync($"/api/v1/sources/{source}/process-events", null));
        pipeline.During = async () => (await client.PostAsync($"/api/v1/processing-jobs/{job.Id}/cancel", null)).EnsureSuccessStatusCode();
        await Run(api);
        using (var scope = api.Services.CreateScope())
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<ArgusDbContext>().SourceTextSnapshots.Where(x => x.SourceId == source).ToListAsync());
        pipeline.During = null;
        job = await Read<JobResponse>(await client.PostAsync($"/api/v1/sources/{source}/process-events", null)); await Run(api);
        job = (await client.GetFromJsonAsync<JobResponse>($"/api/v1/processing-jobs/{job.Id}", Json))!;
        Assert.Equal(JobState.COMPLETED, job.State); Assert.NotNull(job.SnapshotId);
        var items = (await client.GetFromJsonAsync<PageResult<EventResponse>>($"/api/v1/investigations/{inv}/events?status=PROPOSED", Json))!.Items;
        Assert.Equal(2, items.Count); Assert.All(items, item => Assert.Equal(job.SnapshotId, Assert.Single(item.Evidence).SnapshotId));
    }
    [Fact]
    public async Task EntityMergePreservesEventLinksAndConflictingLocationsRequireExplicitRetirement()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, _, entity, passage) = await Seed(api, client);
        Guid target;
        using (var scope = api.Services.CreateScope())
        { var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>(); var e = new IntelligenceEntity(inv, new() { Name = "Meridian alternate", Type = EntityType.LOCATION }); target = e.Id; db.Entities.Add(e); await db.SaveChangesAsync(); }
        var a = await CreateLocation(client, inv, entity, passage); var b = await CreateLocation(client, inv, target, passage);
        var item = await Read<EventResponse>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/events", new EventEditRequest { Title = "Linked", EntityIds = [entity, target], LocationId = a.Id }, Json));
        var merge = new MergeEntitiesRequest { SourceId = entity, TargetId = target, Confirm = true };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/entities/merge", merge, Json)).StatusCode);
        (await client.PostAsJsonAsync($"/api/v1/locations/{b.Id}/remove", new ReviewTemporalRequest(true, b.Revision), Json)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/entities/merge", merge, Json)).EnsureSuccessStatusCode();
        Assert.Equal(target, (await GetLocation(client, a.Id)).EntityId); Assert.Equal(target, Assert.Single((await GetEvent(client, item.Id)).EntityIds));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/v1/entities/{target}", new EntityEditRequest { Name = "Wrong type", Type = EntityType.PERSON }, Json)).StatusCode);
    }
    [Fact]
    public async Task PhaseFourDatabaseUpgradesWithoutLosingRelationshipsOrSources()
    {
        Directory.CreateDirectory(directory);
        var options = new DbContextOptionsBuilder<ArgusDbContext>().UseSqlite($"Data Source={Path.Combine(directory, "argus.db")}").Options;
        var inv = new Investigation(); inv.Update(new() { Title = "Existing Phase 4 workspace" });
        await using (var db = new ArgusDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync("20261112012741_EntityGraph");
            var source = Source.Create(inv.Id, SourceType.Text, new() { Title = "Existing source" }, "original.txt", 4, new('a', 64)); source.CompleteExtraction("Text", "old");
            var a = new IntelligenceEntity(inv.Id, new() { Name = "A", Type = EntityType.PERSON }); var b = new IntelligenceEntity(inv.Id, new() { Name = "B", Type = EntityType.PERSON });
            db.AddRange(inv, source, a, b, new Relationship(inv.Id, a.Id, b.Id)); await db.SaveChangesAsync();
        }
        using var api = Factory(); using var client = api.CreateClient();
        var relationships = (await client.GetFromJsonAsync<PageResult<RelationshipSummary>>($"/api/v1/investigations/{inv.Id}/relationships", Json))!; Assert.Single(relationships.Items);
        Assert.Empty((await client.GetFromJsonAsync<PageResult<EventResponse>>($"/api/v1/investigations/{inv.Id}/events", Json))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/investigations/{inv.Id}/collaboration")).StatusCode);
    }
    public sealed class EventsPipeline : IEventClient
    {
        public bool Failure { get; set; }
        public bool Forge { get; set; }
        public Func<Task>? During { get; set; }
        public async Task<EventProcessingResponse> ProcessEventsAsync(NlpRequest request, CancellationToken ct)
        {
            if (Failure) throw new ProcessingException("nlp_unavailable", "Unavailable", true);
            if (During is not null) await During();
            var start = request.Text.IndexOf("2026-02-10", StringComparison.Ordinal); var relative = request.Text.IndexOf("Last Tuesday", StringComparison.Ordinal);
            return new EventProcessingResponse("1", request.SourceId, request.TextHash, "UTF16_CODE_UNITS", "date_mentions_v1",
                [new("2026-02-10", start, start + 10, 0, request.Text.Length, request.Text, DatePrecision.EXACT, Forge ? "2026-02-11" : "2026-02-10", "Explicit date", null),
                 new("Last Tuesday", relative, relative + 12, 0, request.Text.Length, request.Text, DatePrecision.UNKNOWN, null, "No reference date inferred", null)]);
        }
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
