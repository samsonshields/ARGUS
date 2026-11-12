using System.ComponentModel.DataAnnotations;
using Argus.Core.Enums;

namespace Argus.Core.DTOs;

public sealed record PageResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public sealed record NlpRequest(string ContractVersion, Guid SourceId, string TextHash, string Text);
public sealed record NlpCandidate(string Text, EntityType Type, int Start, int End, double? Confidence);
public sealed record NlpModel(string Name, string Version, string SpacyVersion);
public sealed record NlpResponse(string ContractVersion, Guid SourceId, string TextHash,
    string OffsetConvention, NlpModel Model, List<NlpCandidate> Entities);
public sealed record JobResponse(Guid Id, Guid SourceId, Guid InvestigationId, JobState State,
    int Attempts, string? ErrorCode, string? Error, DateTime CreatedAt, DateTime UpdatedAt,
    DateTime? NextAttemptAt, Guid? SnapshotId, int CandidateCount);
public sealed record MentionResponse(Guid Id, Guid SourceId, string SourceTitle, Guid SnapshotId,
    string Text, EntityType OriginalType, int Start, int End, ReviewStatus Status, Guid? EntityId,
    string ModelName, string ModelVersion, double? Confidence = null);
public sealed record MentionEvidence(MentionResponse Mention, string Before, string Quote, string After, string TextHash);
public sealed record EntityResponse(Guid Id, Guid InvestigationId, string Name, EntityType Type,
    IReadOnlyList<string> Aliases, int MentionCount, Guid? MergedIntoId);
public sealed record EntitySuggestion(EntityResponse Entity, double Similarity);
public sealed record IntelligenceStatistics(int Proposed, int ApprovedEntities, int PendingJobs, int FailedJobs);

public class EntityEditRequest
{
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [EnumDataType(typeof(EntityType))] public EntityType Type { get; set; }
}
public sealed class ApproveMentionRequest : EntityEditRequest
{
    public Guid? EntityId { get; set; }
}
public sealed class MergeEntitiesRequest
{
    public Guid SourceId { get; set; }
    public Guid TargetId { get; set; }
    public bool Confirm { get; set; }
}
