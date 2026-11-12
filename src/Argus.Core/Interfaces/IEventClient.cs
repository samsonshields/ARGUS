using Argus.Core.DTOs;

namespace Argus.Core.Interfaces;

public interface IEventClient
{
    Task<EventProcessingResponse> ProcessEventsAsync(NlpRequest request, CancellationToken ct);
}
