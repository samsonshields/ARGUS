using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Exceptions;
using Argus.Core.Interfaces;
using Argus.Core.Services;
using Argus.Infrastructure.Data;
using Argus.Infrastructure.ExternalServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Argus.Api.Services;

public sealed class ProcessingRunner(ArgusDbContext db, SourceService sources, ProcessingJobService jobs,
    IIntelligenceClient intelligence, IOptions<IntelligenceOptions> options, ILogger<ProcessingRunner> logger)
{
    public async Task<bool> RunNextAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var job = await db.ProcessingJobs.Where(x => x.State == JobState.PENDING && (x.NextAttemptAt == null || x.NextAttemptAt <= now))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (job is null) return false;
        var source = await db.Sources.SingleAsync(x => x.Id == job.SourceId, ct);
        job.Start(); source.StartProcessing();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return true; }
        try
        {
            await sources.ExtractForJobAsync(source, ct);
            await db.Entry(job).ReloadAsync(ct);
            if (job.State != JobState.RUNNING) return true;
            if (source.ProcessingStatus != SourceProcessingStatus.Ready)
                throw new ProcessingException("text_extraction_failed", source.ProcessingError ?? "Text extraction failed; original preserved.");
            var hash = SourceTextSnapshot.Hash(source.ExtractedText);
            var snapshot = await db.SourceTextSnapshots.FirstOrDefaultAsync(x => x.SourceId == source.Id && x.TextHash == hash, ct);
            if (snapshot is null) { snapshot = new(source); db.SourceTextSnapshots.Add(snapshot); }
            job.AttachSnapshot(snapshot.Id);
            await db.SaveChangesAsync(ct);
            var request = new NlpRequest("1", source.Id, snapshot.TextHash, snapshot.Text);
            var result = await intelligence.ProcessAsync(request, ct);
            EntityText.Validate(request, result, options.Value.MaxCandidates);
            await db.Entry(job).ReloadAsync(ct);
            if (job.State != JobState.RUNNING) return true;
            var existing = await db.EntityMentions.Where(x => x.SnapshotId == snapshot.Id).ToListAsync(ct);
            var seen = existing.Select(x => (x.Start, x.End, x.OriginalType)).ToHashSet();
            foreach (var candidate in result.Entities)
                if (seen.Add((candidate.Start, candidate.End, candidate.Type)))
                    db.EntityMentions.Add(new(source, snapshot, job, candidate, result.Model));
            job.Complete(result.Entities.Select(x => (x.Start, x.End, x.Type)).Distinct().Count());
            jobs.Audit(job, "Completed");
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Entity job {JobId} completed with {CandidateCount} candidates", job.Id, job.CandidateCount);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Leave RUNNING persisted. Startup recovery will requeue with the attempt budget intact.
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Cancellation/review won the race. EF rolls back this SaveChanges transaction.
            db.ChangeTracker.Clear();
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            job = await db.ProcessingJobs.SingleAsync(x => x.Id == job.Id, ct);
            if (job.State != JobState.RUNNING) return true;
            var failure = ex as ProcessingException ?? new("processing_failed", "Local processing failed; original documents and reviewed findings are preserved.", true);
            job.Fail(failure.Code, failure.Message, failure.Retryable, options.Value.MaxAttempts, options.Value.RetryDelaySeconds);
            // An extraction failure happened before the snapshot save; preserve its visible source status too.
            if (failure.Code == "text_extraction_failed" || job.State == JobState.FAILED)
            {
                var failedSource = await db.Sources.SingleAsync(x => x.Id == job.SourceId, ct);
                if (failure.Code == "text_extraction_failed" || failedSource.ProcessingStatus is SourceProcessingStatus.Pending or SourceProcessingStatus.Processing)
                    failedSource.FailExtraction(failure.Message);
            }
            jobs.Audit(job, job.State == JobState.PENDING ? "RetryScheduled" : "Failed");
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Entity job {JobId} failed with code {FailureCode}; attempt {Attempt}", job.Id, failure.Code, job.Attempts);
        }
        return true;
    }
}
