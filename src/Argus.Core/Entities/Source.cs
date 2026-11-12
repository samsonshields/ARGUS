using System.ComponentModel.DataAnnotations;
using Argus.Core.DTOs;
using Argus.Core.Enums;

namespace Argus.Core.Entities;

public sealed class Source
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid InvestigationId { get; private set; }
    public string Title { get; private set; } = "";
    public SourceType SourceType { get; private set; }
    public string? OriginalUrl { get; private set; }
    public string? Publisher { get; private set; }
    public DateOnly? PublishedAt { get; private set; }
    public DateTime RetrievedAt { get; private set; } = DateTime.UtcNow;
    public SourceReliability Reliability { get; private set; } = SourceReliability.E;
    public string FilePath { get; private set; } = "";
    public string OriginalFileName { get; private set; } = "";
    public long FileSize { get; private set; }
    public string ContentHash { get; private set; } = "";
    public string ExtractedText { get; private set; } = "";
    public SourceProcessingStatus ProcessingStatus { get; private set; }
    public string? ProcessingError { get; private set; }
    public DateTime? ExtractedAt { get; private set; }
    public string ExtractionVersion { get; private set; } = "";

    private Source() { }
    public static Source Create(Guid investigationId, SourceType type, SourceMetadataRequest metadata,
        string fileName, long fileSize, string hash)
    {
        var source = new Source
        {
            InvestigationId = investigationId,
            SourceType = type,
            OriginalFileName = fileName,
            FileSize = fileSize,
            ContentHash = hash
        };
        source.UpdateMetadata(metadata);
        source.FilePath = source.Id.ToString("N") + (type == SourceType.Pdf ? ".pdf" : ".txt");
        return source;
    }

    public void UpdateMetadata(SourceMetadataRequest request)
    {
        Validator.ValidateObject(request, new(request), true);
        Title = request.Title.Trim();
        Publisher = string.IsNullOrWhiteSpace(request.Publisher) ? null : request.Publisher.Trim();
        OriginalUrl = string.IsNullOrWhiteSpace(request.OriginalUrl) ? null : request.OriginalUrl.Trim();
        PublishedAt = request.PublishedAt;
        Reliability = request.Reliability;
    }

    public void CompleteExtraction(string text, string version)
    {
        ExtractedText = text;
        ExtractionVersion = version;
        ExtractedAt = DateTime.UtcNow;
        ProcessingStatus = SourceProcessingStatus.Ready;
        ProcessingError = null;
    }

    // A failed retry must not destroy previously extracted content or the original.
    public void FailExtraction(string error)
    {
        ProcessingStatus = SourceProcessingStatus.Failed;
        ProcessingError = error;
    }

    public void QueueProcessing() { ProcessingStatus = SourceProcessingStatus.Pending; }
    public void StartProcessing() { ProcessingStatus = SourceProcessingStatus.Processing; }
    public void CancelProcessing()
    {
        ProcessingStatus = ExtractedAt.HasValue ? SourceProcessingStatus.Ready : SourceProcessingStatus.Failed;
        if (!ExtractedAt.HasValue) ProcessingError = "Processing was cancelled; the original is preserved.";
    }

    public SourceSummary ToSummary() => new(Id, InvestigationId, Title, SourceType, Publisher,
        PublishedAt, RetrievedAt, Reliability, ProcessingStatus, ProcessingError, OriginalFileName, FileSize);
    public SourceResponse ToResponse() => new(ToSummary(), OriginalUrl, ContentHash, ExtractedText, ExtractedAt, ExtractionVersion);
}
