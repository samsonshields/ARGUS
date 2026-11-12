using System.Security.Cryptography;
using System.Text;
using Argus.Core.DTOs;
using Argus.Core.Enums;

namespace Argus.Core.Entities;

public sealed class RelationshipEvidence
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RelationshipId { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid SnapshotId { get; private set; }
    public Guid SourceMentionId { get; private set; }
    public Guid TargetMentionId { get; private set; }
    public int Start { get; private set; }
    public int End { get; private set; }
    public string OriginKey { get; private set; } = "";
    public string Method { get; private set; } = "sentence_cooccurrence_v1";
    public ReviewStatus Status { get; private set; } = ReviewStatus.PROPOSED;
    private RelationshipEvidence() { }
    public RelationshipEvidence(Relationship relationship, SourceTextSnapshot snapshot, RelationshipCandidate candidate)
    {
        RelationshipId = relationship.Id; SourceId = snapshot.SourceId; SnapshotId = snapshot.Id;
        SourceMentionId = candidate.SourceMentionId; TargetMentionId = candidate.TargetMentionId;
        Start = candidate.Start; End = candidate.End; OriginKey = Key(snapshot.Id, candidate);
    }
    public void Approve() => Status = ReviewStatus.APPROVED;
    public void Reject() { if (Status == ReviewStatus.PROPOSED) Status = ReviewStatus.REJECTED; }
    public static string Key(Guid snapshotId, RelationshipCandidate candidate)
    {
        var pair = Relationship.Pair(candidate.SourceMentionId, candidate.TargetMentionId);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{snapshotId:N}:{pair.Source:N}:{pair.Target:N}:{candidate.Start}:{candidate.End}:sentence_cooccurrence_v1")));
    }
}
