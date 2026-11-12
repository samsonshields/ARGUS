using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Argus.Core.DTOs;
using Microsoft.Extensions.Options;
namespace Argus.Web.Services;

public sealed class ApiOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5100";
}
public sealed class InvestigationClient(HttpClient http)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    public async Task<List<InvestigationResponse>> ListAsync(string? search = null, string? status = null)
        => await http.GetFromJsonAsync<List<InvestigationResponse>>(
            $"api/v1/investigations?search={Uri.EscapeDataString(search ?? "")}&status={Uri.EscapeDataString(status ?? "")}", Json) ?? [];
    public async Task<InvestigationResponse?> GetAsync(Guid id)
    {
        using var response = await http.GetAsync($"api/v1/investigations/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await CheckAsync(response);
        return await response.Content.ReadFromJsonAsync<InvestigationResponse>(Json);
    }
    public async Task<InvestigationResponse> SaveAsync(Guid? id, InvestigationRequest request)
    {
        using var response = id.HasValue
            ? await http.PutAsJsonAsync($"api/v1/investigations/{id}", request, Json)
            : await http.PostAsJsonAsync("api/v1/investigations", request, Json);
        await CheckAsync(response);
        return (await response.Content.ReadFromJsonAsync<InvestigationResponse>(Json))!;
    }
    public async Task DeleteAsync(Guid id)
    {
        using var response = await http.DeleteAsync($"api/v1/investigations/{id}?confirm=true");
        await CheckAsync(response);
    }
    internal static async Task CheckAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.Content.Headers.ContentType?.MediaType == "application/problem+json")
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (problem.TryGetProperty("errors", out var errors))
                throw new SourceHttpException(string.Join(" ", errors.EnumerateObject()
                    .SelectMany(x => x.Value.EnumerateArray().Select(v => v.GetString()))), response.StatusCode);
            Guid? existing = problem.TryGetProperty("existingSourceId", out var duplicate) && duplicate.TryGetGuid(out var id) ? id : null;
            if (problem.TryGetProperty("title", out var title)) throw new SourceHttpException(title.GetString() ?? "Request failed.", response.StatusCode, existing);
        }
        throw new HttpRequestException($"API request failed ({(int)response.StatusCode}). Please try again.");
    }
}
