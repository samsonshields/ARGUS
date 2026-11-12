using System.Net.Http.Json;
using Argus.Core.DTOs;
using Argus.Core.Enums;

namespace Argus.Web.Services;

public sealed class IntelligenceApiClient(HttpClient http)
{
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct = default)
    {
        using (response)
        {
            await InvestigationClient.CheckAsync(response);
            return (await response.Content.ReadFromJsonAsync<T>(InvestigationClient.Json, ct))!;
        }
    }
    public async Task<bool> ReadyAsync()
    {
        using var response = await http.GetAsync("api/v1/intelligence/health");
        return response.IsSuccessStatusCode;
    }
    public async Task<IntelligenceStatistics> StatisticsAsync() => await ReadAsync<IntelligenceStatistics>(await http.GetAsync("api/v1/intelligence/statistics"));
    public async Task<PageResult<JobResponse>> JobsAsync(Guid? sourceId, Guid? investigationId, int page = 1, CancellationToken ct = default)
        => await ReadAsync<PageResult<JobResponse>>(await http.GetAsync($"api/v1/processing-jobs?sourceId={sourceId}&investigationId={investigationId}&page={page}&pageSize=20", ct), ct);
    public async Task<JobResponse> QueueAsync(Guid id) => await ReadAsync<JobResponse>(await http.PostAsync($"api/v1/sources/{id}/process", null));
    public async Task<JobResponse> CancelAsync(Guid id) => await ReadAsync<JobResponse>(await http.PostAsync($"api/v1/processing-jobs/{id}/cancel", null));
    public async Task<JobResponse> RetryAsync(Guid id) => await ReadAsync<JobResponse>(await http.PostAsync($"api/v1/processing-jobs/{id}/retry", null));
    public async Task<PageResult<MentionResponse>> MentionsAsync(Guid investigationId, Guid? sourceId, Guid? entityId, string? status, int page = 1,
        string? type = null, string? search = null)
        => await ReadAsync<PageResult<MentionResponse>>(await http.GetAsync($"api/v1/investigations/{investigationId}/mentions?sourceId={sourceId}&entityId={entityId}&status={status}&page={page}&pageSize=20&type={type}&search={Uri.EscapeDataString(search ?? "")}"));
    public async Task<MentionEvidence> EvidenceAsync(Guid id) => await ReadAsync<MentionEvidence>(await http.GetAsync($"api/v1/mentions/{id}/evidence"));
    public async Task<List<EntitySuggestion>> SuggestionsAsync(Guid id) => await ReadAsync<List<EntitySuggestion>>(await http.GetAsync($"api/v1/mentions/{id}/suggestions"));
    public async Task<EntityResponse> ApproveAsync(Guid id, ApproveMentionRequest request)
        => await ReadAsync<EntityResponse>(await http.PostAsJsonAsync($"api/v1/mentions/{id}/approve", request, InvestigationClient.Json));
    public async Task<BulkMentionPreview> BulkPreviewAsync(Guid investigationId, Guid? sourceId, string type, string search)
        => await ReadAsync<BulkMentionPreview>(await http.GetAsync($"api/v1/investigations/{investigationId}/mentions/bulk-approval-preview?sourceId={sourceId}&type={type}&search={Uri.EscapeDataString(search)}"));
    public async Task<BulkMentionApprovalResult> BulkApproveAsync(Guid investigationId, BulkApproveMentionsRequest request)
        => await ReadAsync<BulkMentionApprovalResult>(await http.PostAsJsonAsync($"api/v1/investigations/{investigationId}/mentions/approve-bulk", request, InvestigationClient.Json));
    public async Task RejectAsync(Guid id)
    {
        using var response = await http.PostAsync($"api/v1/mentions/{id}/reject", null);
        await InvestigationClient.CheckAsync(response);
    }
    public async Task<PageResult<EntityResponse>> EntitiesAsync(Guid investigationId, string? search = null, int page = 1)
        => await ReadAsync<PageResult<EntityResponse>>(await http.GetAsync($"api/v1/investigations/{investigationId}/entities?search={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize=20"));
    public async Task<EntityResponse> EntityAsync(Guid id) => await ReadAsync<EntityResponse>(await http.GetAsync($"api/v1/entities/{id}"));
    public async Task<EntityResponse> UpdateAsync(Guid id, EntityEditRequest request)
        => await ReadAsync<EntityResponse>(await http.PutAsJsonAsync($"api/v1/entities/{id}", request, InvestigationClient.Json));
    public async Task<EntityResponse> MergeAsync(MergeEntitiesRequest request)
        => await ReadAsync<EntityResponse>(await http.PostAsJsonAsync("api/v1/entities/merge", request, InvestigationClient.Json));
}
