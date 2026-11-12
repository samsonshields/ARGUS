namespace Argus.Web.Services;

public sealed class BackupApiClient(HttpClient http)
{
    public async Task<HttpResponseMessage> ExportAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/backups/export");
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        try { await InvestigationClient.CheckAsync(response); return response; }
        catch { response.Dispose(); throw; }
    }
}
