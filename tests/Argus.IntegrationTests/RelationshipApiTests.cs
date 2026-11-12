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

public sealed class RelationshipApiTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "argus-graph-tests-" + Guid.NewGuid());
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly FakeRelationships pipeline = new();
    private TestApi Factory() => new(directory, new() { ["Intelligence:RetryDelaySeconds"] = "0" }, services =>
    {
        services.RemoveAll<IRelationshipClient>(); services.AddSingleton<IRelationshipClient>(pipeline);
    });
    private static async Task<(Guid Inv, Guid Source, Guid First, Guid Second)> Seed(TestApi api, HttpClient client)
    {
        var inv = (await (await client.PostAsJsonAsync("/api/v1/investigations", new { title = "Fictional graph workspace" })).Content.ReadFromJsonAsync<InvestigationResponse>(Json))!.Id;
        var text = "\U0001F600 Alice Morgan met Aster Maritime.";
        var sourceId = (await (await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/sources/text", new PastedSourceRequest { Title = "Fictional graph report", Text = text }, Json)).Content.ReadFromJsonAsync<SourceResponse>(Json))!.Source.Id;
        using var scope = api.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
        var source = await db.Sources.SingleAsync(x => x.Id == sourceId); var snapshot = new SourceTextSnapshot(source);
        var job = new ProcessingJob(source); job.Start(); job.AttachSnapshot(snapshot.Id); job.Complete(2);
        var first = new IntelligenceEntity(inv, new() { Name = "Alice Morgan", Type = EntityType.PERSON });
        var second = new IntelligenceEntity(inv, new() { Name = "Aster Maritime (fictional)", Type = EntityType.PERSON });
        var a = new EntityMention(source, snapshot, job, new("Alice Morgan", EntityType.PERSON, 3, 15, null), new("test", "1", "3.8"));
        var start = text.IndexOf("Aster", StringComparison.Ordinal);
        var b = new EntityMention(source, snapshot, job, new("Aster Maritime", EntityType.ORGANIZATION, start, start + 14, null), new("test", "1", "3.8"));
        a.Approve(first); b.Approve(second); db.AddRange(snapshot, job, first, second, a, b); await db.SaveChangesAsync();
        return (inv, sourceId, first.Id, second.Id);
    }
    private static async Task Run(TestApi api) { using var scope = api.Services.CreateScope(); Assert.True(await scope.ServiceProvider.GetRequiredService<ProcessingRunner>().RunNextAsync(CancellationToken.None)); }
    private static async Task<JobResponse> Queue(HttpClient client, Guid source)
    {
        var response = await client.PostAsync($"/api/v1/sources/{source}/process-relationships", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<JobResponse>(Json))!;
        Assert.Equal(JobKind.RELATIONSHIPS, job.Kind); return job;
    }
    private static async Task<RelationshipDetail> OnlyRelationship(HttpClient client, Guid inv)
    {
        var list = (await client.GetFromJsonAsync<PageResult<RelationshipSummary>>($"/api/v1/investigations/{inv}/relationships", Json))!;
        var id = Assert.Single(list.Items).Id;
        return (await client.GetFromJsonAsync<RelationshipDetail>($"/api/v1/relationships/{id}", Json))!;
    }
    [Fact]
    public async Task GenerateReviewFilterAndPersistenceKeepExactEvidence()
    {
        Guid inv, source, relationshipId; string quote;
        using (var api = Factory()) using (var client = api.CreateClient())
        {
            (inv, source, _, _) = await Seed(api, client);
            var job = await Queue(client, source); await Run(api);
            Assert.Equal(JobState.COMPLETED, (await client.GetFromJsonAsync<JobResponse>($"/api/v1/processing-jobs/{job.Id}", Json))!.State);
            var detail = await OnlyRelationship(client, inv); relationshipId = detail.Relationship.Id;
            Assert.Equal(ReviewStatus.PROPOSED, detail.Relationship.Status);
            var evidence = Assert.Single(detail.Evidence); quote = evidence.Quote;
            Assert.StartsWith("\U0001F600", quote); Assert.Equal(ReviewStatus.PROPOSED, evidence.Status);
            var proposed = (await client.GetFromJsonAsync<GraphResponse>($"/api/v1/investigations/{inv}/graph", Json))!;
            Assert.Equal(2, proposed.Nodes.Count); Assert.Single(proposed.Edges);
            Assert.Empty((await client.GetFromJsonAsync<GraphResponse>($"/api/v1/investigations/{inv}/graph?includeProposed=false", Json))!.Edges);
            var bad = await client.PostAsJsonAsync($"/api/v1/relationships/{relationshipId}/approve", new RelationshipReviewRequest { Type = RelationshipType.MEMBER_OF }, Json);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            var request = new RelationshipReviewRequest { Type = RelationshipType.MEMBER_OF, Notes = "Explicit fictional training assertion reviewed", EvidenceIds = [evidence.Id], ConfirmEvidenceReviewed = true, Reverse = true };
            var result = await client.PostAsJsonAsync($"/api/v1/relationships/{relationshipId}/approve", request, Json);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var approved = (await result.Content.ReadFromJsonAsync<RelationshipDetail>(Json))!;
            Assert.Equal(1, approved.Relationship.ApprovedEvidenceCount);
            Assert.Equal(detail.Relationship.TargetEntityId, approved.Relationship.SourceEntityId);
            var graph = (await client.GetFromJsonAsync<GraphResponse>($"/api/v1/investigations/{inv}/graph?includeProposed=false", Json))!;
            Assert.True(Assert.Single(graph.Edges).Directed);
            var path = await client.GetFromJsonAsync<GraphPath>($"/api/v1/investigations/{inv}/graph/path?sourceId={approved.Relationship.SourceEntityId}&targetId={approved.Relationship.TargetEntityId}&respectDirection=true", Json);
            Assert.True(path!.Found);
            var reverse = await client.GetFromJsonAsync<GraphPath>($"/api/v1/investigations/{inv}/graph/path?sourceId={approved.Relationship.TargetEntityId}&targetId={approved.Relationship.SourceEntityId}&respectDirection=true", Json);
            Assert.False(reverse!.Found);
            await Queue(client, source); await Run(api);
            Assert.Equal(relationshipId, (await OnlyRelationship(client, inv)).Relationship.Id);
            Assert.Single((await OnlyRelationship(client, inv)).Evidence);
        }
        using (var api = Factory()) using (var client = api.CreateClient())
        {
            var detail = await client.GetFromJsonAsync<RelationshipDetail>($"/api/v1/relationships/{relationshipId}", Json);
            Assert.Equal(quote, Assert.Single(detail!.Evidence).Quote);
            Assert.Equal(ReviewStatus.APPROVED, detail.Relationship.Status);
            Assert.NotEmpty(await client.GetByteArrayAsync($"/api/v1/sources/{source}/original"));
        }
    }
    [Fact]
    public async Task RejectionIsStableAndDoesNotEnterGraph()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, source, _, _) = await Seed(api, client);
        await Queue(client, source); await Run(api); var detail = await OnlyRelationship(client, inv);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/relationships/{detail.Relationship.Id}/reject", null)).StatusCode);
        await Queue(client, source); await Run(api);
        var rejected = await OnlyRelationship(client, inv);
        Assert.Equal(ReviewStatus.REJECTED, rejected.Relationship.Status);
        Assert.All(rejected.Evidence, item => Assert.Equal(ReviewStatus.REJECTED, item.Status));
        Assert.Empty((await client.GetFromJsonAsync<GraphResponse>($"/api/v1/investigations/{inv}/graph", Json))!.Edges);
    }
    [Fact]
    public async Task MergeRetiresSelfLinksAndKeepsCitationHistory()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, source, first, second) = await Seed(api, client);
        await Queue(client, source); await Run(api); var original = await OnlyRelationship(client, inv);
        var merged = await client.PostAsJsonAsync("/api/v1/entities/merge", new MergeEntitiesRequest { SourceId = first, TargetId = second, Confirm = true }, Json);
        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);
        var graph = (await client.GetFromJsonAsync<GraphResponse>($"/api/v1/investigations/{inv}/graph", Json))!;
        Assert.Single(graph.Nodes); Assert.Empty(graph.Edges);
        var history = (await client.GetFromJsonAsync<RelationshipDetail>($"/api/v1/relationships/{original.Relationship.Id}", Json))!;
        Assert.NotNull(history.Relationship.RetiredReason);
        Assert.Equal(original.Evidence.Single().Quote, history.Evidence.Single().Quote);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidOrUnavailablePipelineNeverPartiallyStoresEdges(bool unavailable)
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, source, _, _) = await Seed(api, client);
        pipeline.Invalid = !unavailable; pipeline.Unavailable = unavailable;
        var queued = await Queue(client, source); await Run(api);
        if (unavailable) { await Run(api); await Run(api); }
        var job = (await client.GetFromJsonAsync<JobResponse>($"/api/v1/processing-jobs/{queued.Id}", Json))!;
        Assert.Equal(JobState.FAILED, job.State); Assert.Equal(unavailable ? 3 : 1, job.Attempts);
        Assert.Empty((await client.GetFromJsonAsync<GraphResponse>($"/api/v1/investigations/{inv}/graph", Json))!.Edges);
        Assert.Equal(SourceProcessingStatus.Ready, (await client.GetFromJsonAsync<SourceResponse>($"/api/v1/sources/{source}", Json))!.Source.ProcessingStatus);
        pipeline.Unavailable = pipeline.Invalid = false;
        var retry = await client.PostAsync($"/api/v1/processing-jobs/{job.Id}/retry", null);
        Assert.Equal(JobKind.RELATIONSHIPS, (await retry.Content.ReadFromJsonAsync<JobResponse>(Json))!.Kind);
        await Run(api); Assert.Single((await client.GetFromJsonAsync<GraphResponse>($"/api/v1/investigations/{inv}/graph", Json))!.Edges);
    }
    [Fact]
    public async Task EmptyInvalidCrossInvestigationAndCancellationAreSafe()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, source, first, _) = await Seed(api, client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/investigations/{inv}/graph?entityType=999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/investigations/{inv}/relationships?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/investigations/{inv}/graph/path?sourceId={first}&targetId={Guid.NewGuid()}")).StatusCode);
        var job = await Queue(client, source);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/sources/{source}/process-relationships", null)).StatusCode);
        await client.PostAsync($"/api/v1/processing-jobs/{job.Id}/cancel", null);
        Assert.Equal(SourceProcessingStatus.Ready, (await client.GetFromJsonAsync<SourceResponse>($"/api/v1/sources/{source}", Json))!.Source.ProcessingStatus);
        var empty = (await (await client.PostAsJsonAsync("/api/v1/investigations", new { title = "Empty graph" })).Content.ReadFromJsonAsync<InvestigationResponse>(Json))!.Id;
        var graph = (await client.GetFromJsonAsync<GraphResponse>($"/api/v1/investigations/{empty}/graph", Json))!;
        Assert.Empty(graph.Nodes); Assert.Empty(graph.Edges); Assert.Equal(0, graph.ComponentCount);
        Assert.Empty((await (await client.PostAsync($"/api/v1/investigations/{empty}/relationships/generate", null)).Content.ReadFromJsonAsync<GenerationQueueResult>(Json))!.Jobs);
    }
    [Fact]
    public async Task EvidenceRemainsOnSnapshotAfterCurrentTextChanges()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, source, _, _) = await Seed(api, client);
        await Queue(client, source); await Run(api); var original = await OnlyRelationship(client, inv);
        using (var scope = api.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>(); (await db.Sources.FindAsync(source))!.CompleteExtraction("Changed current extraction", "v2"); await db.SaveChangesAsync(); }
        Assert.Equal(original.Evidence.Single().Quote, (await OnlyRelationship(client, inv)).Evidence.Single().Quote);
    }
    [Fact]
    public async Task UnrelatedEvidenceCannotApproveRelationship()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, source, _, _) = await Seed(api, client);
        await Queue(client, source); await Run(api); var relationship = await OnlyRelationship(client, inv);
        var result = await client.PostAsJsonAsync($"/api/v1/relationships/{relationship.Relationship.Id}/approve", new RelationshipReviewRequest { ConfirmEvidenceReviewed = true, EvidenceIds = [Guid.NewGuid()] }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(ReviewStatus.PROPOSED, (await OnlyRelationship(client, inv)).Relationship.Status);
    }
    [Fact]
    public async Task PhaseThreeJobHistoryDefaultsToEntityKindOnUpgrade()
    {
        Directory.CreateDirectory(directory);
        var options = new DbContextOptionsBuilder<ArgusDbContext>().UseSqlite($"Data Source={Path.Combine(directory, "argus.db")}").Options;
        var inv = new Investigation(); inv.Update(new() { Title = "Existing Phase 3 history" });
        var source = Source.Create(inv.Id, SourceType.Text, new() { Title = "Existing source" }, "old.txt", 4, new('a', 64));
        source.CompleteExtraction("Text", "old"); var jobId = Guid.NewGuid(); var now = DateTime.UtcNow;
        await using (var db = new ArgusDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync("20261112003231_EntityProcessing");
            db.AddRange(inv, source); await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ProcessingJobs (Id,SourceId,InvestigationId,State,Attempts,CreatedAt,UpdatedAt,CandidateCount,Revision) VALUES ({jobId},{source.Id},{inv.Id},'COMPLETED',1,{now},{now},0,1)");
        }
        using var api = Factory(); using var client = api.CreateClient();
        var job = await client.GetFromJsonAsync<JobResponse>($"/api/v1/processing-jobs/{jobId}", Json);
        Assert.Equal(JobKind.ENTITIES, job!.Kind); Assert.Equal(JobState.COMPLETED, job.State);
        Assert.Equal("Text", (await client.GetFromJsonAsync<SourceResponse>($"/api/v1/sources/{source.Id}", Json))!.ExtractedText);
        var graph = await client.GetFromJsonAsync<GraphResponse>($"/api/v1/investigations/{inv.Id}/graph", Json);
        Assert.Empty(graph!.Edges);
    }
    [Fact]
    public async Task NewPassagesOnReviewedCoMentionRequireTheirOwnReview()
    {
        using var api = Factory(); using var client = api.CreateClient(); var (inv, source, first, second) = await Seed(api, client);
        await Queue(client, source); await Run(api); var original = await OnlyRelationship(client, inv);
        await client.PostAsJsonAsync($"/api/v1/relationships/{original.Relationship.Id}/approve", new RelationshipReviewRequest
        {
            ConfirmEvidenceReviewed = true,
            EvidenceIds = [original.Evidence.Single().Id]
        }, Json);
        const string text = "Alice Morgan met Aster Maritime again.";
        var sourceId = (await (await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/sources/text",
            new PastedSourceRequest { Title = "Second fictional report", Text = text }, Json)).Content.ReadFromJsonAsync<SourceResponse>(Json))!.Source.Id;
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>(); var stored = await db.Sources.FindAsync(sourceId);
            var snapshot = new SourceTextSnapshot(stored!); var job = new ProcessingJob(stored!); job.Start(); job.Complete(2);
            var a = new EntityMention(stored!, snapshot, job, new("Alice Morgan", EntityType.PERSON, 0, 12, null), new("test", "1", "3.8"));
            // Use actual offsets from the immutable text, independent of candidate-generation logic.
            var start = text.IndexOf("Aster", StringComparison.Ordinal);
            var b = new EntityMention(stored!, snapshot, job, new("Aster Maritime", EntityType.ORGANIZATION, start, start + 14, null), new("test", "1", "3.8"));
            a.Approve((await db.Entities.FindAsync(first))!); b.Approve((await db.Entities.FindAsync(second))!);
            db.AddRange(snapshot, job, a, b); await db.SaveChangesAsync();
        }
        await Queue(client, sourceId); await Run(api);
        var augmented = await OnlyRelationship(client, inv);
        Assert.Equal(ReviewStatus.APPROVED, augmented.Relationship.Status);
        Assert.Equal(1, augmented.Relationship.ApprovedEvidenceCount); Assert.Equal(1, augmented.Relationship.ProposedEvidenceCount);
        var pending = Assert.Single(augmented.Evidence, x => x.Status == ReviewStatus.PROPOSED);
        await client.PostAsJsonAsync($"/api/v1/relationships/{original.Relationship.Id}/approve", new RelationshipReviewRequest
        {
            ConfirmEvidenceReviewed = true,
            EvidenceIds = [pending.Id]
        }, Json);
        Assert.Equal(2, (await OnlyRelationship(client, inv)).Relationship.ApprovedEvidenceCount);
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private sealed class FakeRelationships : IRelationshipClient
    {
        public bool Invalid { get; set; }
        public bool Unavailable { get; set; }
        public Task<RelationshipProcessingResponse> ProcessAsync(RelationshipProcessingRequest request, CancellationToken ct)
        {
            if (Unavailable) throw new ProcessingException("nlp_unavailable", "Unavailable local service", true);
            var mentions = request.Mentions.GroupBy(x => x.EntityId).Select(x => x.First()).ToArray();
            var candidate = new RelationshipCandidate(mentions[0].Id, mentions[1].Id, mentions[0].EntityId,
                Invalid ? Guid.NewGuid() : mentions[1].EntityId, RelationshipType.MENTIONED_WITH, 0, request.Text.Length, request.Text, null);
            return Task.FromResult(new RelationshipProcessingResponse("1", request.SourceId, request.TextHash, "UTF16_CODE_UNITS", "sentence_cooccurrence_v1", [candidate]));
        }
    }
}
