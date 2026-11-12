using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Exceptions;
using Argus.Core.Services;

namespace Argus.UnitTests;

public sealed class IntelligenceTests
{
    private static Source Source() => Argus.Core.Entities.Source.Create(Guid.NewGuid(), SourceType.Text,
        new() { Title = "Fictional report" }, "report.txt", 10, new('a', 64));
    [Fact]
    public void JobTransitionsAreBoundedAndTerminal()
    {
        var job = new ProcessingJob(Source());
        Assert.Equal(JobState.PENDING, job.State);
        Assert.Throws<InvalidOperationException>(() => job.Complete(0));
        job.Start();
        Assert.Equal(1, job.Attempts);
        Assert.Throws<InvalidOperationException>(() => job.Start());
        job.Fail("offline", "Unavailable", true, 2, 0);
        Assert.Equal(JobState.PENDING, job.State);
        job.Start(); job.Recover(2);
        Assert.Equal(JobState.FAILED, job.State);
        Assert.Equal(2, job.Attempts);
        Assert.Throws<InvalidOperationException>(() => job.Cancel());
    }
    [Fact]
    public void CompletedAndCancelledJobsCannotRestart()
    {
        var completed = new ProcessingJob(Source());
        completed.Start(); completed.AttachSnapshot(Guid.NewGuid()); completed.Complete(0);
        Assert.Equal(JobState.COMPLETED, completed.State);
        Assert.Null(completed.Error);
        var cancelled = new ProcessingJob(Source());
        cancelled.Cancel();
        Assert.Throws<InvalidOperationException>(() => cancelled.Start());
        Assert.Throws<InvalidOperationException>(() => cancelled.Complete(1));
    }
    [Fact]
    public void NormalizationAliasesAndMergesPreserveIdentity()
    {
        var inv = Guid.NewGuid();
        var first = new IntelligenceEntity(inv, new() { Name = " Alice   Morgan ", Type = EntityType.PERSON });
        Assert.Equal("ALICE MORGAN", first.NormalizedName);
        first.Update(new() { Name = "Alice M. Morgan", Type = EntityType.PERSON });
        Assert.Contains("Alice Morgan", first.Aliases());
        var target = new IntelligenceEntity(inv, new() { Name = "A. Morgan", Type = EntityType.PERSON });
        first.MergeInto(target);
        Assert.Equal(target.Id, first.MergedIntoId);
        Assert.Contains("Alice M. Morgan", target.Aliases());
        Assert.Throws<InvalidOperationException>(() => first.MergeInto(target));
        Assert.Throws<InvalidOperationException>(() => target.MergeInto(target));
        var other = new IntelligenceEntity(Guid.NewGuid(), new() { Name = "A. Morgan", Type = EntityType.PERSON });
        Assert.Throws<InvalidOperationException>(() => target.MergeInto(other));
    }
    [Theory]
    [InlineData(" Alice   Morgan ", "alice morgan", 1.0)]
    [InlineData("ALICE", "ALISE", 0.8)]
    [InlineData("", "Alice", 0.0)]
    public void FuzzyMatchingIsDeterministic(string first, string second, double expected)
        => Assert.Equal(expected, EntityText.Similarity(first, second), precision: 5);

    [Theory]
    [InlineData("span")]
    [InlineData("surrogate")]
    [InlineData("confidence")]
    [InlineData("source")]
    [InlineData("hash")]
    [InlineData("type")]
    [InlineData("model")]
    public void InvalidNlpContractsAreRejectedBeforePersistence(string kind)
    {
        var request = new NlpRequest("1", Guid.NewGuid(), new('a', 64), "\U0001F600 Alice");
        var candidate = new NlpCandidate("Alice", EntityType.PERSON, 3, 8, null);
        var response = new NlpResponse("1", request.SourceId, request.TextHash, "UTF16_CODE_UNITS", new("test", "1", "3.8"), [candidate]);
        EntityText.Validate(request, response, 10);
        response = kind switch
        {
            "span" => response with { Entities = [candidate with { End = 7 }] },
            "surrogate" => response with { Entities = [new("\uDE00", EntityType.OTHER, 1, 2, null)] },
            "confidence" => response with { Entities = [candidate with { Confidence = .99 }] },
            "source" => response with { SourceId = Guid.NewGuid() },
            "hash" => response with { TextHash = "wrong" },
            "type" => response with { Entities = [candidate with { Type = (EntityType)999 }] },
            _ => response with { Model = null! }
        };
        Assert.Throws<ProcessingException>(() => EntityText.Validate(request, response, 10));
    }
    [Fact]
    public void ReviewingMentionsDoesNotOverwriteMachineEvidence()
    {
        var source = Source(); source.CompleteExtraction("Alice", "test");
        var snapshot = new SourceTextSnapshot(source);
        var job = new ProcessingJob(source);
        var mention = new EntityMention(source, snapshot, job, new("Alice", EntityType.OTHER, 0, 5, null), new("test", "1", "3.8"));
        var entity = new IntelligenceEntity(source.InvestigationId, new() { Name = "Alice Morgan", Type = EntityType.PERSON });
        mention.Approve(entity);
        Assert.Equal("Alice", mention.Text);
        Assert.Equal(EntityType.OTHER, mention.OriginalType);
        Assert.Equal(ReviewStatus.APPROVED, mention.Status);
        Assert.Throws<InvalidOperationException>(() => mention.Reject());
    }
}
