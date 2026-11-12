using Argus.Core.DTOs;
using Argus.Core.Enums;

namespace Argus.Core.Entities;

public sealed class EntityMention
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid SourceId { get; private set; }
    public Guid InvestigationId { get; private set; }
    public Guid SnapshotId { get; private set; }
    public Guid ProcessingJobId { get; private set; }
    public string Text { get; private set; } = "";
    public EntityType OriginalType { get; private set; }
    public int Start { get; private set; }
    public int End { get; private set; }
    public ReviewStatus Status { get; private set; } = ReviewStatus.PROPOSED;
    public Guid? EntityId { get; private set; }
    public string ModelName { get; private set; } = "";
    public string ModelVersion { get; private set; } = "";
    public DateTime? ReviewedAt { get; private set; }
    public int Revision { get; private set; }
    private EntityMention() { }
    public EntityMention(Source source, SourceTextSnapshot snapshot, ProcessingJob job, NlpCandidate candidate, NlpModel model)
    {
        SourceId = source.Id; InvestigationId = source.InvestigationId; SnapshotId = snapshot.Id; ProcessingJobId = job.Id;
        Text = candidate.Text; OriginalType = candidate.Type; Start = candidate.Start; End = candidate.End;
        ModelName = model.Name; ModelVersion = $"{model.Version} / spaCy {model.SpacyVersion}";
    }
    public void Approve(IntelligenceEntity entity)
    {
        if (Status != ReviewStatus.PROPOSED || entity.InvestigationId != InvestigationId || entity.MergedIntoId.HasValue)
            throw new InvalidOperationException("Candidate must be proposed and entity must be active in this investigation.");
        Status = ReviewStatus.APPROVED; EntityId = entity.Id; ReviewedAt = DateTime.UtcNow; Revision++;
    }
    public void Reject()
    {
        if (Status != ReviewStatus.PROPOSED) throw new InvalidOperationException("Only proposed candidates can be rejected.");
        Status = ReviewStatus.REJECTED; ReviewedAt = DateTime.UtcNow; Revision++;
    }
    public void MoveTo(IntelligenceEntity target)
    {
        if (Status != ReviewStatus.APPROVED || target.InvestigationId != InvestigationId) throw new InvalidOperationException("Invalid mention reassignment.");
        EntityId = target.Id; Revision++;
    }
    public MentionResponse ToResponse(string sourceTitle) => new(Id, SourceId, sourceTitle, SnapshotId, Text, OriginalType,
        Start, End, Status, EntityId, ModelName, ModelVersion);
}
