using System.ComponentModel.DataAnnotations;
using Argus.Core.Enums;

namespace Argus.Core.DTOs;

public class SourceMetadataRequest : IValidatableObject
{
    [Required, StringLength(200)] public string Title { get; set; } = "";
    [StringLength(200)] public string? Publisher { get; set; }
    [StringLength(2048)] public string? OriginalUrl { get; set; }
    public DateOnly? PublishedAt { get; set; }
    [EnumDataType(typeof(SourceReliability))] public SourceReliability Reliability { get; set; } = SourceReliability.E;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(OriginalUrl) &&
            (!Uri.TryCreate(OriginalUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
            yield return new("Original URL must be an absolute HTTP(S) URL. It is stored as attribution only, never fetched.", [nameof(OriginalUrl)]);
    }
}

public sealed class PastedSourceRequest : SourceMetadataRequest
{
    [Required, StringLength(2_000_000)] public string Text { get; set; } = "";
}

public sealed record SourceSummary(Guid Id, Guid InvestigationId, string Title, SourceType SourceType,
    string? Publisher, DateOnly? PublishedAt, DateTime RetrievedAt, SourceReliability Reliability,
    SourceProcessingStatus ProcessingStatus, string? ProcessingError, string OriginalFileName, long FileSize);

public sealed record SourceResponse(SourceSummary Source, string? OriginalUrl, string ContentHash,
    string ExtractedText, DateTime? ExtractedAt, string ExtractionVersion);

public sealed record ImportSettings(long MaxFileBytes);
public sealed record SourceStatistics(int Total, int Ready, int Failed);
