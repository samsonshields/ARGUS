using Argus.Core.DTOs;
using Argus.Core.Enums;
using Argus.Core.Exceptions;

namespace Argus.Core.Services;

public static class RelationshipContract
{
    public static void Validate(RelationshipProcessingRequest request, RelationshipProcessingResponse response)
    {
        if (response.ContractVersion != "1" || response.SourceId != request.SourceId || response.TextHash != request.TextHash
            || response.OffsetConvention != "UTF16_CODE_UNITS" || response.Method != "sentence_cooccurrence_v1"
            || response.Relationships is null || response.Relationships.Count > 5000) throw Invalid();
        var mentions = request.Mentions.ToDictionary(x => x.Id);
        foreach (var candidate in response.Relationships)
        {
            if (candidate is null || candidate.Type != RelationshipType.MENTIONED_WITH || candidate.Confidence is not null
                || candidate.SourceEntityId == candidate.TargetEntityId || candidate.Start < 0 || candidate.End <= candidate.Start
                || candidate.End > request.Text.Length || candidate.End - candidate.Start > 8000
                || !EntityText.Boundary(request.Text, candidate.Start) || !EntityText.Boundary(request.Text, candidate.End)
                || request.Text[candidate.Start..candidate.End] != candidate.Quote
                || !mentions.TryGetValue(candidate.SourceMentionId, out var source) || !mentions.TryGetValue(candidate.TargetMentionId, out var target)
                || source.EntityId != candidate.SourceEntityId || target.EntityId != candidate.TargetEntityId
                || source.Start < candidate.Start || source.End > candidate.End || target.Start < candidate.Start || target.End > candidate.End)
                throw Invalid();
        }
    }
    private static ProcessingException Invalid() => new("invalid_relationship_contract", "Co-mention response failed entity/evidence validation; nothing was stored.");
}
