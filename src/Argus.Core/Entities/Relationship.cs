using System.ComponentModel.DataAnnotations;
using Argus.Core.DTOs;
using Argus.Core.Enums;

namespace Argus.Core.Entities;

public sealed class Relationship
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid InvestigationId { get; private set; }
    public Guid SourceEntityId { get; private set; }
    public Guid TargetEntityId { get; private set; }
    public RelationshipType Type { get; private set; } = RelationshipType.MENTIONED_WITH;
    public ReviewStatus Status { get; private set; } = ReviewStatus.PROPOSED;
    public string? CustomLabel { get; private set; }
    public string Notes { get; private set; } = "";
    public string Origin { get; private set; } = "sentence_cooccurrence_v1";
    public string? RetiredReason { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public int Revision { get; private set; }
    private Relationship() { }
    public Relationship(Guid investigationId, Guid source, Guid target)
    {
        if (source == target) throw new InvalidOperationException("A relationship requires different entities.");
        InvestigationId = investigationId;
        (SourceEntityId, TargetEntityId) = Pair(source, target);
    }
    public void Approve(RelationshipReviewRequest request)
    {
        Validator.ValidateObject(request, new(request), true);
        if (RetiredReason is not null || Status == ReviewStatus.REJECTED) throw new InvalidOperationException("This relationship cannot be approved.");
        if (Status == ReviewStatus.APPROVED && (request.Type != Type || request.Reverse || request.CustomLabel != CustomLabel))
            throw new InvalidOperationException("An approved relationship's classification/direction cannot be silently replaced.");
        if (request.Reverse) (SourceEntityId, TargetEntityId) = (TargetEntityId, SourceEntityId);
        Type = request.Type;
        if (!Directed(Type)) (SourceEntityId, TargetEntityId) = Pair(SourceEntityId, TargetEntityId);
        CustomLabel = Type == RelationshipType.CUSTOM ? request.CustomLabel!.Trim() : null;
        Notes = request.Notes.Trim(); Status = ReviewStatus.APPROVED; Revision++;
    }
    public void Reject()
    {
        if (Status != ReviewStatus.PROPOSED || RetiredReason is not null) throw new InvalidOperationException("Only active proposals can be rejected.");
        Status = ReviewStatus.REJECTED; Revision++;
    }
    public void RedirectEntity(Guid former, Guid target)
    {
        if (SourceEntityId == former) SourceEntityId = target;
        if (TargetEntityId == former) TargetEntityId = target;
        if (!Directed(Type)) (SourceEntityId, TargetEntityId) = Pair(SourceEntityId, TargetEntityId);
        if (SourceEntityId == TargetEntityId) RetiredReason = "Both endpoints were merged into the same entity.";
        Revision++;
    }
    public void Touch() => Revision++;
    public static bool Directed(RelationshipType type) => type is RelationshipType.MEMBER_OF or RelationshipType.LOCATED_IN or RelationshipType.PARTICIPATED_IN or RelationshipType.CUSTOM;
    public static (Guid Source, Guid Target) Pair(Guid first, Guid second) =>
        string.CompareOrdinal(first.ToString("N"), second.ToString("N")) <= 0 ? (first, second) : (second, first);
}
