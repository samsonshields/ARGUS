using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Argus.Core.DTOs;
using Argus.Core.Enums;
namespace Argus.IntegrationTests;

public sealed class WorkflowApiTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "argus-workflow-tests-" + Guid.NewGuid());
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> Read<T>(HttpResponseMessage response)
    { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }

    [Fact]
    public async Task SummaryFollowsInvestigationStateAndIsScopedToIt()
    {
        using var api = new TestApi(directory);
        using var client = api.CreateClient();
        var inv = await Read<InvestigationResponse>(await client.PostAsJsonAsync("/api/v1/investigations", new { title = "Fictional workflow case" }));
        var other = await Read<InvestigationResponse>(await client.PostAsJsonAsync("/api/v1/investigations", new { title = "Fictional unrelated case" }));

        var empty = await client.GetFromJsonAsync<WorkflowSummary>($"/api/v1/investigations/{inv.Id}/workflow", Json);
        Assert.Equal(WorkflowStep.IMPORT_SOURCES, empty!.NextStep);
        Assert.Equal(0, empty.Counts.Sources);

        // Automatic processing is disabled in tests, so an imported source has no mentions yet.
        await Read<SourceResponse>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv.Id}/sources/text",
            new PastedSourceRequest { Title = "Fictional note", Text = "A fictional vessel arrived at a fictional port.", Reliability = SourceReliability.C }, Json));
        await Read<ClaimDetail>(await client.PostAsJsonAsync($"/api/v1/investigations/{inv.Id}/claims",
            new ClaimEditRequest { Statement = "The fictional vessel arrived.", Reasoning = "Analyst interpretation of one note.", ConfidenceLevel = ConfidenceLevel.LOW }, Json));

        var imported = await client.GetFromJsonAsync<WorkflowSummary>($"/api/v1/investigations/{inv.Id}/workflow", Json);
        Assert.Equal(WorkflowStep.AWAIT_EXTRACTION, imported!.NextStep);
        Assert.Equal(1, imported.Counts.Sources);
        Assert.Equal(1, imported.Counts.Claims.Proposed);

        var unrelated = await client.GetFromJsonAsync<WorkflowSummary>($"/api/v1/investigations/{other.Id}/workflow", Json);
        Assert.Equal(0, unrelated!.Counts.Sources);
        Assert.Equal(0, unrelated.Counts.Claims.Proposed);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/investigations/{Guid.NewGuid()}/workflow")).StatusCode);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
