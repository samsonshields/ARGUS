using Argus.Core.DTOs;

namespace Argus.Core.Services;

public static class WorkflowRules
{
    /// <summary>
    /// Chooses the earliest unfinished stage. Review queues come before analysis because claims and reports
    /// should rest on reviewed material. Unresolved contradictions are deliberately not a step: an analyst may
    /// legitimately leave conflicting reports unresolved, so the count is reported but never demands action.
    /// </summary>
    public static WorkflowSummary Summarize(WorkflowCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var (step, count) = counts switch
        {
            { Sources: 0 } => (WorkflowStep.IMPORT_SOURCES, 0),
            { Mentions: 0 } => (WorkflowStep.AWAIT_EXTRACTION, 0),
            { Entities.Proposed: > 0 } => (WorkflowStep.REVIEW_ENTITIES, counts.Entities.Proposed),
            { Relationships.Proposed: > 0 } => (WorkflowStep.REVIEW_RELATIONSHIPS, counts.Relationships.Proposed),
            { Events.Proposed: > 0 } => (WorkflowStep.REVIEW_EVENTS, counts.Events.Proposed),
            { Locations.Proposed: > 0 } => (WorkflowStep.REVIEW_LOCATIONS, counts.Locations.Proposed),
            { Claims.Proposed: > 0 } => (WorkflowStep.REVIEW_CLAIMS, counts.Claims.Proposed),
            { Reports: 0 } => (WorkflowStep.GENERATE_REPORT, 0),
            _ => (WorkflowStep.UP_TO_DATE, 0)
        };
        return new(counts, step, count);
    }
}
