using Argus.Core.DTOs;

namespace Argus.Core.Interfaces;

public interface IIntelligenceClient
{
    Task<NlpResponse> ProcessAsync(NlpRequest request, CancellationToken ct);
    Task<bool> IsReadyAsync(CancellationToken ct);
}
