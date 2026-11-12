using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Exceptions;
using Argus.Core.Interfaces;
using Argus.Core.Services;
using Argus.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Argus.Api.Services;

public sealed class TemporalService(ArgusDbContext db, IEventClient client)
{
    public async Task<PageResult<EventResponse>> EventsAsync(Guid investigationId, ReviewStatus? status, EventType? type,
        DateOnly? from, DateOnly? to, bool includeUnknown, int page, int pageSize, CancellationToken ct)
    {
        await InvestigationAsync(investigationId, ct);
        var query = EventQuery(investigationId, status, type, from, to, includeUnknown);
        var count = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.StartAt == null).ThenBy(x => x.StartAt).ThenBy(x => x.Title).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var result = new List<EventResponse>();
        foreach (var item in items) result.Add(await EventResponseAsync(item, ct));
        return new(result, count, page, pageSize);
    }
    private IQueryable<AnalysisEvent> EventQuery(Guid id, ReviewStatus? status, EventType? type, DateOnly? from, DateOnly? to, bool includeUnknown)
    {
        if (from > to) throw new SourceException(400, "Date filter end precedes its start.");
        if ((status.HasValue && !Enum.IsDefined(status.Value)) || (type.HasValue && !Enum.IsDefined(type.Value)))
            throw new SourceException(400, "Unknown event filter.");
        var query = db.Events.Where(x => x.InvestigationId == id && x.DeletedAt == null);
        if (status.HasValue) query = query.Where(x => x.Status == status);
        if (type.HasValue) query = query.Where(x => x.Type == type);
        if (!includeUnknown) query = query.Where(x => x.StartAt != null);
        if (from.HasValue) query = query.Where(x => (includeUnknown && x.StartAt == null) || x.EndAt >= from);
        if (to.HasValue) query = query.Where(x => (includeUnknown && x.StartAt == null) || x.StartAt <= to);
        return query;
    }
    public async Task<EventResponse> EventAsync(Guid id, CancellationToken ct) => await EventResponseAsync(await FindEventAsync(id, ct), ct);
    public async Task<EventResponse> SaveEventAsync(Guid investigationId, Guid? id, EventEditRequest request, CancellationToken ct)
    {
        await InvestigationAsync(investigationId, ct);
        if (request.EntityIds is null || request.EntityIds.Count > 100) throw new SourceException(400, "Select at most 100 entities.");
        var ids = request.EntityIds.Distinct().ToArray();
        var entities = await db.Entities.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        if (entities.Count != ids.Length || entities.Any(x => x.InvestigationId != investigationId || x.MergedIntoId != null))
            throw new SourceException(400, "Select active entities from this investigation.");
        foreach (var entity in entities) entity.TouchForAssociation();
        if (request.LocationId.HasValue)
        {
            var location = await FindLocationAsync(request.LocationId.Value, ct);
            if (location.InvestigationId != investigationId || location.Status != ReviewStatus.APPROVED)
                throw new SourceException(400, "Select an approved location in this investigation.");
            // Compete with concurrent location edits/retirement without changing its review state.
            db.Entry(location).Property(x => x.Revision).CurrentValue++;
        }
        AnalysisEvent item;
        if (id.HasValue)
        {
            item = await FindEventAsync(id.Value, ct);
            if (item.InvestigationId != investigationId) throw new SourceException(400, "Investigation mismatch.");
            Revision(item.Revision, request.Revision); item.Update(request);
        }
        else { item = new(investigationId, request); db.Events.Add(item); }
        var old = await db.EventEntities.Where(x => x.EventId == item.Id).ToListAsync(ct);
        db.EventEntities.RemoveRange(old.Where(x => !ids.Contains(x.EntityId)));
        foreach (var entityId in ids.Except(old.Select(x => x.EntityId))) db.EventEntities.Add(new(item, entityId));
        Audit(investigationId, "Event", item.Id, id.HasValue ? "Corrected" : "Created", item);
        await db.SaveChangesAsync(ct); return await EventResponseAsync(item, ct);
    }
    public async Task<List<LocationResponse>> LocationsAsync(Guid id, CancellationToken ct)
    {
        await InvestigationAsync(id, ct);
        var items = await db.Locations.Where(x => x.InvestigationId == id && x.DeletedAt == null).OrderBy(x => x.Id).Take(2001).ToListAsync(ct);
        if (items.Count > 2000) throw new SourceException(422, "Location limit is 2000 per investigation.");
        var result = new List<LocationResponse>();
        foreach (var item in items) result.Add(await LocationResponseAsync(item, ct));
        return result;
    }
    public async Task<LocationResponse> LocationAsync(Guid id, CancellationToken ct) => await LocationResponseAsync(await FindLocationAsync(id, ct), ct);
    public async Task<LocationResponse> SaveLocationAsync(Guid investigationId, Guid? id, LocationEditRequest request, CancellationToken ct)
    {
        await InvestigationAsync(investigationId, ct);
        var entity = await db.Entities.FindAsync([request.EntityId], ct);
        if (entity is null || entity.InvestigationId != investigationId || entity.MergedIntoId.HasValue || entity.Type != EntityType.LOCATION)
            throw new SourceException(400, "Select an active approved LOCATION entity in this investigation.");
        entity.TouchForAssociation();
        if (await db.Locations.AnyAsync(x => x.EntityId == entity.Id && x.DeletedAt == null && x.Id != id, ct))
            throw new SourceException(409, "This entity already has an active location profile.");
        LocationProfile item;
        if (id.HasValue)
        {
            item = await FindLocationAsync(id.Value, ct);
            if (item.InvestigationId != investigationId || item.EntityId != request.EntityId) throw new SourceException(400, "A location's entity cannot be replaced.");
            Revision(item.Revision, request.Revision); item.Update(request);
        }
        else { item = new(investigationId, request); db.Locations.Add(item); }
        Audit(investigationId, "Location", item.Id, id.HasValue ? "Corrected" : "Created", item);
        await db.SaveChangesAsync(ct); return await LocationResponseAsync(item, ct);
    }
    public async Task ReviewAsync(Guid id, bool location, bool approve, ReviewTemporalRequest request, CancellationToken ct)
    {
        if (!request.Confirm) throw new SourceException(400, "Confirm that you reviewed the record and its source evidence.");
        if (location)
        {
            var item = await FindLocationAsync(id, ct); Revision(item.Revision, request.Revision);
            var hasEvidence = await db.LocationEvidence.AnyAsync(x => x.LocationId == id && x.Active, ct);
            if (approve && !hasEvidence) throw new SourceException(409, "Approval requires a source citation.");
            item.Review(approve, hasEvidence);
            Audit(item.InvestigationId, "Location", id, approve ? "Approved" : "Rejected", item);
        }
        else
        {
            var item = await FindEventAsync(id, ct); Revision(item.Revision, request.Revision);
            var hasEvidence = await db.EventEvidence.AnyAsync(x => x.EventId == id && x.Active, ct);
            if (approve && !hasEvidence) throw new SourceException(409, "Approval requires a source citation.");
            item.Review(approve, hasEvidence);
            Audit(item.InvestigationId, "Event", id, approve ? "Approved" : "Rejected", item);
        }
        await db.SaveChangesAsync(ct);
    }
    public async Task RemoveAsync(Guid id, bool location, ReviewTemporalRequest request, CancellationToken ct)
    {
        if (!request.Confirm) throw new SourceException(400, "Confirm removal; citation history will be retained.");
        if (location)
        {
            var item = await FindLocationAsync(id, ct); Revision(item.Revision, request.Revision); item.Remove();
            Audit(item.InvestigationId, "Location", id, "Removed", item);
        }
        else
        {
            var item = await FindEventAsync(id, ct); Revision(item.Revision, request.Revision); item.Remove();
            Audit(item.InvestigationId, "Event", id, "Removed", item);
        }
        await db.SaveChangesAsync(ct);
    }
    public async Task AddEvidenceAsync(Guid id, bool location, CitationRequest request, CancellationToken ct)
    {
        var snapshot = await db.SourceTextSnapshots.FindAsync([request.SnapshotId], ct) ?? throw new SourceException(404, "Text snapshot not found.");
        var source = await db.Sources.SingleAsync(x => x.Id == snapshot.SourceId, ct);
        TemporalRules.Citation(snapshot.Text, request.Start, request.End);
        Guid investigationId;
        if (location)
        {
            var item = await FindLocationAsync(id, ct); investigationId = item.InvestigationId; Revision(item.Revision, request.Revision);
            var citations = await db.LocationEvidence.Where(x => x.LocationId == id).ToListAsync(ct);
            var existing = citations.SingleOrDefault(x => x.SnapshotId == snapshot.Id && x.Start == request.Start && x.End == request.End);
            if (existing is null) { CheckCitationLimit(citations.Count); db.LocationEvidence.Add(new(id, snapshot, request.Start, request.End)); }
            else existing.SetActive(true);
            item.Touch();
        }
        else
        {
            var item = await FindEventAsync(id, ct); investigationId = item.InvestigationId; Revision(item.Revision, request.Revision);
            var citations = await db.EventEvidence.Where(x => x.EventId == id).ToListAsync(ct);
            var existing = citations.SingleOrDefault(x => x.SnapshotId == snapshot.Id && x.Start == request.Start && x.End == request.End);
            if (existing is null) { CheckCitationLimit(citations.Count); db.EventEvidence.Add(new(id, snapshot, request.Start, request.End)); }
            else existing.SetActive(true);
            item.Touch();
        }
        if (source.InvestigationId != investigationId) throw new SourceException(400, "Citation belongs to another investigation.");
        Audit(investigationId, location ? "Location" : "Event", id, "CitationAdded", request);
        await db.SaveChangesAsync(ct);
    }
    public async Task RemoveEvidenceAsync(Guid id, bool location, Guid evidenceId, int revision, CancellationToken ct)
    {
        Guid investigationId;
        if (location)
        {
            var item = await FindLocationAsync(id, ct); investigationId = item.InvestigationId; Revision(item.Revision, revision);
            var citation = await db.LocationEvidence.SingleOrDefaultAsync(x => x.Id == evidenceId && x.LocationId == id, ct) ?? throw new SourceException(404, "Citation not found.");
            citation.SetActive(false); item.Touch();
        }
        else
        {
            var item = await FindEventAsync(id, ct); investigationId = item.InvestigationId; Revision(item.Revision, revision);
            var citation = await db.EventEvidence.SingleOrDefaultAsync(x => x.Id == evidenceId && x.EventId == id, ct) ?? throw new SourceException(404, "Citation not found.");
            citation.SetActive(false); item.Touch();
        }
        Audit(investigationId, location ? "Location" : "Event", id, "CitationRemoved", new { evidenceId }); await db.SaveChangesAsync(ct);
    }
    public async Task<PageResult<PassageResponse>> PassagesAsync(Guid sourceId, int page, CancellationToken ct)
    {
        var source = await db.Sources.FindAsync([sourceId], ct) ?? throw new SourceException(404, "Source not found.");
        if (string.IsNullOrWhiteSpace(source.ExtractedText)) throw new SourceException(409, "Extract source text before selecting citations.");
        var hash = SourceTextSnapshot.Hash(source.ExtractedText);
        var snapshot = await db.SourceTextSnapshots.SingleOrDefaultAsync(x => x.SourceId == sourceId && x.TextHash == hash, ct);
        if (snapshot is null) { snapshot = new(source); db.SourceTextSnapshots.Add(snapshot); await db.SaveChangesAsync(ct); }
        var passages = new List<PassageResponse>();
        for (var start = 0; start < snapshot.Text.Length;)
        {
            var end = Math.Min(start + 2000, snapshot.Text.Length);
            if (!EntityText.Boundary(snapshot.Text, end)) end--;
            passages.Add(new(snapshot.Id, sourceId, source.Title, snapshot.TextHash, start, end, snapshot.Text[start..end])); start = end;
        }
        return new(passages.Skip((page - 1) * 10).Take(10).ToList(), passages.Count, page, 10);
    }
    public async Task<int> PrepareEventsAsync(Source source, ProcessingJob job, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(source.ExtractedText)) throw new ProcessingException("text_not_ready", "Extract source text before event generation.");
        var hash = SourceTextSnapshot.Hash(source.ExtractedText);
        var snapshot = await db.SourceTextSnapshots.SingleOrDefaultAsync(x => x.SourceId == source.Id && x.TextHash == hash, ct);
        if (snapshot is null) { snapshot = new(source); db.SourceTextSnapshots.Add(snapshot); }
        job.AttachSnapshot(snapshot.Id);
        var request = new NlpRequest("1", source.Id, hash, snapshot.Text);
        var result = await client.ProcessEventsAsync(request, ct); TemporalRules.Validate(request, result);
        var keys = (await db.Events.Where(x => x.InvestigationId == source.InvestigationId && x.OriginKey != null).Select(x => x.OriginKey!).ToListAsync(ct)).ToHashSet();
        foreach (var candidate in result.Events)
        {
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{snapshot.Id}:date_mentions_v1:{candidate.Start}:{candidate.End}")));
            if (!keys.Add(key)) continue;
            var item = new AnalysisEvent(source.InvestigationId, candidate, job.Id, key);
            db.Events.Add(item); db.EventEvidence.Add(new(item.Id, snapshot, candidate.SentenceStart, candidate.SentenceEnd));
        }
        return result.Events.Select(x => (x.Start, x.End)).Distinct().Count();
    }
    public async Task<MapResponse> MapAsync(Guid investigationId, EventType? type, DateOnly? from, DateOnly? to, bool includeUnknown, CancellationToken ct)
    {
        var locations = (await LocationsAsync(investigationId, ct)).Where(x => x.Status == ReviewStatus.APPROVED && x.Precision != GeoPrecision.UNKNOWN).ToList();
        var ids = locations.Select(x => x.Id).ToArray();
        var items = await EventQuery(investigationId, ReviewStatus.APPROVED, type, from, to, includeUnknown)
            .Where(x => x.LocationId != null && ids.Contains(x.LocationId.Value)).OrderBy(x => x.StartAt).ThenBy(x => x.Id).Take(2001).ToListAsync(ct);
        if (items.Count > 2000) throw new SourceException(422, "Map event limit is 2000; narrow the filters.");
        var events = new List<EventResponse>(); foreach (var item in items) events.Add(await EventResponseAsync(item, ct));
        var focus = await db.GeographicFocus.FindAsync([investigationId], ct);
        return new(locations, events, focus is null ? null : new(focus.Latitude, focus.Longitude, focus.Zoom));
    }
    public async Task SaveFocusAsync(Guid investigationId, MapFocusRequest request, CancellationToken ct)
    {
        await InvestigationAsync(investigationId, ct);
        var focus = await db.GeographicFocus.FindAsync([investigationId], ct);
        if (focus is null) db.GeographicFocus.Add(new(investigationId, request)); else focus.Update(request);
        await db.SaveChangesAsync(ct);
    }
    private async Task<EventResponse> EventResponseAsync(AnalysisEvent item, CancellationToken ct) => new(item.Id, item.InvestigationId, item.Title,
        item.Description, item.StartAt, item.EndAt, item.DatePrecision, item.Type, item.Status, item.LocationId,
        await db.EventEntities.Where(x => x.EventId == item.Id).Select(x => x.EntityId).ToListAsync(ct), item.OriginalDateText, item.Interpretation, item.Revision,
        await (from e in db.EventEvidence
               where e.EventId == item.Id && e.Active
               join s in db.SourceTextSnapshots on e.SnapshotId equals s.Id
               join source in db.Sources on e.SourceId equals source.Id
               orderby e.CreatedAt, e.Id
               select new CitationResponse(e.Id, s.Id, source.Id, source.Title, s.TextHash, e.Start, e.End, TemporalRules.Quote(s.Text, e.Start, e.End))).ToListAsync(ct));
    private async Task<LocationResponse> LocationResponseAsync(LocationProfile item, CancellationToken ct) => new(item.Id, item.InvestigationId, item.EntityId,
        await db.Entities.Where(x => x.Id == item.EntityId).Select(x => x.Name).SingleAsync(ct), item.Precision, item.Latitude, item.Longitude, item.UncertaintyMeters,
        item.Basis, item.Notes, item.Status, item.Revision,
        await (from e in db.LocationEvidence
               where e.LocationId == item.Id && e.Active
               join s in db.SourceTextSnapshots on e.SnapshotId equals s.Id
               join source in db.Sources on e.SourceId equals source.Id
               orderby e.CreatedAt, e.Id
               select new CitationResponse(e.Id, s.Id, source.Id, source.Title, s.TextHash, e.Start, e.End, TemporalRules.Quote(s.Text, e.Start, e.End))).ToListAsync(ct));
    private async Task InvestigationAsync(Guid id, CancellationToken ct)
    { if (!await db.Investigations.AnyAsync(x => x.Id == id, ct)) throw new SourceException(404, "Investigation not found."); }
    private async Task<AnalysisEvent> FindEventAsync(Guid id, CancellationToken ct) => await db.Events.SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct)
        ?? throw new SourceException(404, "Event not found.");
    private async Task<LocationProfile> FindLocationAsync(Guid id, CancellationToken ct) => await db.Locations.SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct)
        ?? throw new SourceException(404, "Location not found.");
    private static void Revision(int actual, int expected) { if (actual != expected) throw new SourceException(409, "This record changed. Reload before editing/reviewing."); }
    private static void CheckCitationLimit(int count) { if (count >= 100) throw new SourceException(422, "A record supports at most 100 retained citations."); }
    private void Audit(Guid investigationId, string recordType, Guid id, string action, object after) => db.AuditEvents.Add(new()
    { InvestigationId = investigationId, RecordType = recordType, RecordId = id, Action = action, AfterJson = JsonSerializer.Serialize(after) });
}
