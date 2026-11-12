using System.Net.Http.Json;
using Argus.Core.DTOs;

namespace Argus.Web.Services;

public sealed class GraphApiClient(HttpClient http)
{
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response) { await InvestigationClient.CheckAsync(response); return (await response.Content.ReadFromJsonAsync<T>(InvestigationClient.Json))!; }
    }
    private static string Filters(string entityType, string relationshipType, bool proposed, string search) =>
        $"entityType={entityType}&relationshipType={relationshipType}&includeProposed={proposed}&search={Uri.EscapeDataString(search)}";
    public async Task<GraphResponse> GraphAsync(Guid id, string entityType, string relationshipType, bool proposed, string search)
        => await ReadAsync<GraphResponse>(await http.GetAsync($"api/v1/investigations/{id}/graph?{Filters(entityType, relationshipType, proposed, search)}"));
    public async Task<GraphPath> PathAsync(Guid id, Guid source, Guid target, string entityType, string relationshipType, bool proposed, bool direction, string search)
        => await ReadAsync<GraphPath>(await http.GetAsync($"api/v1/investigations/{id}/graph/path?sourceId={source}&targetId={target}&respectDirection={direction}&{Filters(entityType, relationshipType, proposed, search)}"));
    public async Task<PageResult<RelationshipSummary>> ListAsync(Guid id, string status, string type, int page)
        => await ReadAsync<PageResult<RelationshipSummary>>(await http.GetAsync($"api/v1/investigations/{id}/relationships?status={status}&type={type}&page={page}&pageSize=20"));
    public async Task<RelationshipDetail> DetailAsync(Guid id, int evidencePage = 1)
        => await ReadAsync<RelationshipDetail>(await http.GetAsync($"api/v1/relationships/{id}?evidencePage={evidencePage}"));
    public async Task<RelationshipDetail> ApproveAsync(Guid id, RelationshipReviewRequest request)
        => await ReadAsync<RelationshipDetail>(await http.PostAsJsonAsync($"api/v1/relationships/{id}/approve", request, InvestigationClient.Json));
    public async Task RejectAsync(Guid id)
    { using var response = await http.PostAsync($"api/v1/relationships/{id}/reject", null); await InvestigationClient.CheckAsync(response); }
    public async Task<GenerationQueueResult> GenerateAsync(Guid investigationId)
        => await ReadAsync<GenerationQueueResult>(await http.PostAsync($"api/v1/investigations/{investigationId}/relationships/generate", null));
}
