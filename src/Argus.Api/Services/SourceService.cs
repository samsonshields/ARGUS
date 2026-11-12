using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Exceptions;
using Argus.Core.Interfaces;
using Argus.Infrastructure.Data;
using Argus.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Argus.Api.Services;

public sealed class SourceService(ArgusDbContext db, DocumentStorage storage, IDocumentExtractor extractor,
    IOptions<DocumentOptions> options, ILogger<SourceService> logger)
{
    public async Task<SourceStatistics> StatisticsAsync(CancellationToken ct)
    {
        var counts = await db.Sources.GroupBy(x => x.ProcessingStatus)
            .Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(ct);
        return new(counts.Sum(x => x.Count), counts.Where(x => x.Status == SourceProcessingStatus.Ready).Sum(x => x.Count),
            counts.Where(x => x.Status == SourceProcessingStatus.Failed).Sum(x => x.Count));
    }

    public async Task<List<SourceSummary>> ListAsync(Guid investigationId, string? search, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        await RequireInvestigationAsync(investigationId, ct);
        var query = db.Sources.AsNoTracking().Where(x => x.InvestigationId == investigationId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x => x.Title.ToLower().Contains(term) || x.ExtractedText.ToLower().Contains(term));
        }
        if (from.HasValue) query = query.Where(x => x.PublishedAt >= from);
        if (to.HasValue) query = query.Where(x => x.PublishedAt <= to);
        return await query.OrderByDescending(x => x.RetrievedAt).ThenBy(x => x.Id)
            .Select(x => new SourceSummary(x.Id, x.InvestigationId, x.Title, x.SourceType, x.Publisher,
                x.PublishedAt, x.RetrievedAt, x.Reliability, x.ProcessingStatus, x.ProcessingError, x.OriginalFileName, x.FileSize)).ToListAsync(ct);
    }

    public async Task<SourceResponse> GetAsync(Guid id, CancellationToken ct) => (await FindAsync(id, ct)).ToResponse();

    public async Task<SourceResponse> ImportAsync(Guid investigationId, SourceMetadataRequest metadata,
        string fileName, Stream input, bool pasted, CancellationToken ct)
    {
        await RequireInvestigationAsync(investigationId, ct);
        var pdf = DocumentStorage.IsPdf(fileName);
        var limit = options.Value.MaxFileBytes;
        using var content = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) != 0)
        {
            if (content.Length + read > limit) throw new SourceException(413, "Document exceeds the configured file size limit.");
            await content.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (content.Length == 0) throw new SourceException(400, "The document is empty.");
        var bytes = content.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var existing = await db.Sources.AsNoTracking().FirstOrDefaultAsync(x => x.InvestigationId == investigationId && x.ContentHash == hash, ct);
        if (existing is not null) throw Duplicate(existing.Id);
        var source = Source.Create(investigationId, pasted ? SourceType.PastedText : pdf ? SourceType.Pdf : SourceType.Text,
            metadata, DocumentStorage.SafeName(fileName), bytes.Length, hash);
        await storage.SaveAsync(source.FilePath, bytes, ct);
        try
        {
            await ExtractAsync(source, ct);
            db.Sources.Add(source);
            Audit(source, "Imported", null);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 })
        {
            storage.Remove(source.FilePath);
            db.ChangeTracker.Clear();
            var duplicate = await db.Sources.FirstOrDefaultAsync(x => x.InvestigationId == investigationId && x.ContentHash == hash, ct);
            if (duplicate is not null) throw Duplicate(duplicate.Id);
            throw new SourceException(409, "Investigation changed while importing. Please reload and retry.");
        }
        catch
        {
            storage.Remove(source.FilePath);
            throw;
        }
        logger.LogInformation("Imported source {SourceId} with extraction status {Status}", source.Id, source.ProcessingStatus);
        return source.ToResponse();
    }

    public async Task<SourceResponse> UpdateAsync(Guid id, SourceMetadataRequest request, CancellationToken ct)
    {
        var source = await FindAsync(id, ct);
        var before = Snapshot(source);
        source.UpdateMetadata(request);
        Audit(source, "MetadataUpdated", before);
        await db.SaveChangesAsync(ct);
        return source.ToResponse();
    }

    public async Task<SourceResponse> ReextractAsync(Guid id, CancellationToken ct)
    {
        var source = await FindAsync(id, ct);
        var before = Snapshot(source);
        await ExtractAsync(source, ct);
        Audit(source, "TextExtractionRetried", before);
        await db.SaveChangesAsync(ct);
        return source.ToResponse();
    }

    public async Task<(Stream Content, string Name)> OpenOriginalAsync(Guid id, CancellationToken ct)
    {
        var source = await FindAsync(id, ct);
        var path = storage.Resolve(source.FilePath);
        if (!File.Exists(path)) throw new SourceException(404, "Original file is missing from local storage. Restore it from backup.");
        return (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true), source.OriginalFileName);
    }

    private async Task ExtractAsync(Source source, CancellationToken ct)
    {
        try
        {
            var text = await extractor.ExtractAsync(storage.Resolve(source.FilePath), source.SourceType == SourceType.Pdf, ct);
            source.CompleteExtraction(text, TextExtraction.Version);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            source.FailExtraction("Text extraction timed out. The original is preserved; retry extraction if appropriate.");
        }
        catch (Exception ex) when (ex is IOException or DecoderFallbackException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            source.FailExtraction(source.SourceType == SourceType.Pdf
                ? "Unable to extract PDF text. The file may be malformed, encrypted, scanned, or exceed extraction limits. OCR is not supported."
                : "Unable to extract text. Use nonempty UTF-8 or BOM-marked UTF-16/32 text within the configured limits.");
            logger.LogWarning("Text extraction failed for source {SourceId}; category {FailureType}", source.Id, ex.GetType().Name);
        }
    }

    private async Task RequireInvestigationAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Investigations.AnyAsync(x => x.Id == id, ct)) throw new SourceException(404, "Investigation not found.");
    }
    private async Task<Source> FindAsync(Guid id, CancellationToken ct)
        => await db.Sources.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new SourceException(404, "Source not found.");
    private static SourceException Duplicate(Guid id) => new(409, "An exact copy already exists in this investigation.", id);
    private static string Snapshot(Source source) => JsonSerializer.Serialize(new
    {
        Metadata = source.ToSummary(),
        source.OriginalUrl,
        source.ContentHash,
        source.ExtractedAt,
        source.ExtractionVersion
    });
    private void Audit(Source source, string action, string? before) => db.AuditEvents.Add(new AuditEvent
    {
        InvestigationId = source.InvestigationId,
        RecordId = source.Id,
        Action = action,
        BeforeJson = before,
        AfterJson = Snapshot(source)
    });
}
