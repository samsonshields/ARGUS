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

public sealed class IntelligenceWorkflowTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "argus-nlp-tests-" + Guid.NewGuid());
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly Dictionary<string, string?> Auto = new() { ["Intelligence:AutoProcessImports"] = "true", ["Intelligence:RetryDelaySeconds"] = "0" };
    private TestApi Factory(IIntelligenceClient? client = null) => new(directory, Auto, services =>
    {
        services.RemoveAll<IIntelligenceClient>(); services.AddSingleton(client ?? new TestNlp());
    });
    private static async Task<(Guid Investigation, Guid Source)> Import(HttpClient client, string text = "\U0001F600 Alice Morgan met United Nations.")
    {
        var investigation = (await (await client.PostAsJsonAsync("/api/v1/investigations", new { title = "Fictional NLP workspace" }))
            .Content.ReadFromJsonAsync<InvestigationResponse>(Json))!;
        var response = await client.PostAsJsonAsync($"/api/v1/investigations/{investigation.Id}/sources/text", new PastedSourceRequest { Title = "Fictional report", Text = text }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var source = (await response.Content.ReadFromJsonAsync<SourceResponse>(Json))!;
        Assert.Equal(SourceProcessingStatus.Pending, source.Source.ProcessingStatus);
        return (investigation.Id, source.Source.Id);
    }
    private static async Task Run(TestApi api)
    {
        using var scope = api.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<ProcessingRunner>().RunNextAsync(CancellationToken.None));
    }
    private static async Task<PageResult<MentionResponse>> Mentions(HttpClient client, Guid inv, string? status = null)
        => (await client.GetFromJsonAsync<PageResult<MentionResponse>>($"/api/v1/investigations/{inv}/mentions?status={status}", Json))!;

    [Fact]
    public async Task FullReviewReprocessingAndEvidenceSurviveRestart()
    {
        Guid inv, sourceId, approvedId, mentionId;
        using (var api = Factory())
        using (var client = api.CreateClient())
        {
            (inv, sourceId) = await Import(client);
            var original = await client.GetByteArrayAsync($"/api/v1/sources/{sourceId}/original");
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/sources/{sourceId}/process", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/sources/{sourceId}/extract-text", null)).StatusCode);
            await Run(api);
            var mentions = (await Mentions(client, inv)).Items;
            Assert.Equal(2, mentions.Count);
            Assert.All(mentions, x => { Assert.Equal(ReviewStatus.PROPOSED, x.Status); Assert.Null(x.EntityId); Assert.Null(x.Confidence); });
            var person = mentions.Single(x => x.OriginalType == EntityType.PERSON);
            mentionId = person.Id;
            Assert.Equal(3, person.Start);
            var evidence = (await client.GetFromJsonAsync<MentionEvidence>($"/api/v1/mentions/{person.Id}/evidence", Json))!;
            Assert.Equal("Alice Morgan", evidence.Quote);
            Assert.Contains("\U0001F600", evidence.Before);
            var approved = await client.PostAsJsonAsync($"/api/v1/mentions/{person.Id}/approve", new ApproveMentionRequest { Name = "Alice M. Morgan", Type = EntityType.PERSON }, Json);
            approvedId = (await approved.Content.ReadFromJsonAsync<EntityResponse>(Json))!.Id;
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/mentions/{mentions.Single(x => x.Id != person.Id).Id}/reject", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/mentions/{person.Id}/reject", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/v1/sources/{sourceId}/process", null)).StatusCode);
            await Run(api);
            var after = await Mentions(client, inv);
            Assert.Equal(2, after.Total);
            Assert.Contains(after.Items, x => x.Id == person.Id && x.Status == ReviewStatus.APPROVED);
            Assert.Single((await Mentions(client, inv, "REJECTED")).Items);
            Assert.Equal(original, await client.GetByteArrayAsync($"/api/v1/sources/{sourceId}/original"));
            var jobs = await client.GetFromJsonAsync<PageResult<JobResponse>>($"/api/v1/processing-jobs?sourceId={sourceId}", Json);
            Assert.All(jobs!.Items, x => Assert.Equal(JobState.COMPLETED, x.State));
            using var scope = api.Services.CreateScope();
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ArgusDbContext>().SourceTextSnapshots.CountAsync());
        }
        using (var api = Factory())
        using (var client = api.CreateClient())
        {
            var entity = await client.GetFromJsonAsync<EntityResponse>($"/api/v1/entities/{approvedId}", Json);
            Assert.Equal("Alice M. Morgan", entity!.Name);
            Assert.Equal(1, entity.MentionCount);
            Assert.Equal("Alice Morgan", (await client.GetFromJsonAsync<MentionEvidence>($"/api/v1/mentions/{mentionId}/evidence", Json))!.Quote);
            Assert.Single((await Mentions(client, inv, "APPROVED")).Items);
        }
    }

    [Fact]
    public async Task MentionSearchAndTypeFiltersRetainAllReviewStatesAndSourceScope()
    {
        using var api = Factory();
        using var client = api.CreateClient();
        var (inv, source) = await Import(client);
        await Run(api);
        var mentions = (await Mentions(client, inv)).Items;
        var person = mentions.Single(x => x.OriginalType == EntityType.PERSON);
        var org = mentions.Single(x => x.OriginalType == EntityType.ORGANIZATION);
        (await client.PostAsJsonAsync($"/api/v1/mentions/{person.Id}/approve", new ApproveMentionRequest { Name = "Alice Morgan", Type = EntityType.PERSON }, Json)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/v1/mentions/{org.Id}/reject", null)).EnsureSuccessStatusCode();
        var route = $"/api/v1/investigations/{inv}/mentions";
        var approved = (await client.GetFromJsonAsync<PageResult<MentionResponse>>(route + $"?sourceId={source}&status=APPROVED&type=PERSON&search=mOrGaN", Json))!;
        Assert.Equal(person.Id, Assert.Single(approved.Items).Id);
        var rejected = (await client.GetFromJsonAsync<PageResult<MentionResponse>>(route + "?status=REJECTED&type=ORGANIZATION&search=United%20Nations", Json))!;
        Assert.Equal(org.Id, Assert.Single(rejected.Items).Id);
        Assert.Equal(2, (await client.GetFromJsonAsync<PageResult<MentionResponse>>(route + "?status=&type=&search=", Json))!.Total);
        foreach (var filter in new[] { "status=PROPOSED", "type=PERSON&search=United", $"sourceId={Guid.NewGuid()}" })
            Assert.Empty((await client.GetFromJsonAsync<PageResult<MentionResponse>>(route + "?" + filter, Json))!.Items);
        Assert.Equal(1, (await client.GetFromJsonAsync<PageResult<EntityResponse>>($"/api/v1/investigations/{inv}/entities", Json))!.Total);
    }

    [Theory]
    [InlineData("type=999")]
    [InlineData("type=invalid")]
    [InlineData("status=invalid")]
    public async Task InvalidMentionFiltersAreRejected(string filter)
    {
        using var api = Factory();
        using var client = api.CreateClient();
        var (inv, _) = await Import(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/investigations/{inv}/mentions?{filter}")).StatusCode);
    }

    [Fact]
    public async Task FailuresRetryWithinBudgetAndCanBeManuallyRetried()
    {
        var offline = new TestNlp { Failure = new("nlp_unavailable", "Service unavailable", true) };
        using var api = Factory(offline);
        using var client = api.CreateClient();
        var (inv, source) = await Import(client);
        for (var i = 0; i < 3; i++) await Run(api);
        var job = (await client.GetFromJsonAsync<PageResult<JobResponse>>($"/api/v1/processing-jobs?sourceId={source}", Json))!.Items.Single();
        Assert.Equal(JobState.FAILED, job.State);
        Assert.Equal(3, job.Attempts);
        Assert.Equal("nlp_unavailable", job.ErrorCode);
        Assert.Empty((await Mentions(client, inv)).Items);
        Assert.NotEmpty(await client.GetByteArrayAsync($"/api/v1/sources/{source}/original"));
        offline.Failure = null;
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/v1/processing-jobs/{job.Id}/retry", null)).StatusCode);
        await Run(api);
        Assert.Equal(2, (await Mentions(client, inv)).Total);
    }

    [Fact]
    public async Task PendingAndRunningJobsRecoverAcrossRestart()
    {
        Guid source;
        using (var api = Factory())
        using (var client = api.CreateClient())
        {
            (_, source) = await Import(client);
            using var scope = api.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
            var job = await db.ProcessingJobs.SingleAsync(); job.Start(); await db.SaveChangesAsync();
        }
        using (var api = Factory())
        using (var client = api.CreateClient())
        {
            using (var scope = api.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<ProcessingJobService>().RecoverAsync(CancellationToken.None);
            await Run(api);
            var job = (await client.GetFromJsonAsync<PageResult<JobResponse>>($"/api/v1/processing-jobs?sourceId={source}", Json))!.Items.Single();
            Assert.Equal(JobState.COMPLETED, job.State);
            Assert.Equal(2, job.Attempts);
        }
    }

    [Fact]
    public async Task CancellingRunningJobDiscardsLateResults()
    {
        var nlp = new TestNlp { Block = true };
        using var api = Factory(nlp);
        using var client = api.CreateClient();
        var (inv, source) = await Import(client);
        var run = Run(api);
        await nlp.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var job = (await client.GetFromJsonAsync<PageResult<JobResponse>>($"/api/v1/processing-jobs?sourceId={source}", Json))!.Items.Single();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/processing-jobs/{job.Id}/cancel", null)).StatusCode);
        nlp.Release.TrySetResult();
        await run;
        Assert.Empty((await Mentions(client, inv)).Items);
        var cancelled = await client.GetFromJsonAsync<JobResponse>($"/api/v1/processing-jobs/{job.Id}", Json);
        Assert.Equal(JobState.CANCELLED, cancelled!.State);
    }

    [Theory]
    [InlineData("span")]
    [InlineData("confidence")]
    public async Task InvalidResponsesNeverPartiallyWriteCandidates(string invalid)
    {
        using var api = Factory(new TestNlp { Invalid = invalid });
        using var client = api.CreateClient();
        var (inv, source) = await Import(client);
        await Run(api);
        Assert.Empty((await Mentions(client, inv)).Items);
        var job = (await client.GetFromJsonAsync<PageResult<JobResponse>>($"/api/v1/processing-jobs?sourceId={source}", Json))!.Items.Single();
        Assert.Equal(JobState.FAILED, job.State);
        Assert.Equal(1, job.Attempts);
        Assert.Equal("invalid_contract", job.ErrorCode);
    }

    [Fact]
    public async Task AnalystAssociationAliasesCorrectionAndMergeKeepMentionEvidence()
    {
        using var api = Factory();
        using var client = api.CreateClient();
        var (inv, source) = await Import(client);
        await Run(api);
        var person = (await Mentions(client, inv)).Items.Single(x => x.OriginalType == EntityType.PERSON);
        var first = (await (await client.PostAsJsonAsync($"/api/v1/mentions/{person.Id}/approve", new ApproveMentionRequest { Name = "Alice Morgan", Type = EntityType.PERSON }, Json)).Content.ReadFromJsonAsync<EntityResponse>(Json))!;
        var secondSource = (await (await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/sources/text", new PastedSourceRequest { Title = "Second report", Text = "Alice Morgan visited United Nations." }, Json)).Content.ReadFromJsonAsync<SourceResponse>(Json))!.Source.Id;
        await Run(api);
        var second = (await Mentions(client, inv)).Items.Single(x => x.SourceId == secondSource && x.OriginalType == EntityType.PERSON);
        var suggestions = (await client.GetFromJsonAsync<List<EntitySuggestion>>($"/api/v1/mentions/{second.Id}/suggestions", Json))!;
        Assert.Equal(first.Id, suggestions.Single().Entity.Id);
        // Similarity never automatically associates a person; approval explicitly chooses the target.
        var associated = await client.PostAsJsonAsync($"/api/v1/mentions/{second.Id}/approve", new ApproveMentionRequest { Name = first.Name, Type = first.Type, EntityId = first.Id }, Json);
        Assert.Equal(2, (await associated.Content.ReadFromJsonAsync<EntityResponse>(Json))!.MentionCount);
        var org = (await Mentions(client, inv)).Items.First(x => x.Status == ReviewStatus.PROPOSED);
        var duplicate = (await (await client.PostAsJsonAsync($"/api/v1/mentions/{org.Id}/approve", new ApproveMentionRequest { Name = "Alice M. Morgan", Type = EntityType.PERSON }, Json)).Content.ReadFromJsonAsync<EntityResponse>(Json))!;
        var noConfirm = await client.PostAsJsonAsync("/api/v1/entities/merge", new MergeEntitiesRequest { SourceId = duplicate.Id, TargetId = first.Id }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, noConfirm.StatusCode);
        var merged = (await (await client.PostAsJsonAsync("/api/v1/entities/merge", new MergeEntitiesRequest { SourceId = duplicate.Id, TargetId = first.Id, Confirm = true }, Json)).Content.ReadFromJsonAsync<EntityResponse>(Json))!;
        Assert.Equal(3, merged.MentionCount);
        Assert.Contains("Alice M. Morgan", merged.Aliases);
        var evidence = (await client.GetFromJsonAsync<MentionEvidence>($"/api/v1/mentions/{org.Id}/evidence", Json))!;
        Assert.Equal("United Nations", evidence.Quote);
        Assert.Equal(EntityType.ORGANIZATION, evidence.Mention.OriginalType);
        Assert.Equal(first.Id, evidence.Mention.EntityId);
        var updated = await client.PutAsJsonAsync($"/api/v1/entities/{first.Id}", new EntityEditRequest { Name = "Alice Morgan (fictional)", Type = EntityType.PERSON }, Json);
        Assert.Contains("Alice Morgan", (await updated.Content.ReadFromJsonAsync<EntityResponse>(Json))!.Aliases);
        Assert.Equal(first.Id, (await client.GetFromJsonAsync<EntityResponse>($"/api/v1/entities/{duplicate.Id}", Json))!.MergedIntoId);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/entities/merge", new MergeEntitiesRequest { SourceId = first.Id, TargetId = duplicate.Id, Confirm = true }, Json)).StatusCode);
    }

    [Fact]
    public async Task ChangedExtractionKeepsPreviouslyReviewedSnapshot()
    {
        using var api = Factory();
        using var client = api.CreateClient();
        var (inv, source) = await Import(client);
        await Run(api);
        var mention = (await Mentions(client, inv)).Items.First();
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
            var stored = await db.Sources.SingleAsync(); stored.CompleteExtraction("Different extraction text", "changed-test"); await db.SaveChangesAsync();
        }
        var evidence = (await client.GetFromJsonAsync<MentionEvidence>($"/api/v1/mentions/{mention.Id}/evidence", Json))!;
        Assert.Equal("Alice Morgan", evidence.Quote);
        Assert.Equal(mention.SnapshotId, evidence.Mention.SnapshotId);
        Assert.Contains("\U0001F600", evidence.Before);
    }

    [Fact]
    public async Task PaginationValidationAndOpenApiWorkWithoutFutureCollaborationEndpoints()
    {
        using var api = Factory();
        using var client = api.CreateClient();
        var (inv, _) = await Import(client);
        await Run(api);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/processing-jobs?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/investigations/{inv}/mentions?status=999")).StatusCode);
        var page = await client.GetFromJsonAsync<PageResult<MentionResponse>>($"/api/v1/investigations/{inv}/mentions?pageSize=1&page=2", Json);
        Assert.Single(page!.Items);
        Assert.Equal(2, page.Total);
        var schema = await client.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/v1/sources/{id}/process", schema);
        Assert.Contains("/graph", schema);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/investigations/{inv}/collaboration")).StatusCode);
    }

    [Fact]
    public async Task PhaseTwoUpgradePreservesExistingSourceAndItsOriginal()
    {
        Directory.CreateDirectory(directory);
        var options = new DbContextOptionsBuilder<ArgusDbContext>().UseSqlite($"Data Source={Path.Combine(directory, "argus.db")}").Options;
        var inv = new Investigation(); inv.Update(new() { Title = "Existing Phase 2 workspace" });
        var bytes = System.Text.Encoding.UTF8.GetBytes("Existing source text");
        var source = Source.Create(inv.Id, SourceType.Text, new() { Title = "Existing Phase 2 source", Reliability = SourceReliability.B },
            "existing.txt", bytes.Length, SourceTextSnapshot.Hash("Existing source text"));
        source.CompleteExtraction("Existing source text", "phase2");
        Directory.CreateDirectory(Path.Combine(directory, "documents"));
        await File.WriteAllBytesAsync(Path.Combine(directory, "documents", source.FilePath), bytes);
        await using (var db = new ArgusDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync("20261111234541_SourceDocuments");
            db.Add(inv); db.Add(source); await db.SaveChangesAsync();
        }
        using var api = Factory();
        using var client = api.CreateClient();
        var stored = await client.GetFromJsonAsync<SourceResponse>($"/api/v1/sources/{source.Id}", Json);
        Assert.Equal(SourceReliability.B, stored!.Source.Reliability);
        Assert.Equal("Existing source text", stored.ExtractedText);
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/v1/sources/{source.Id}/original"));
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/v1/sources/{source.Id}/process", null)).StatusCode);
        await Run(api);
    }

    [Fact]
    public async Task CrossInvestigationAssociationAndMergeAreRejected()
    {
        using var api = Factory(); using var client = api.CreateClient();
        var (firstInv, _) = await Import(client); await Run(api);
        var person = (await Mentions(client, firstInv)).Items.First(x => x.OriginalType == EntityType.PERSON);
        var first = (await (await client.PostAsJsonAsync($"/api/v1/mentions/{person.Id}/approve", new ApproveMentionRequest { Name = "Alice Morgan", Type = EntityType.PERSON }, Json)).Content.ReadFromJsonAsync<EntityResponse>(Json))!;
        var (secondInv, _) = await Import(client); await Run(api);
        var otherPerson = (await Mentions(client, secondInv)).Items.First(x => x.OriginalType == EntityType.PERSON);
        var associated = await client.PostAsJsonAsync($"/api/v1/mentions/{otherPerson.Id}/approve", new ApproveMentionRequest { Name = first.Name, Type = first.Type, EntityId = first.Id }, Json);
        Assert.Equal(HttpStatusCode.Conflict, associated.StatusCode);
        var second = (await (await client.PostAsJsonAsync($"/api/v1/mentions/{otherPerson.Id}/approve", new ApproveMentionRequest { Name = "Alice Morgan", Type = EntityType.PERSON }, Json)).Content.ReadFromJsonAsync<EntityResponse>(Json))!;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/entities/merge", new MergeEntitiesRequest { SourceId = first.Id, TargetId = second.Id, Confirm = true }, Json)).StatusCode);
        Assert.Equal(1, (await client.GetFromJsonAsync<EntityResponse>($"/api/v1/entities/{first.Id}", Json))!.MentionCount);
    }

    [Fact]
    public async Task AsyncExtractionFailurePreservesOriginalAndDoesNotCallNlp()
    {
        var nlp = new TestNlp();
        using var api = Factory(nlp); using var client = api.CreateClient();
        var (inv, _) = await Import(client); await Run(api);
        var calls = nlp.Calls;
        var bytes = System.Text.Encoding.UTF8.GetBytes("%PDF-1.7 malformed training file");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Malformed PDF"), "Title"); form.Add(new ByteArrayContent(bytes), "File", "malformed.pdf");
        var created = await client.PostAsync($"/api/v1/investigations/{inv}/sources", form);
        var source = (await created.Content.ReadFromJsonAsync<SourceResponse>(Json))!.Source.Id;
        await Run(api);
        var failed = await client.GetFromJsonAsync<SourceResponse>($"/api/v1/sources/{source}", Json);
        Assert.Equal(SourceProcessingStatus.Failed, failed!.Source.ProcessingStatus);
        var job = (await client.GetFromJsonAsync<PageResult<JobResponse>>($"/api/v1/processing-jobs?sourceId={source}", Json))!.Items.Single();
        Assert.Equal(JobState.FAILED, job.State); Assert.Equal("text_extraction_failed", job.ErrorCode);
        Assert.Equal(calls, nlp.Calls);
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/v1/sources/{source}/original"));
    }
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
    private sealed class TestNlp : IIntelligenceClient
    {
        public ProcessingException? Failure { get; set; }
        public string? Invalid { get; set; }
        public bool Block { get; set; }
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> IsReadyAsync(CancellationToken ct) => Task.FromResult(Failure is null);
        public async Task<NlpResponse> ProcessAsync(NlpRequest request, CancellationToken ct)
        {
            Calls++;
            Started.TrySetResult();
            if (Block) await Release.Task.WaitAsync(ct);
            if (Failure is not null) throw Failure;
            var candidates = new List<NlpCandidate>();
            foreach (var (text, type) in new[] { ("Alice Morgan", EntityType.PERSON), ("United Nations", EntityType.ORGANIZATION) })
            {
                var start = request.Text.IndexOf(text, StringComparison.Ordinal);
                if (start >= 0) candidates.Add(new(text, type, start, start + text.Length, null));
            }
            if (Invalid == "span") candidates.Add(new("Bad offset", EntityType.OTHER, 0, 1, null));
            if (Invalid == "confidence") candidates[0] = candidates[0] with { Confidence = .9 };
            return new("1", request.SourceId, request.TextHash, "UTF16_CODE_UNITS", new("test", "1", "3.8"), candidates);
        }
    }
}
