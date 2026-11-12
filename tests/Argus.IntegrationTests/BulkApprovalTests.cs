using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.IntegrationTests;

public sealed class BulkApprovalTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "argus-bulk-tests-" + Guid.NewGuid());
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static string Route(Guid inv) => $"/api/v1/investigations/{inv}/mentions";
    private static async Task<Guid> Investigation(HttpClient client)
        => (await (await client.PostAsJsonAsync("/api/v1/investigations", new { title = "Fictional bulk review" })).Content.ReadFromJsonAsync<InvestigationResponse>(Json))!.Id;
    private static async Task<(Guid Source, List<EntityMention> Mentions)> Seed(TestApi api, HttpClient client, Guid inv, params (string Text, EntityType Type)[] specs)
    {
        var text = "Fictional fixture " + Guid.NewGuid() + "\n\U0001F600 " + string.Join("\n", specs.Select(x => x.Text));
        var response = await client.PostAsJsonAsync($"/api/v1/investigations/{inv}/sources/text", new PastedSourceRequest { Title = "Fictional evidence", Text = text }, Json);
        response.EnsureSuccessStatusCode();
        var sourceId = (await response.Content.ReadFromJsonAsync<SourceResponse>(Json))!.Source.Id;
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
        var source = await db.Sources.SingleAsync(x => x.Id == sourceId);
        var snapshot = new SourceTextSnapshot(source);
        var job = new ProcessingJob(source); job.Start(); job.AttachSnapshot(snapshot.Id); job.Complete(specs.Length);
        var mentions = new List<EntityMention>();
        var offset = 0;
        foreach (var spec in specs)
        {
            var start = source.ExtractedText.IndexOf(spec.Text, offset, StringComparison.Ordinal);
            mentions.Add(new(source, snapshot, job, new(spec.Text, spec.Type, start, start + spec.Text.Length, null), new("test", "1", "3.8")));
            offset = start + spec.Text.Length;
        }
        db.AddRange(snapshot, job); db.EntityMentions.AddRange(mentions); await db.SaveChangesAsync();
        return (sourceId, mentions);
    }
    private static async Task<BulkMentionPreview> Preview(HttpClient client, Guid inv, string filters = "")
        => (await client.GetFromJsonAsync<BulkMentionPreview>(Route(inv) + "/bulk-approval-preview" + filters, Json))!;
    private static Task<HttpResponseMessage> Approve(HttpClient client, Guid inv, Guid[] ids, bool group = false, bool confirm = true)
        => client.PostAsJsonAsync(Route(inv) + "/approve-bulk", new BulkApproveMentionsRequest { CandidateIds = ids, Confirm = confirm, GroupIdenticalNames = group }, Json);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkApprovesAllPagesAtomicallyWithProvenanceAndExplicitGrouping(bool group)
    {
        Guid inv, source; int created;
        using (var api = new TestApi(directory))
        using (var client = api.CreateClient())
        {
            inv = await Investigation(client);
            var specs = Enumerable.Range(0, 44).Select(i => (i % 2 == 0 ? "Alice Morgan" : "alice   Morgan", EntityType.PERSON))
                .Append(("Alice Morgan", EntityType.ORGANIZATION)).Append((new string('x', 201), EntityType.OTHER)).ToArray();
            var seeded = await Seed(api, client, inv, specs); source = seeded.Source;
            var original = await client.GetByteArrayAsync($"/api/v1/sources/{source}/original");
            (await client.PostAsJsonAsync($"/api/v1/mentions/{seeded.Mentions[0].Id}/approve", new ApproveMentionRequest { Name = "Alice Morgan", Type = EntityType.PERSON }, Json)).EnsureSuccessStatusCode();
            (await client.PostAsync($"/api/v1/mentions/{seeded.Mentions[1].Id}/reject", null)).EnsureSuccessStatusCode();
            var preview = await Preview(client, inv);
            Assert.Equal(44, preview.ProposedCount); Assert.Equal(1, preview.SkippedCount);
            Assert.Equal(43, preview.CandidateIds.Count); Assert.Equal(2, preview.DistinctNameTypeCount);
            var later = await Seed(api, client, inv, ("Bob Carter", EntityType.PERSON));
            var response = await Approve(client, inv, preview.CandidateIds.ToArray(), group);
            response.EnsureSuccessStatusCode();
            var result = (await response.Content.ReadFromJsonAsync<BulkMentionApprovalResult>(Json))!;
            created = group ? 2 : 43;
            Assert.Equal(43, result.ApprovedCount); Assert.Equal(created, result.CreatedEntityCount);
            Assert.Equal(original, await client.GetByteArrayAsync($"/api/v1/sources/{source}/original"));
            Assert.Equal(HttpStatusCode.Conflict, (await Approve(client, inv, preview.CandidateIds.ToArray(), group)).StatusCode);
            using var scope = api.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
            Assert.Equal(created + 1, await db.Entities.CountAsync());
            Assert.Equal(44, await db.EntityMentions.CountAsync(x => x.Status == ReviewStatus.APPROVED));
            Assert.Equal(1, await db.EntityMentions.CountAsync(x => x.Status == ReviewStatus.REJECTED));
            Assert.Equal(ReviewStatus.PROPOSED, (await db.EntityMentions.FindAsync(later.Mentions[0].Id))!.Status);
            foreach (var before in seeded.Mentions)
            {
                var after = (await db.EntityMentions.FindAsync(before.Id))!;
                Assert.Equal((before.Text, before.Start, before.End, before.SnapshotId, before.ModelName, before.OriginalType),
                    (after.Text, after.Start, after.End, after.SnapshotId, after.ModelName, after.OriginalType));
                if (preview.CandidateIds.Contains(after.Id)) { Assert.NotNull(after.ReviewedAt); Assert.Equal(DateTimeKind.Utc, after.ReviewedAt.Value.Kind); }
            }
            var audits = await db.AuditEvents.Where(x => x.Action == "BulkApproved").ToListAsync();
            Assert.Equal(43, audits.Count);
            Assert.All(audits, x => Assert.Contains(result.BatchId.ToString(), x.AfterJson!));
            Assert.Single(await db.AuditEvents.Where(x => x.RecordType == "EntityMentionBatch").ToListAsync());
        }
        using var restarted = new TestApi(directory);
        using var restartedClient = restarted.CreateClient();
        Assert.Equal(44, (await restartedClient.GetFromJsonAsync<PageResult<MentionResponse>>(Route(inv) + "?status=APPROVED", Json))!.Total);
        Assert.Equal(created + 1, (await restartedClient.GetFromJsonAsync<PageResult<EntityResponse>>($"/api/v1/investigations/{inv}/entities", Json))!.Total);
    }

    [Fact]
    public async Task PreviewCombinesSourceTypeAndSearchWithoutTouchingData()
    {
        using var api = new TestApi(directory); using var client = api.CreateClient();
        var inv = await Investigation(client);
        var first = await Seed(api, client, inv, ("Alice Morgan", EntityType.PERSON), ("Aster Maritime", EntityType.ORGANIZATION));
        await Seed(api, client, inv, ("Alice Morgan", EntityType.PERSON));
        var preview = await Preview(client, inv, $"?sourceId={first.Source}&type=PERSON&search=mOrGaN");
        Assert.Equal(first.Mentions[0].Id, Assert.Single(preview.CandidateIds));
        Assert.Equal(0, (await Preview(client, inv, $"?sourceId={first.Source}&type=ORGANIZATION&search=Alice")).ProposedCount);
        Assert.Equal(3, (await client.GetFromJsonAsync<PageResult<MentionResponse>>(Route(inv) + "?status=PROPOSED", Json))!.Total);
    }

    [Fact]
    public async Task StaleBatchRejectsEverythingWithoutPartialApprovals()
    {
        using var api = new TestApi(directory); using var client = api.CreateClient();
        var inv = await Investigation(client);
        var seeded = await Seed(api, client, inv, ("Alice Morgan", EntityType.PERSON), ("Aster Maritime", EntityType.ORGANIZATION));
        var preview = await Preview(client, inv);
        (await client.PostAsync($"/api/v1/mentions/{seeded.Mentions[1].Id}/reject", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Approve(client, inv, preview.CandidateIds.ToArray())).StatusCode);
        using var scope = api.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
        Assert.Equal(0, await db.Entities.CountAsync());
        Assert.Equal(1, await db.EntityMentions.CountAsync(x => x.Status == ReviewStatus.PROPOSED));
        Assert.Equal(0, await db.AuditEvents.CountAsync(x => x.Action == "BulkApproved"));
    }

    [Theory]
    [InlineData("empty", HttpStatusCode.BadRequest)]
    [InlineData("duplicate", HttpStatusCode.BadRequest)]
    [InlineData("unconfirmed", HttpStatusCode.BadRequest)]
    [InlineData("foreign", HttpStatusCode.NotFound)]
    [InlineData("missing", HttpStatusCode.NotFound)]
    [InlineData("long", HttpStatusCode.BadRequest)]
    public async Task InvalidBatchesDoNotApproveAnyMention(string kind, HttpStatusCode expected)
    {
        using var api = new TestApi(directory); using var client = api.CreateClient();
        var inv = await Investigation(client);
        var seeded = await Seed(api, client, inv, ("Alice Morgan", EntityType.PERSON), (new string('x', 201), EntityType.OTHER));
        var ids = new[] { seeded.Mentions[0].Id };
        if (kind == "empty") ids = [];
        if (kind == "duplicate") ids = [ids[0], ids[0]];
        if (kind == "foreign") ids = [ids[0], (await Seed(api, client, await Investigation(client), ("Other person", EntityType.PERSON))).Mentions[0].Id];
        if (kind == "missing") ids = [ids[0], Guid.NewGuid()];
        if (kind == "long") ids = [ids[0], seeded.Mentions[1].Id];
        Assert.Equal(expected, (await Approve(client, inv, ids, confirm: kind != "unconfirmed")).StatusCode);
        using var scope = api.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
        Assert.Equal(0, await db.Entities.CountAsync());
        Assert.Equal(0, await db.EntityMentions.CountAsync(x => x.Status == ReviewStatus.APPROVED));
    }

    [Fact]
    public async Task OversizedScopeRequiresNarrowerFilters()
    {
        using var api = new TestApi(directory); using var client = api.CreateClient();
        var inv = await Investigation(client);
        await Seed(api, client, inv, Enumerable.Repeat(("Alice Morgan", EntityType.PERSON), 5001).ToArray());
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.GetAsync(Route(inv) + "/bulk-approval-preview")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Approve(client, inv, Enumerable.Repeat(Guid.NewGuid(), 5001).ToArray())).StatusCode);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
