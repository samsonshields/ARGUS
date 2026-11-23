using Argus.Core.DTOs;
using Argus.Core.Enums;
using Argus.Core.Exceptions;
using Argus.Core.Services;
using Argus.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Argus.Api.Services;

public sealed class WorkflowService(ArgusDbContext db)
{
    public async Task<WorkflowSummary> SummaryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Investigations.AnyAsync(x => x.Id == id, ct)) throw new SourceException(404, "Investigation not found.");
        var mentions = db.EntityMentions.AsNoTracking().Where(x => x.InvestigationId == id);
        var relationships = db.Relationships.AsNoTracking().Where(x => x.InvestigationId == id && x.RetiredReason == null);
        var events = db.Events.AsNoTracking().Where(x => x.InvestigationId == id && x.DeletedAt == null);
        var locations = db.Locations.AsNoTracking().Where(x => x.InvestigationId == id && x.DeletedAt == null);
        var claims = db.Claims.AsNoTracking().Where(x => x.InvestigationId == id && x.DeletedAt == null);
        var counts = new WorkflowCounts(
            await db.Sources.CountAsync(x => x.InvestigationId == id, ct),
            await mentions.CountAsync(ct),
            await StageAsync(mentions.Select(x => x.Status), ct),
            await StageAsync(relationships.Select(x => x.Status), ct),
            await StageAsync(events.Select(x => x.Status), ct),
            await StageAsync(locations.Select(x => x.Status), ct),
            await StageAsync(claims.Select(x => x.Status), ct),
            // Active contradicting evidence not marked resolved (as ClaimService.SummaryAsync), on claims still in play.
            await claims.CountAsync(x => x.Status != ReviewStatus.REJECTED && !x.ContradictionsResolved
                && db.ClaimEvidence.Any(e => e.ClaimId == x.Id && e.Active && e.Relationship == EvidenceRelationship.CONTRADICTS), ct),
            await db.GeneratedReports.CountAsync(x => x.InvestigationId == id, ct));
        return WorkflowRules.Summarize(counts);
    }

    private static async Task<StageCounts> StageAsync(IQueryable<ReviewStatus> statuses, CancellationToken ct)
    {
        var totals = await statuses.GroupBy(x => x).Select(x => new { Status = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        return new(totals.GetValueOrDefault(ReviewStatus.PROPOSED), totals.GetValueOrDefault(ReviewStatus.APPROVED));
    }
}
