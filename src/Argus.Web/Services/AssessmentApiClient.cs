using System.Net.Http.Json;
using Argus.Core.DTOs;

namespace Argus.Web.Services;

public sealed class AssessmentApiClient(HttpClient http)
{
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    { using (response) { await InvestigationClient.CheckAsync(response); return (await response.Content.ReadFromJsonAsync<T>(InvestigationClient.Json))!; } }
    public async Task<PageResult<ClaimSummary>> ClaimsAsync(Guid id, string status, string confidence, bool unresolved, string search, int page)
        => await ReadAsync<PageResult<ClaimSummary>>(await http.GetAsync($"api/v1/investigations/{id}/claims?status={status}&confidence={confidence}&unresolvedOnly={unresolved}&search={Uri.EscapeDataString(search)}&page={page}&pageSize=20"));
    public async Task<AssessmentSummary> SummaryAsync(Guid id) => await ReadAsync<AssessmentSummary>(await http.GetAsync($"api/v1/investigations/{id}/assessments/summary"));
    public async Task<ClaimDetail> ClaimAsync(Guid id) => await ReadAsync<ClaimDetail>(await http.GetAsync($"api/v1/claims/{id}"));
    public async Task<ClaimDetail> SaveAsync(Guid inv, Guid? id, ClaimEditRequest request) => await ReadAsync<ClaimDetail>(id.HasValue
        ? await http.PutAsJsonAsync($"api/v1/claims/{id}", request, InvestigationClient.Json)
        : await http.PostAsJsonAsync($"api/v1/investigations/{inv}/claims", request, InvestigationClient.Json));
    public async Task ActionAsync(Guid id, string action, object request)
    { using var response = await http.PostAsJsonAsync($"api/v1/claims/{id}/{action}", request, InvestigationClient.Json); await InvestigationClient.CheckAsync(response); }
    public async Task RemoveEvidenceAsync(Guid id, Guid evidenceId, int revision)
    { using var response = await http.DeleteAsync($"api/v1/claims/{id}/evidence/{evidenceId}?revision={revision}"); await InvestigationClient.CheckAsync(response); }
    public async Task<ReportValidation> ValidationAsync(Guid id) => await ReadAsync<ReportValidation>(await http.GetAsync($"api/v1/investigations/{id}/reports/validation"));
    public async Task<ReportResponse> GenerateAsync(Guid id, bool confirm) => await ReadAsync<ReportResponse>(await http.PostAsJsonAsync($"api/v1/investigations/{id}/reports", new ReportRequest(confirm), InvestigationClient.Json));
    public async Task<PageResult<ReportSummary>> ReportsAsync(Guid id, int page) => await ReadAsync<PageResult<ReportSummary>>(await http.GetAsync($"api/v1/investigations/{id}/reports?page={page}"));
    public async Task<ReportResponse> ReportAsync(Guid id) => await ReadAsync<ReportResponse>(await http.GetAsync($"api/v1/reports/{id}"));
    public async Task<(byte[] Content, string Name)> DownloadAsync(Guid id, CancellationToken ct)
    {
        using var response = await http.GetAsync($"api/v1/reports/{id}/markdown", ct); await InvestigationClient.CheckAsync(response);
        return (await response.Content.ReadAsByteArrayAsync(ct), $"argus-{id:N}.md");
    }
}
