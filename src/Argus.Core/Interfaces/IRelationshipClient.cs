using Argus.Core.DTOs;

namespace Argus.Core.Interfaces;

public interface IRelationshipClient
{
    Task<RelationshipProcessingResponse> ProcessAsync(RelationshipProcessingRequest request, CancellationToken ct);
}
