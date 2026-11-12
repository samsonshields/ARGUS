using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Exceptions;
using Argus.Core.Services;
using Argus.Infrastructure.Data;
using Argus.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Argus.Api.Services;

public sealed class ReportService(ArgusDbContext db, ClaimService claims, TemporalService temporal, DocumentStorage storage)
{
    private const int FindingLimit = 500, PassageLimit = 2000, SourceLimit = 200, ByteLimit = 8 * 1024 * 1024;
    public async Task<ReportValidation> ValidateAsync(Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var prepared = await PrepareAsync(id, Guid.Empty, DateTime.UtcNow, ct);
        return prepared.Material.Validation;
    }
    public async Task<ReportResponse> GenerateAsync(Guid id, ReportRequest request, CancellationToken ct)
    {
        if (!request.ConfirmApprovedContentReviewed) throw new SourceException(400, "Confirm that the approved content is ready for this report snapshot.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var reportId = Guid.NewGuid(); var generatedAt = DateTime.UtcNow;
        var (material, markdown) = await PrepareAsync(id, reportId, generatedAt, ct);
        if (!material.Validation.IsValid) throw new SourceException(422, "Report validation failed: " + string.Join(" ", material.Validation.Errors.Take(20)));
        var report = new GeneratedReport(reportId, id, generatedAt, markdown!, JsonSerializer.Serialize(material.Validation));
        db.GeneratedReports.Add(report);
        db.AuditEvents.Add(new()
        {
            InvestigationId = id,
            RecordType = "Report",
            RecordId = report.Id,
            Action = "Generated",
            AfterJson = JsonSerializer.Serialize(new { report.Id, report.GeneratedAt, report.ContentHash, material.Validation })
        });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(Summary(report), report.Markdown, material.Validation);
    }
    public async Task<PageResult<ReportSummary>> ListAsync(Guid id, int page, CancellationToken ct)
    {
        if (!await db.Investigations.AnyAsync(x => x.Id == id, ct)) throw new SourceException(404, "Investigation not found.");
        var query = db.GeneratedReports.AsNoTracking().Where(x => x.InvestigationId == id);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.GeneratedAt).ThenBy(x => x.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        return new(items.Select(Summary).ToList(), total, page, 20);
    }
    public async Task<ReportResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var report = await db.GeneratedReports.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new SourceException(404, "Report not found.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(report.Markdown))).ToLowerInvariant();
        if (report.ContentHash != hash) throw new SourceException(409, "Stored report integrity check failed.");
        ReportValidation? validation;
        try { validation = JsonSerializer.Deserialize<ReportValidation>(report.ValidationJson); }
        catch (JsonException) { throw new SourceException(409, "Stored report validation record is invalid."); }
        if (validation is null || !validation.IsValid) throw new SourceException(409, "Stored report has no valid generation record.");
        return new(Summary(report), report.Markdown, validation);
    }
    private static ReportSummary Summary(GeneratedReport report) => new(report.Id, report.InvestigationId, report.GeneratedAt, report.ContentHash, $"argus-{report.Id:N}.md");
    private async Task<(ReportMaterial Material, string? Markdown)> PrepareAsync(Guid id, Guid reportId, DateTime generatedAt, CancellationToken ct)
    {
        var inv = await db.Investigations.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new SourceException(404, "Investigation not found.");
        var errors = new List<string>(); var warnings = new List<string>();
        var claimQuery = db.Claims.Where(x => x.InvestigationId == id && x.DeletedAt == null && x.Status == ReviewStatus.APPROVED);
        var eventQuery = db.Events.Where(x => x.InvestigationId == id && x.DeletedAt == null && x.Status == ReviewStatus.APPROVED);
        var locationQuery = db.Locations.Where(x => x.InvestigationId == id && x.DeletedAt == null && x.Status == ReviewStatus.APPROVED);
        var entityQuery = db.Entities.Where(x => x.InvestigationId == id && x.MergedIntoId == null && db.EntityMentions.Any(m => m.EntityId == x.Id && m.Status == ReviewStatus.APPROVED));
        var claimCount = await claimQuery.CountAsync(ct); var eventCount = await eventQuery.CountAsync(ct);
        var locationCount = await locationQuery.CountAsync(ct); var entityCount = await entityQuery.CountAsync(ct);
        var associationCount = await db.ClaimEvidence.CountAsync(x => x.Active && claimQuery.Any(c => c.Id == x.ClaimId), ct)
            + await db.EventEvidence.CountAsync(x => x.Active && eventQuery.Any(e => e.Id == x.EventId), ct)
            + await db.LocationEvidence.CountAsync(x => x.Active && locationQuery.Any(l => l.Id == x.LocationId), ct)
            + await db.EntityMentions.CountAsync(x => x.Status == ReviewStatus.APPROVED && x.EntityId != null && entityQuery.Any(e => e.Id == x.EntityId), ct);
        if (new[] { claimCount, eventCount, locationCount, entityCount }.Any(x => x > FindingLimit) || associationCount > PassageLimit)
        {
            var blocked = new ReportValidation(false, [$"Export supports {FindingLimit} approved records per module and {PassageLimit} evidence associations in total; no data was truncated."], [], claimCount, entityCount, eventCount, locationCount, 0, 0);
            return (new(inv.ToResponse(), [], [], [], [], [], [], blocked), null);
        }
        var approvedClaims = await db.Claims.Where(x => x.InvestigationId == id && x.DeletedAt == null && x.Status == ReviewStatus.APPROVED).OrderBy(x => x.Id).Take(FindingLimit + 1).ToListAsync(ct);
        if (approvedClaims.Count == 0) errors.Add("At least one approved evidence-backed claim is required.");
        var claimDetails = new List<ClaimDetail>();
        foreach (var item in approvedClaims.Take(FindingLimit))
        {
            var detail = await claims.GetAsync(item.Id, ct); claimDetails.Add(detail);
            if (!detail.Evidence.Any(x => x.Relationship == EvidenceRelationship.SUPPORTS) || string.IsNullOrWhiteSpace(detail.Reasoning)) errors.Add($"Claim {item.Id} lacks support or analyst reasoning.");
            if (!Enum.IsDefined(item.ConfidenceLevel)) errors.Add($"Claim {item.Id} has invalid confidence.");
            if (detail.Claim.UnresolvedContradictions) warnings.Add($"Claim {item.Id} contains unresolved contradictory evidence.");
            if (item.ConfidenceLevel == ConfidenceLevel.INSUFFICIENT_INFORMATION) warnings.Add($"Claim {item.Id}: analyst confidence is INSUFFICIENT_INFORMATION.");
        }
        var eventsPage = await temporal.EventsAsync(id, ReviewStatus.APPROVED, null, null, null, true, 1, FindingLimit + 1, ct);
        var events = eventsPage.Items.ToList();
        var locations = (await temporal.LocationsAsync(id, ct)).Where(x => x.Status == ReviewStatus.APPROVED).ToList();
        var entityRecords = await db.Entities.Where(x => x.InvestigationId == id && x.MergedIntoId == null &&
            db.EntityMentions.Any(m => m.EntityId == x.Id && m.Status == ReviewStatus.APPROVED)).OrderBy(x => x.Id).Take(FindingLimit + 1).ToListAsync(ct);
        var entities = new List<ReportEntity>();
        foreach (var entity in entityRecords.Take(FindingLimit))
        {
            var evidence = await (from m in db.EntityMentions
                                  where m.EntityId == entity.Id && m.Status == ReviewStatus.APPROVED
                                  join snapshot in db.SourceTextSnapshots on m.SnapshotId equals snapshot.Id
                                  join source in db.Sources on m.SourceId equals source.Id
                                  orderby m.Id
                                  select new CitationResponse(m.Id, snapshot.Id, source.Id, source.Title, snapshot.TextHash,
                                      m.Start, m.End, TemporalRules.Quote(snapshot.Text, m.Start, m.End))).Take(PassageLimit + 1).ToListAsync(ct);
            entities.Add(new(entity.Id, entity.Name, entity.Type, evidence));
        }
        if (approvedClaims.Count > FindingLimit || eventsPage.Total > FindingLimit || locations.Count > FindingLimit || entityRecords.Count > FindingLimit)
            errors.Add($"A report supports at most {FindingLimit} approved records per module; split the investigation scope before export.");
        foreach (var item in events)
        {
            if (item.Evidence.Count == 0) errors.Add($"Event {item.Id} lacks citations.");
            try
            {
                var dates = TemporalRules.Dates(item.DatePrecision, TemporalDisplay.DateValue(item), item.DatePrecision == DatePrecision.EXACT ? item.EndAt?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : null);
                if (dates.Start != item.StartAt || dates.End != item.EndAt) errors.Add($"Event {item.Id} has inconsistent date precision.");
            }
            catch (ValidationException) { errors.Add($"Event {item.Id} has invalid dates."); }
        }
        foreach (var location in locations)
        {
            if (location.Evidence.Count == 0) errors.Add($"Location {location.Id} lacks citations.");
            try
            {
                _ = new LocationProfile(id, new()
                {
                    EntityId = location.EntityId,
                    Precision = location.Precision,
                    Latitude = location.Latitude,
                    Longitude = location.Longitude,
                    UncertaintyMeters = location.UncertaintyMeters,
                    Basis = location.Basis,
                    Notes = location.Notes,
                    ConfirmCoordinates = true
                });
            }
            catch (ValidationException) { errors.Add($"Location {location.Id} has invalid coordinate precision/basis."); }
        }
        var passages = claimDetails.SelectMany(x => x.Evidence).Select(x => new CitationResponse(x.Id, x.SnapshotId, x.SourceId, x.SourceTitle, x.TextHash, x.Start, x.End, x.Quote))
            .Concat(events.SelectMany(x => x.Evidence)).Concat(locations.SelectMany(x => x.Evidence)).Concat(entities.SelectMany(x => x.Evidence))
            .DistinctBy(x => (x.SnapshotId, x.Start, x.End)).OrderBy(x => x.SourceId).ThenBy(x => x.SnapshotId).ThenBy(x => x.Start).ThenBy(x => x.End).ToList();
        var sourceIds = passages.Select(x => x.SourceId).Distinct().ToArray();
        if (passages.Count > PassageLimit || sourceIds.Length > SourceLimit) errors.Add($"Report limits are {PassageLimit} distinct passages and {SourceLimit} sources.");
        var sources = await db.Sources.Where(x => sourceIds.Contains(x.Id)).ToListAsync(ct);
        var snapshotIds = passages.Select(x => x.SnapshotId).Distinct().ToArray();
        if (await db.SourceTextSnapshots.Where(x => snapshotIds.Contains(x.Id)).SumAsync(x => (long)x.Text.Length, ct) > 16_000_000)
        {
            var blocked = new ReportValidation(false, ["Referenced snapshots exceed the 16,000,000 UTF-16-unit verification budget."], warnings, claimCount, entityCount, eventCount, locationCount, sources.Count, passages.Count);
            return (new(inv.ToResponse(), [], [], [], [], [], [], blocked), null);
        }
        var snapshots = await db.SourceTextSnapshots.Where(x => snapshotIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var checkedHashes = new HashSet<Guid>();
        foreach (var passage in passages)
        {
            if (!snapshots.TryGetValue(passage.SnapshotId, out var snapshot) || snapshot.SourceId != passage.SourceId || !sources.Any(x => x.Id == passage.SourceId && x.InvestigationId == id))
            { errors.Add($"Passage {passage.Id} has invalid source provenance."); continue; }
            if (checkedHashes.Add(snapshot.Id) && SourceTextSnapshot.Hash(snapshot.Text) != snapshot.TextHash) errors.Add($"Snapshot {snapshot.Id} failed its SHA-256 check.");
            try
            {
                TemporalRules.Citation(snapshot.Text, passage.Start, passage.End);
                if (snapshot.Text[passage.Start..passage.End] != passage.Quote || passage.TextHash != snapshot.TextHash) errors.Add($"Passage {passage.Id} differs from its snapshot.");
            }
            catch (ValidationException) { errors.Add($"Passage {passage.Id} has invalid UTF-16 offsets."); }
        }
        if (sources.Sum(x => x.FileSize) > 256L * 1024 * 1024) errors.Add("Original verification exceeds the 256 MiB report budget.");
        if (errors.Count == 0)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(30));
            foreach (var source in sources)
            {
                try
                {
                    await using var stream = new FileStream(storage.Resolve(source.FilePath), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                    if (stream.Length != source.FileSize || stream.Length > 100L * 1024 * 1024) { errors.Add($"Source {source.Id} has an unexpected original size."); continue; }
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, deadline.Token)).ToLowerInvariant();
                    if (hash != source.ContentHash) errors.Add($"Source {source.Id} failed its original SHA-256 check.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { errors.Add($"Original document unavailable or invalid for source {source.Id}."); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { errors.Add("Original verification exceeded its 30-second deadline."); break; }
            }
        }
        if (events.Count == 0) warnings.Add("No approved events are included.");
        if (locations.Count == 0) warnings.Add("No approved geographic profiles are included.");
        if (entities.Count == 0) warnings.Add("No approved entity mentions are included.");
        if (events.Any(x => x.DatePrecision != DatePrecision.EXACT)) warnings.Add("Some event dates are partial or unknown.");
        if (locations.Any(x => x.Precision != GeoPrecision.EXACT)) warnings.Add("Some geographic locations are approximate or unknown.");
        var validation = new ReportValidation(errors.Count == 0, errors.Distinct().ToList(), warnings, approvedClaims.Count, entityRecords.Count, eventsPage.Total, locations.Count, sources.Count, passages.Count);
        var material = new ReportMaterial(inv.ToResponse(), claimDetails, entities, events, locations,
            sources.Select(x => new ReportSource(x.Id, x.Title, x.Publisher, x.Reliability, x.PublishedAt, x.RetrievedAt, x.ContentHash, x.OriginalUrl)).ToList(), passages, validation);
        string? markdown = null;
        if (validation.IsValid)
        {
            markdown = MarkdownReport.Render(material, reportId, generatedAt);
            if (Encoding.UTF8.GetByteCount(markdown) > ByteLimit)
            { validation = validation with { IsValid = false, Errors = ["Markdown report exceeds the 8 MiB export limit."] }; material = material with { Validation = validation }; markdown = null; }
        }
        return (material, markdown);
    }
}
