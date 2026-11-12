using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Argus.Core.DTOs;
using Argus.Core.Exceptions;
using Argus.Core.Interfaces;
using Argus.Core.Services;
using Microsoft.Extensions.Options;

namespace Argus.Infrastructure.ExternalServices;

public sealed class IntelligenceClient(HttpClient http, IOptions<IntelligenceOptions> options) : IIntelligenceClient, IRelationshipClient, IEventClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        try { using var response = await http.GetAsync("health", ct); return response.IsSuccessStatusCode; }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    public async Task<NlpResponse> ProcessAsync(NlpRequest request, CancellationToken ct)
    {
        CheckText(request.Text);
        var response = await SendAsync<NlpResponse>("process/entities", request, ct);
        EntityText.Validate(request, response, options.Value.MaxCandidates);
        return response;
    }
    public async Task<RelationshipProcessingResponse> ProcessAsync(RelationshipProcessingRequest request, CancellationToken ct)
    {
        CheckText(request.Text);
        var response = await SendAsync<RelationshipProcessingResponse>("process/relationships", request, ct);
        RelationshipContract.Validate(request, response);
        return response;
    }
    private void CheckText(string text)
    {
        if (text.Length > options.Value.MaxTextCharacters)
            throw new ProcessingException("text_limit", "Source text exceeds the configured local pipeline limit; no truncation was performed.");
    }
    public async Task<EventProcessingResponse> ProcessEventsAsync(NlpRequest request, CancellationToken ct)
    {
        CheckText(request.Text);
        var response = await SendAsync<EventProcessingResponse>("process/events", request, ct);
        TemporalRules.Validate(request, response);
        return response;
    }
    private async Task<T> SendAsync<T>(string path, object request, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(request, options: Json) };
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
                throw new ProcessingException(response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ? "nlp_not_ready" : "nlp_error",
                    "Local NLP service rejected processing. Check its readiness, model and configured limits.", (int)response.StatusCode >= 500);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920]; int read;
            while ((read = await stream.ReadAsync(chunk, deadline.Token)) > 0)
            {
                if (buffer.Length + read > 4 * 1024 * 1024) throw new ProcessingException("response_limit", "NLP response exceeded its size limit.");
                buffer.Write(chunk, 0, read);
            }
            var result = JsonSerializer.Deserialize<T>(buffer.ToArray(), Json)
                ?? throw new ProcessingException("invalid_contract", "NLP service returned an empty response.");
            return result;
        }
        catch (JsonException) { throw new ProcessingException("invalid_contract", "NLP service returned an invalid JSON contract."); }
        catch (HttpRequestException) { throw new ProcessingException("nlp_unavailable", "Cannot reach the local NLP service. Start it, then retry.", true); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ProcessingException("nlp_timeout", "Local NLP processing exceeded its timeout.", true); }
    }
}
