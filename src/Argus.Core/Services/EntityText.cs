using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Exceptions;

namespace Argus.Core.Services;

public static class EntityText
{
    public static void Validate(NlpRequest request, NlpResponse response, int maxCandidates)
    {
        if (response.ContractVersion != "1" || response.SourceId != request.SourceId || response.TextHash != request.TextHash
            || response.OffsetConvention != "UTF16_CODE_UNITS" || response.Entities is null || response.Entities.Count > maxCandidates
            || response.Model is null || string.IsNullOrWhiteSpace(response.Model.Name) || response.Model.Name.Length > 200
            || string.IsNullOrWhiteSpace(response.Model.Version) || response.Model.Version.Length > 80
            || string.IsNullOrWhiteSpace(response.Model.SpacyVersion) || response.Model.SpacyVersion.Length > 80)
            throw Invalid();
        foreach (var candidate in response.Entities)
            if (candidate is null || candidate.Start < 0 || candidate.End <= candidate.Start || candidate.End > request.Text.Length
                || !Boundary(request.Text, candidate.Start) || !Boundary(request.Text, candidate.End)
                || string.IsNullOrWhiteSpace(candidate.Text) || candidate.Text.Length > 2000
                || request.Text[candidate.Start..candidate.End] != candidate.Text || !Enum.IsDefined(candidate.Type)
                || candidate.Confidence is not null)
                throw Invalid();
    }
    private static ProcessingException Invalid() => new("invalid_contract", "NLP response failed source/span validation; no candidates were stored.");
    public static bool Boundary(string text, int offset) => offset == 0 || offset == text.Length || !char.IsLowSurrogate(text[offset]);

    public static double Similarity(string first, string second)
    {
        first = IntelligenceEntity.Normalize(first); second = IntelligenceEntity.Normalize(second);
        if (first == second) return 1;
        if (first.Length == 0 || second.Length == 0) return 0;
        if ((double)Math.Min(first.Length, second.Length) / Math.Max(first.Length, second.Length) < 0.5) return 0;
        var previous = Enumerable.Range(0, second.Length + 1).ToArray();
        for (var i = 1; i <= first.Length; i++)
        {
            var current = new int[second.Length + 1]; current[0] = i;
            for (var j = 1; j <= second.Length; j++) current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1),
                previous[j - 1] + (first[i - 1] == second[j - 1] ? 0 : 1));
            previous = current;
        }
        return 1.0 - (double)previous[second.Length] / Math.Max(first.Length, second.Length);
    }
}
