using System.ComponentModel.DataAnnotations;
using Argus.Core.Enums;

namespace Argus.Core.DTOs;

public sealed class ClaimEditRequest
{
    [Required, StringLength(2000, MinimumLength = 1)] public string Statement { get; set; } = "";
    [Required(AllowEmptyStrings = true), StringLength(10000)] public string Reasoning { get; set; } = "";
    [EnumDataType(typeof(ConfidenceLevel))] public ConfidenceLevel ConfidenceLevel { get; set; } = ConfidenceLevel.INSUFFICIENT_INFORMATION;
    public bool ContradictionsResolved { get; set; }
    [Required(AllowEmptyStrings = true), StringLength(5000)] public string ContradictionResolution { get; set; } = "";
    [Range(0, int.MaxValue)] public int Revision { get; set; }
}
public sealed class ClaimEvidenceRequest
{
    public Guid SnapshotId { get; set; }
    public int Start { get; set; }
    public int End { get; set; }
    [EnumDataType(typeof(EvidenceRelationship))] public EvidenceRelationship Relationship { get; set; }
    [Required(AllowEmptyStrings = true), StringLength(2000)] public string Notes { get; set; } = "";
    [Range(0, int.MaxValue)] public int Revision { get; set; }
}
public sealed record ClaimReviewRequest(bool Confirm, bool AcknowledgeUnresolvedContradictions, int Revision);
public sealed record ClaimSummary(Guid Id, Guid InvestigationId, string Statement, ConfidenceLevel ConfidenceLevel, ReviewStatus Status,
    int SupportingCount, int ContradictingCount, int ContextCount, bool UnresolvedContradictions, int Revision, DateTime UpdatedAt);
public sealed record ClaimEvidenceResponse(Guid Id, Guid SourceId, string SourceTitle, SourceReliability Reliability, Guid SnapshotId,
    string TextHash, int Start, int End, string Quote, EvidenceRelationship Relationship, string Notes);
public sealed record ClaimDetail(ClaimSummary Claim, string Reasoning, bool ContradictionsResolved, string ContradictionResolution,
    DateTime CreatedAt, IReadOnlyList<ClaimEvidenceResponse> Evidence);
public sealed record AssessmentSummary(int Proposed, int Approved, int Rejected, int UnresolvedContradictions, int ApprovedWithUnresolvedContradictions);
public sealed record ReportRequest(bool ConfirmApprovedContentReviewed);
public sealed record ReportValidation(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings,
    int ClaimCount, int EntityCount, int EventCount, int LocationCount, int SourceCount, int PassageCount);
public sealed record ReportSummary(Guid Id, Guid InvestigationId, DateTime GeneratedAt, string ContentHash, string FileName);
public sealed record ReportResponse(ReportSummary Report, string Markdown, ReportValidation Validation);
