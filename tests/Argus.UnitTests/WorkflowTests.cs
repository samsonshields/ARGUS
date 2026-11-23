using Argus.Core.DTOs;
using Argus.Core.Services;
namespace Argus.UnitTests;

public sealed class WorkflowTests
{
    private static WorkflowCounts Counts(int sources = 3, int mentions = 10, int proposedMentions = 0, int relationships = 0,
        int events = 0, int locations = 0, int claims = 0, int contradictions = 0, int reports = 1)
        => new(sources, mentions, new(proposedMentions, 5), new(relationships, 2), new(events, 1), new(locations, 1),
            new(claims, 1), contradictions, reports);

    [Fact]
    public void EmptyInvestigationStartsWithImport()
        => Assert.Equal(WorkflowStep.IMPORT_SOURCES, WorkflowRules.Summarize(Counts(sources: 0, mentions: 0, reports: 0)).NextStep);

    [Fact]
    public void SourcesWithoutMentionsAwaitExtraction()
        => Assert.Equal(WorkflowStep.AWAIT_EXTRACTION, WorkflowRules.Summarize(Counts(mentions: 0, claims: 4)).NextStep);

    [Fact]
    public void EarliestReviewQueueWinsAndCarriesItsCount()
    {
        var result = WorkflowRules.Summarize(Counts(relationships: 15, events: 2, claims: 1));
        Assert.Equal(WorkflowStep.REVIEW_RELATIONSHIPS, result.NextStep);
        Assert.Equal(15, result.NextStepCount);
        Assert.Equal(WorkflowStep.REVIEW_ENTITIES, WorkflowRules.Summarize(Counts(proposedMentions: 4, relationships: 15)).NextStep);
        Assert.Equal(WorkflowStep.REVIEW_LOCATIONS, WorkflowRules.Summarize(Counts(locations: 1, claims: 1)).NextStep);
        Assert.Equal(WorkflowStep.REVIEW_CLAIMS, WorkflowRules.Summarize(Counts(claims: 2)).NextStep);
    }

    [Fact]
    public void UnresolvedContradictionsNeverDemandAction()
    {
        var result = WorkflowRules.Summarize(Counts(contradictions: 2));
        Assert.Equal(WorkflowStep.UP_TO_DATE, result.NextStep);
        Assert.Equal(2, result.Counts.UnresolvedContradictions);
    }

    [Fact]
    public void ReviewedInvestigationWithoutReportSuggestsReport()
        => Assert.Equal(WorkflowStep.GENERATE_REPORT, WorkflowRules.Summarize(Counts(reports: 0)).NextStep);
}
