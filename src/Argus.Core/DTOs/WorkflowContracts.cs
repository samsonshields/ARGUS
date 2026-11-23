namespace Argus.Core.DTOs;

/// <summary>The single most useful next analytic step for an investigation, in workflow order.</summary>
public enum WorkflowStep
{
    IMPORT_SOURCES,
    AWAIT_EXTRACTION,
    REVIEW_ENTITIES,
    REVIEW_RELATIONSHIPS,
    REVIEW_EVENTS,
    REVIEW_LOCATIONS,
    REVIEW_CLAIMS,
    GENERATE_REPORT,
    UP_TO_DATE
}

/// <summary>Records awaiting analyst review and records the analyst approved. Rejected records are excluded.</summary>
public sealed record StageCounts(int Proposed, int Approved);

/// <summary>Review-state counts for one investigation. Retired, merged-away and deleted records are excluded.</summary>
public sealed record WorkflowCounts(int Sources, int Mentions, StageCounts Entities, StageCounts Relationships,
    StageCounts Events, StageCounts Locations, StageCounts Claims, int UnresolvedContradictions, int Reports);

/// <summary>Counts plus the next step and how many items that step covers (0 when the step is not a queue).</summary>
public sealed record WorkflowSummary(WorkflowCounts Counts, WorkflowStep NextStep, int NextStepCount);
