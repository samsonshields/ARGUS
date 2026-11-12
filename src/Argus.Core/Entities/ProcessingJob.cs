using Argus.Core.DTOs;
using Argus.Core.Enums;

namespace Argus.Core.Entities;

public sealed class ProcessingJob
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid SourceId { get; private set; }
    public Guid InvestigationId { get; private set; }
    public JobState State { get; private set; } = JobState.PENDING;
    public int Attempts { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? Error { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? NextAttemptAt { get; private set; }
    public Guid? SnapshotId { get; private set; }
    public int CandidateCount { get; private set; }
    public int Revision { get; private set; }
    public JobKind Kind { get; private set; } = JobKind.ENTITIES;
    private ProcessingJob() { }
    public ProcessingJob(Source source, JobKind kind = JobKind.ENTITIES) { SourceId = source.Id; InvestigationId = source.InvestigationId; Kind = kind; }
    private void Changed() { UpdatedAt = DateTime.UtcNow; Revision++; }
    public void Start()
    {
        if (State != JobState.PENDING) throw new InvalidOperationException("Only pending jobs can start.");
        State = JobState.RUNNING; Attempts++; NextAttemptAt = null; Changed();
    }
    public void AttachSnapshot(Guid id)
    {
        if (State != JobState.RUNNING) throw new InvalidOperationException("Job is not running.");
        SnapshotId = id; Changed();
    }
    public void Complete(int count)
    {
        if (State != JobState.RUNNING) throw new InvalidOperationException("Job is not running.");
        State = JobState.COMPLETED; CandidateCount = count; ErrorCode = Error = null; Changed();
    }
    public void Fail(string code, string error, bool retry, int maxAttempts, int retryDelaySeconds)
    {
        if (State != JobState.RUNNING) throw new InvalidOperationException("Job is not running.");
        ErrorCode = code; Error = error;
        State = retry && Attempts < maxAttempts ? JobState.PENDING : JobState.FAILED;
        NextAttemptAt = State == JobState.PENDING ? DateTime.UtcNow.AddSeconds(retryDelaySeconds) : null;
        Changed();
    }
    public void Recover(int maxAttempts) => Fail("interrupted", "Processing was interrupted by application shutdown; retry is bounded.", true, maxAttempts, 0);
    public void Cancel()
    {
        if (State is not (JobState.PENDING or JobState.RUNNING)) throw new InvalidOperationException("Only active jobs can be cancelled.");
        State = JobState.CANCELLED; NextAttemptAt = null; ErrorCode = "cancelled"; Error = "Cancelled by the analyst."; Changed();
    }
    public JobResponse ToResponse() => new(Id, SourceId, InvestigationId, State, Attempts, ErrorCode,
        Error, CreatedAt, UpdatedAt, NextAttemptAt, SnapshotId, CandidateCount, Kind);
}
