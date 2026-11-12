using System.Text.Json;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Exceptions;
using Argus.Infrastructure.Data;
using Argus.Infrastructure.ExternalServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Argus.Api.Services;

public sealed class ProcessingJobService(ArgusDbContext db, IOptions<IntelligenceOptions> options)
{
    public ProcessingJob AddForImport(Source source, JobKind kind = JobKind.ENTITIES)
    {
        if (kind == JobKind.ENTITIES) source.QueueProcessing();
        var job = new ProcessingJob(source, kind);
        db.ProcessingJobs.Add(job);
        Audit(job, "Queued");
        return job;
    }

    public async Task<JobResponse> QueueAsync(Guid sourceId, CancellationToken ct, JobKind kind = JobKind.ENTITIES)
    {
        var source = await db.Sources.FindAsync([sourceId], ct) ?? throw new SourceException(404, "Source not found.");
        if (await HasActiveAsync(sourceId, ct)) throw new SourceException(409, "This source already has an active processing job.");
        var job = AddForImport(source, kind);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 })
        { throw new SourceException(409, "A processing job was queued concurrently. Reload the source."); }
        return job.ToResponse();
    }
    public Task<bool> HasActiveAsync(Guid id, CancellationToken ct) => db.ProcessingJobs.AnyAsync(x => x.SourceId == id &&
        (x.State == JobState.PENDING || x.State == JobState.RUNNING), ct);
    public async Task<JobResponse> GetAsync(Guid id, CancellationToken ct) => (await FindAsync(id, ct)).ToResponse();
    public async Task<PageResult<JobResponse>> ListAsync(Guid? investigationId, Guid? sourceId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ProcessingJobs.AsNoTracking().AsQueryable();
        if (investigationId.HasValue) query = query.Where(x => x.InvestigationId == investigationId);
        if (sourceId.HasValue) query = query.Where(x => x.SourceId == sourceId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(items.Select(x => x.ToResponse()).ToList(), total, page, pageSize);
    }
    public async Task<JobResponse> CancelAsync(Guid id, CancellationToken ct)
    {
        var job = await FindAsync(id, ct);
        if (job.State is not (JobState.PENDING or JobState.RUNNING)) throw new SourceException(409, "Only active jobs can be cancelled.");
        job.Cancel();
        var source = await db.Sources.FindAsync([job.SourceId], ct);
        if (job.Kind == JobKind.ENTITIES) source!.CancelProcessing();
        Audit(job, "Cancelled");
        await db.SaveChangesAsync(ct);
        return job.ToResponse();
    }
    public async Task<JobResponse> RetryAsync(Guid id, CancellationToken ct)
    {
        var job = await FindAsync(id, ct);
        if (job.State is not (JobState.FAILED or JobState.CANCELLED)) throw new SourceException(409, "Only failed or cancelled jobs can be retried.");
        return await QueueAsync(job.SourceId, ct, job.Kind);
    }
    public async Task RecoverAsync(CancellationToken ct)
    {
        var jobs = await db.ProcessingJobs.Where(x => x.State == JobState.RUNNING).ToListAsync(ct);
        foreach (var job in jobs)
        {
            job.Recover(options.Value.MaxAttempts);
            Audit(job, "RecoveredAfterInterruption");
            if (job.State == JobState.FAILED && job.Kind == JobKind.ENTITIES)
            {
                var source = await db.Sources.FindAsync([job.SourceId], ct);
                source!.CancelProcessing();
            }
        }
        await db.SaveChangesAsync(ct);
    }
    private async Task<ProcessingJob> FindAsync(Guid id, CancellationToken ct) => await db.ProcessingJobs.FindAsync([id], ct)
        ?? throw new SourceException(404, "Processing job not found.");
    internal void Audit(ProcessingJob job, string action) => db.AuditEvents.Add(new AuditEvent
    {
        InvestigationId = job.InvestigationId,
        RecordType = "ProcessingJob",
        RecordId = job.Id,
        Action = action,
        AfterJson = JsonSerializer.Serialize(job.ToResponse())
    });
}
