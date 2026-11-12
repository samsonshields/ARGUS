using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Argus.Core.DTOs;

namespace Argus.Web.Services;

public sealed class SourceHttpException(string message, HttpStatusCode status, Guid? existingSourceId = null)
    : HttpRequestException(message, null, status)
{
    public Guid? ExistingSourceId { get; } = existingSourceId;
}

public sealed class SourceClient(HttpClient http)
{
    private async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            await InvestigationClient.CheckAsync(response);
            return (await response.Content.ReadFromJsonAsync<T>(InvestigationClient.Json))!;
        }
    }
    public async Task<ImportSettings> SettingsAsync() => await ReadAsync<ImportSettings>(await http.GetAsync("api/v1/import-settings"));
    public async Task<SourceStatistics> StatisticsAsync() => await ReadAsync<SourceStatistics>(await http.GetAsync("api/v1/source-statistics"));
    public async Task<List<SourceSummary>> ListAsync(Guid investigationId, string? search = null, DateOnly? from = null, DateOnly? to = null)
        => await ReadAsync<List<SourceSummary>>(await http.GetAsync($"api/v1/investigations/{investigationId}/sources?search={Uri.EscapeDataString(search ?? "")}&from={from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"));
    public async Task<SourceResponse> GetAsync(Guid id) => await ReadAsync<SourceResponse>(await http.GetAsync($"api/v1/sources/{id}"));
    public async Task<SourceResponse> PasteAsync(Guid investigationId, PastedSourceRequest request)
        => await ReadAsync<SourceResponse>(await http.PostAsJsonAsync($"api/v1/investigations/{investigationId}/sources/text", request, InvestigationClient.Json));
    public async Task<SourceResponse> UpdateAsync(Guid id, SourceMetadataRequest request)
        => await ReadAsync<SourceResponse>(await http.PutAsJsonAsync($"api/v1/sources/{id}", request, InvestigationClient.Json));
    public async Task<SourceResponse> ExtractAsync(Guid id)
        => await ReadAsync<SourceResponse>(await http.PostAsync($"api/v1/sources/{id}/extract-text", null));
    public async Task<SourceResponse> UploadAsync(Guid investigationId, SourceMetadataRequest metadata, string fileName, Stream stream)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(metadata.Title), "Title");
        form.Add(new StringContent(metadata.Publisher ?? ""), "Publisher");
        form.Add(new StringContent(metadata.OriginalUrl ?? ""), "OriginalUrl");
        form.Add(new StringContent(metadata.PublishedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ""), "PublishedAt");
        form.Add(new StringContent(metadata.Reliability.ToString()), "Reliability");
        form.Add(new StreamContent(stream), "File", fileName);
        return await ReadAsync<SourceResponse>(await http.PostAsync($"api/v1/investigations/{investigationId}/sources", form));
    }
    public async Task<(byte[] Content, string Name)> DownloadAsync(Guid id, CancellationToken ct)
    {
        using var response = await http.GetAsync($"api/v1/sources/{id}/original", ct);
        await InvestigationClient.CheckAsync(response);
        return (await response.Content.ReadAsByteArrayAsync(ct), response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "document.txt");
    }
}
