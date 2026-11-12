namespace Argus.Core.Interfaces;

public interface IDocumentExtractor
{
    Task<string> ExtractAsync(string path, bool pdf, CancellationToken ct);
}
