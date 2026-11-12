using System.Text.Json;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Exceptions;
using Argus.Core.Services;
using Argus.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Argus.Api.Services;

public sealed class ClaimService(ArgusDbContext db)
{
    public async Task<PageResult<ClaimSummary>> ListAsync(Guid id, ReviewStatus? status, ConfidenceLevel? confidence, bool unresolvedOnly,
        string? search, int page, int pageSize, CancellationToken ct)
    {
        await InvestigationAsync(id, ct);
        if ((status.HasValue && !Enum.IsDefined(status.Value)) || (confidence.HasValue && !Enum.IsDefined(confidence.Value)))
            throw new SourceException(400, "Unknown assessment filter.");
        var query = db.Claims.Where(x => x.InvestigationId == id && x.DeletedAt == null);
        if (status.HasValue) query = query.Where(x => x.Status == status);
        if (confidence.HasValue) query = query.Where(x => x.ConfidenceLevel == confidence);
        if (unresolvedOnly) query = query.Where(x => !x.ContradictionsResolved && db.ClaimEvidence.Any(e => e.ClaimId == x.Id && e.Active && e.Relationship == EvidenceRelationship.CONTRADICTS));
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => x.Statement.Contains(term)); }
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var result = new List<ClaimSummary>(); foreach (var item in items) result.Add(await SummaryAsync(item, ct));
        return new(result, count, page, pageSize);
    }
    public async Task<AssessmentSummary> SummaryAsync(Guid id, CancellationToken ct)
    {
        await InvestigationAsync(id, ct);
        var query = db.Claims.Where(x => x.InvestigationId == id && x.DeletedAt == null);
        var unresolved = query.Where(x => !x.ContradictionsResolved && db.ClaimEvidence.Any(e => e.ClaimId == x.Id && e.Active && e.Relationship == EvidenceRelationship.CONTRADICTS));
        return new(await query.CountAsync(x => x.Status == ReviewStatus.PROPOSED, ct), await query.CountAsync(x => x.Status == ReviewStatus.APPROVED, ct),
            await query.CountAsync(x => x.Status == ReviewStatus.REJECTED, ct), await unresolved.CountAsync(ct), await unresolved.CountAsync(x => x.Status == ReviewStatus.APPROVED, ct));
    }
    public async Task<ClaimDetail> GetAsync(Guid id, CancellationToken ct)
    {
        var item = await FindAsync(id, ct);
        var evidence = await (from e in db.ClaimEvidence
                              where e.ClaimId == id && e.Active
                              join snapshot in db.SourceTextSnapshots on e.SnapshotId equals snapshot.Id
                              join source in db.Sources on e.SourceId equals source.Id
                              orderby e.CreatedAt, e.Id
                              select new ClaimEvidenceResponse(e.Id, source.Id, source.Title, source.Reliability, snapshot.Id, snapshot.TextHash,
                                  e.Start, e.End, TemporalRules.Quote(snapshot.Text, e.Start, e.End), e.Relationship, e.Notes)).ToListAsync(ct);
        return new(await SummaryAsync(item, ct), item.Reasoning, item.ContradictionsResolved, item.ContradictionResolution, item.CreatedAt, evidence);
    }
    public async Task<ClaimDetail> SaveAsync(Guid investigationId, Guid? id, ClaimEditRequest request, CancellationToken ct)
    {
        await InvestigationAsync(investigationId, ct);
        Claim item; string? before = null;
        if (id.HasValue)
        {
            item = await FindAsync(id.Value, ct); Revision(item, request.Revision); before = JsonSerializer.Serialize(item);
            if (item.InvestigationId != investigationId) throw new SourceException(400, "Investigation mismatch.");
            if (request.ContradictionsResolved && !await db.ClaimEvidence.AnyAsync(x => x.ClaimId == id && x.Active && x.Relationship == EvidenceRelationship.CONTRADICTS, ct))
                throw new SourceException(400, "There is no active contradictory evidence to resolve.");
            item.Update(request);
        }
        else
        {
            if (request.ContradictionsResolved) throw new SourceException(400, "Attach contradictory evidence before recording a resolution.");
            item = new(investigationId, request); db.Claims.Add(item);
        }
        Audit(item, id.HasValue ? "Corrected" : "Created", item, before); await db.SaveChangesAsync(ct); return await GetAsync(item.Id, ct);
    }
    public async Task AddEvidenceAsync(Guid id, ClaimEvidenceRequest request, CancellationToken ct)
    {
        var item = await FindAsync(id, ct); Revision(item, request.Revision);
        var snapshot = await db.SourceTextSnapshots.FindAsync([request.SnapshotId], ct) ?? throw new SourceException(404, "Snapshot not found.");
        var source = await db.Sources.SingleAsync(x => x.Id == snapshot.SourceId, ct);
        if (source.InvestigationId != item.InvestigationId) throw new SourceException(400, "Citation belongs to another investigation.");
        TemporalRules.Citation(snapshot.Text, request.Start, request.End);
        var citations = await db.ClaimEvidence.Where(x => x.ClaimId == id).ToListAsync(ct);
        var existing = citations.SingleOrDefault(x => x.SnapshotId == request.SnapshotId && x.Start == request.Start && x.End == request.End);
        var before = JsonSerializer.Serialize(new { Claim = item, Evidence = existing });
        if (existing is null)
        {
            if (citations.Count >= 100) throw new SourceException(422, "A claim supports at most 100 retained passages.");
            existing = new(id, snapshot, request); db.ClaimEvidence.Add(existing);
        }
        else existing.Update(request);
        item.EvidenceChanged(); Audit(item, "EvidenceAttached", new { Claim = item, Evidence = existing }, before); await db.SaveChangesAsync(ct);
    }
    public async Task RemoveEvidenceAsync(Guid id, Guid evidenceId, int revision, CancellationToken ct)
    {
        var item = await FindAsync(id, ct); Revision(item, revision);
        var evidence = await db.ClaimEvidence.SingleOrDefaultAsync(x => x.Id == evidenceId && x.ClaimId == id && x.Active, ct)
            ?? throw new SourceException(404, "Active evidence not found.");
        var before = JsonSerializer.Serialize(new { Claim = item, Evidence = evidence });
        evidence.Remove(); item.EvidenceChanged(); Audit(item, "EvidenceRemoved", new { Claim = item, Evidence = evidence }, before); await db.SaveChangesAsync(ct);
    }
    public async Task ReviewAsync(Guid id, bool approve, ClaimReviewRequest request, CancellationToken ct)
    {
        var item = await FindAsync(id, ct); Revision(item, request.Revision); var before = JsonSerializer.Serialize(item);
        var evidence = await db.ClaimEvidence.Where(x => x.ClaimId == id && x.Active).ToListAsync(ct);
        if (approve)
        {
            foreach (var citation in evidence)
            {
                var snapshot = await db.SourceTextSnapshots.SingleAsync(x => x.Id == citation.SnapshotId, ct);
                TemporalRules.Citation(snapshot.Text, citation.Start, citation.End);
                if (snapshot.TextHash != SourceTextSnapshot.Hash(snapshot.Text) || !await db.Sources.AnyAsync(x => x.Id == citation.SourceId && x.InvestigationId == item.InvestigationId, ct))
                    throw new SourceException(409, "Citation provenance validation failed.");
            }
        }
        item.Review(approve, request, evidence.Select(x => x.Relationship));
        Audit(item, approve ? "Approved" : "Rejected", item, before); await db.SaveChangesAsync(ct);
    }
    public async Task RemoveAsync(Guid id, ReviewTemporalRequest request, CancellationToken ct)
    {
        var item = await FindAsync(id, ct); Revision(item, request.Revision);
        if (!request.Confirm) throw new SourceException(400, "Confirm removal; citations and report snapshots will be retained.");
        var before = JsonSerializer.Serialize(item); item.Remove(); Audit(item, "Removed", item, before); await db.SaveChangesAsync(ct);
    }
    private async Task<ClaimSummary> SummaryAsync(Claim item, CancellationToken ct)
    {
        var evidence = await db.ClaimEvidence.Where(x => x.ClaimId == item.Id && x.Active).Select(x => x.Relationship).ToListAsync(ct);
        var contradicts = evidence.Count(x => x == EvidenceRelationship.CONTRADICTS);
        return new(item.Id, item.InvestigationId, item.Statement, item.ConfidenceLevel, item.Status, evidence.Count(x => x == EvidenceRelationship.SUPPORTS),
            contradicts, evidence.Count(x => x == EvidenceRelationship.CONTEXT), contradicts > 0 && !item.ContradictionsResolved, item.Revision, item.UpdatedAt);
    }
    private async Task<Claim> FindAsync(Guid id, CancellationToken ct) => await db.Claims.SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct) ?? throw new SourceException(404, "Claim not found.");
    private async Task InvestigationAsync(Guid id, CancellationToken ct)
    { if (!await db.Investigations.AnyAsync(x => x.Id == id, ct)) throw new SourceException(404, "Investigation not found."); }
    private static void Revision(Claim item, int revision)
    { if (item.Revision != revision) throw new SourceException(409, "This claim changed. Reload before editing or reviewing."); }
    private void Audit(Claim item, string action, object after, string? before) => db.AuditEvents.Add(new()
    { InvestigationId = item.InvestigationId, RecordType = "Claim", RecordId = item.Id, Action = action, BeforeJson = before, AfterJson = JsonSerializer.Serialize(after) });
}
