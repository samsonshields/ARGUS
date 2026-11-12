using System.ComponentModel.DataAnnotations;
using Argus.Core.DTOs;
using Argus.Core.Enums;

namespace Argus.Core.Entities;

public sealed class Claim
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid InvestigationId { get; private set; }
    public string Statement { get; private set; } = "";
    public ConfidenceLevel ConfidenceLevel { get; private set; } = ConfidenceLevel.INSUFFICIENT_INFORMATION;
    public ReviewStatus Status { get; private set; } = ReviewStatus.PROPOSED;
    public string Reasoning { get; private set; } = "";
    public bool ContradictionsResolved { get; private set; }
    public string ContradictionResolution { get; private set; } = "";
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; private set; }
    public int Revision { get; private set; }
    private Claim() { }
    public Claim(Guid investigationId, ClaimEditRequest request) { InvestigationId = investigationId; Update(request); }
    public void Update(ClaimEditRequest request)
    {
        Validator.ValidateObject(request, new(request), true);
        if (DeletedAt.HasValue) throw new ValidationException("This claim was removed.");
        if (request.ContradictionsResolved && string.IsNullOrWhiteSpace(request.ContradictionResolution))
            throw new ValidationException("Explain how contradictory evidence was addressed before marking it resolved.");
        if (!request.ContradictionsResolved && !string.IsNullOrWhiteSpace(request.ContradictionResolution))
            throw new ValidationException("Resolution notes require an explicit resolved decision; use reasoning for unresolved questions.");
        Statement = request.Statement.Trim(); ConfidenceLevel = request.ConfidenceLevel; Reasoning = request.Reasoning.Trim();
        ContradictionsResolved = request.ContradictionsResolved; ContradictionResolution = request.ContradictionResolution.Trim();
        Status = ReviewStatus.PROPOSED; Changed();
    }
    public void EvidenceChanged()
    { Status = ReviewStatus.PROPOSED; ContradictionsResolved = false; ContradictionResolution = ""; Changed(); }
    public void Review(bool approve, ClaimReviewRequest request, IEnumerable<EvidenceRelationship> relationships)
    {
        if (!request.Confirm || DeletedAt.HasValue) throw new ValidationException("Confirm review of this active claim and its evidence.");
        var kinds = relationships.ToList();
        if (approve && (!kinds.Contains(EvidenceRelationship.SUPPORTS) || string.IsNullOrWhiteSpace(Reasoning)))
            throw new ValidationException("Approval requires supporting evidence and explanatory analyst reasoning.");
        if (approve && kinds.Contains(EvidenceRelationship.CONTRADICTS) && !ContradictionsResolved && !request.AcknowledgeUnresolvedContradictions)
            throw new ValidationException("Explicitly acknowledge the unresolved contradictory evidence before approval.");
        Status = approve ? ReviewStatus.APPROVED : ReviewStatus.REJECTED; Changed();
    }
    public void Remove() { DeletedAt = DateTime.UtcNow; Changed(); }
    private void Changed() { UpdatedAt = DateTime.UtcNow; Revision++; }
}
