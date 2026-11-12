using System.ComponentModel.DataAnnotations;
using Argus.Core.DTOs;
using Argus.Core.Enums;
using Argus.Core.Services;

namespace Argus.Core.Entities;

public sealed class ClaimEvidence
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ClaimId { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid SnapshotId { get; private set; }
    public int Start { get; private set; }
    public int End { get; private set; }
    public EvidenceRelationship Relationship { get; private set; }
    public string Notes { get; private set; } = "";
    public bool Active { get; private set; } = true;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    private ClaimEvidence() { }
    public ClaimEvidence(Guid claimId, SourceTextSnapshot snapshot, ClaimEvidenceRequest request)
    {
        TemporalRules.Citation(snapshot.Text, request.Start, request.End);
        ClaimId = claimId; SnapshotId = snapshot.Id; SourceId = snapshot.SourceId; Start = request.Start; End = request.End; Update(request);
    }
    public void Update(ClaimEvidenceRequest request)
    { Validator.ValidateObject(request, new(request), true); Relationship = request.Relationship; Notes = request.Notes.Trim(); Active = true; }
    public void Remove() => Active = false;
}
