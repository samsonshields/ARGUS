using System.Net.Http.Json;
using Argus.Core.DTOs;

namespace Argus.Web.Services;

public sealed class TemporalApiClient(HttpClient http)
{
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    { using (response) { await InvestigationClient.CheckAsync(response); return (await response.Content.ReadFromJsonAsync<T>(InvestigationClient.Json))!; } }
    public static string Filters(string type, string from, string to, bool unknown) => $"type={Uri.EscapeDataString(type)}&from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&includeUnknown={unknown}";
    public async Task<PageResult<EventResponse>> EventsAsync(Guid id, string status, string type, string from, string to, bool unknown, int page)
        => await ReadAsync<PageResult<EventResponse>>(await http.GetAsync($"api/v1/investigations/{id}/events?status={status}&allStatuses={status.Length == 0}&{Filters(type, from, to, unknown)}&page={page}&pageSize=20"));
    public async Task<EventResponse> EventAsync(Guid id) => await ReadAsync<EventResponse>(await http.GetAsync($"api/v1/events/{id}"));
    public async Task<EventResponse> SaveEventAsync(Guid inv, Guid? id, EventEditRequest request) => await ReadAsync<EventResponse>(id.HasValue
        ? await http.PutAsJsonAsync($"api/v1/events/{id}", request, InvestigationClient.Json)
        : await http.PostAsJsonAsync($"api/v1/investigations/{inv}/events", request, InvestigationClient.Json));
    public async Task<List<LocationResponse>> LocationsAsync(Guid id) => await ReadAsync<List<LocationResponse>>(await http.GetAsync($"api/v1/investigations/{id}/locations"));
    public async Task<LocationResponse> LocationAsync(Guid id) => await ReadAsync<LocationResponse>(await http.GetAsync($"api/v1/locations/{id}"));
    public async Task<LocationResponse> SaveLocationAsync(Guid inv, Guid? id, LocationEditRequest request) => await ReadAsync<LocationResponse>(id.HasValue
        ? await http.PutAsJsonAsync($"api/v1/locations/{id}", request, InvestigationClient.Json)
        : await http.PostAsJsonAsync($"api/v1/investigations/{inv}/locations", request, InvestigationClient.Json));
    public async Task ActionAsync(Guid id, bool location, string action, object request)
    { using var response = await http.PostAsJsonAsync($"api/v1/{(location ? "locations" : "events")}/{id}/{action}", request, InvestigationClient.Json); await InvestigationClient.CheckAsync(response); }
    public async Task RemoveCitationAsync(Guid id, bool location, Guid evidence, int revision)
    { using var response = await http.DeleteAsync($"api/v1/{(location ? "locations" : "events")}/{id}/evidence/{evidence}?revision={revision}"); await InvestigationClient.CheckAsync(response); }
    public async Task<PageResult<PassageResponse>> PassagesAsync(Guid source, int page) => await ReadAsync<PageResult<PassageResponse>>(await http.GetAsync($"api/v1/sources/{source}/passages?page={page}"));
    public async Task<JobResponse> GenerateAsync(Guid source) => await ReadAsync<JobResponse>(await http.PostAsync($"api/v1/sources/{source}/process-events", null));
    public async Task<MapResponse> MapAsync(Guid inv, string type, string from, string to, bool unknown) => await ReadAsync<MapResponse>(await http.GetAsync($"api/v1/investigations/{inv}/map?{Filters(type, from, to, unknown)}"));
    public async Task FocusAsync(Guid inv, MapFocusRequest request)
    { using var response = await http.PutAsJsonAsync($"api/v1/investigations/{inv}/map/focus", request, InvestigationClient.Json); await InvestigationClient.CheckAsync(response); }
}
