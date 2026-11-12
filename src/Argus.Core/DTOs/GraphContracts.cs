using System.ComponentModel.DataAnnotations;
using Argus.Core.Enums;

namespace Argus.Core.DTOs;

public sealed record RelationshipMention(Guid Id, Guid EntityId, string Text, int Start, int End);
public sealed record RelationshipProcessingRequest(string ContractVersion, Guid SourceId, string TextHash,
    string Text, IReadOnlyList<RelationshipMention> Mentions);
public sealed record RelationshipCandidate(Guid SourceMentionId, Guid TargetMentionId, Guid SourceEntityId,
    Guid TargetEntityId, RelationshipType Type, int Start, int End, string Quote, double? Confidence);
public sealed record RelationshipProcessingResponse(string ContractVersion, Guid SourceId, string TextHash,
    string OffsetConvention, string Method, List<RelationshipCandidate> Relationships);
public sealed record RelationshipSummary(Guid Id, Guid InvestigationId, Guid SourceEntityId, string SourceName,
    Guid TargetEntityId, string TargetName, RelationshipType Type, ReviewStatus Status, string? CustomLabel,
    string Notes, string Origin, string? RetiredReason, int ApprovedEvidenceCount, int ProposedEvidenceCount);
public sealed record RelationshipEvidenceResponse(Guid Id, Guid SourceId, string SourceTitle, Guid SnapshotId,
    Guid SourceMentionId, Guid TargetMentionId, int Start, int End, string Quote, string TextHash,
    ReviewStatus Status, string Method);
public sealed record RelationshipDetail(RelationshipSummary Relationship, IReadOnlyList<RelationshipEvidenceResponse> Evidence,
    int EvidenceTotal, int EvidencePage, int EvidencePageSize);
public sealed record GraphNode(Guid Id, string Label, EntityType Type, int Degree, int ConnectionCount,
    double DegreeCentrality, double Betweenness, int Component);
public sealed record GraphEdge(Guid Id, Guid Source, Guid Target, RelationshipType Type, ReviewStatus Status,
    string Label, bool Directed, int EvidenceCount, int PendingEvidenceCount);
public sealed record GraphResponse(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges,
    int ComponentCount, bool IncludesProposed);
public sealed record GraphPath(bool Found, IReadOnlyList<Guid> NodeIds, IReadOnlyList<Guid> EdgeIds);
public sealed record GenerationQueueResult(IReadOnlyList<JobResponse> Jobs, int BusySources);

public class RelationshipReviewRequest : IValidatableObject
{
    [EnumDataType(typeof(RelationshipType))] public RelationshipType Type { get; set; } = RelationshipType.MENTIONED_WITH;
    [StringLength(100)] public string? CustomLabel { get; set; }
    [Required(AllowEmptyStrings = true), StringLength(2000)] public string Notes { get; set; } = "";
    public bool Reverse { get; set; }
    public bool ConfirmEvidenceReviewed { get; set; }
    [Required, MinLength(1), MaxLength(100)] public List<Guid> EvidenceIds { get; set; } = [];
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!ConfirmEvidenceReviewed) yield return new("Confirm that the selected evidence has been reviewed.", [nameof(ConfirmEvidenceReviewed)]);
        if (Type != RelationshipType.MENTIONED_WITH && string.IsNullOrWhiteSpace(Notes))
            yield return new("Substantive relationships require analyst reasoning.", [nameof(Notes)]);
        if (Type == RelationshipType.CUSTOM && string.IsNullOrWhiteSpace(CustomLabel))
            yield return new("A custom relationship needs a label.", [nameof(CustomLabel)]);
    }
}
