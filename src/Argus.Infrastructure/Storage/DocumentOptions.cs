namespace Argus.Infrastructure.Storage;

public sealed class DocumentOptions
{
    public long MaxFileBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxExtractedCharacters { get; set; } = 2_000_000;
    public int MaxPdfPages { get; set; } = 500;
    public int ExtractionTimeoutSeconds { get; set; } = 20;
}

// Internal storage settings are supplied by the API; never supplied by an upload.
public sealed record DocumentStorageRoot(string Path);
public sealed record ApplicationDataRoot(string Path);
