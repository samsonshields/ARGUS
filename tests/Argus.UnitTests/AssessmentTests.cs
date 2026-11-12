using System.ComponentModel.DataAnnotations;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Services;

namespace Argus.UnitTests;

public sealed class AssessmentTests
{
    [Theory]
    [InlineData(ConfidenceLevel.LOW)]
    [InlineData(ConfidenceLevel.MODERATE)]
    [InlineData(ConfidenceLevel.HIGH)]
    [InlineData(ConfidenceLevel.INSUFFICIENT_INFORMATION)]
    public void ConfidenceIsAnExplicitAnalystSelectionAndNeverApprovesClaim(ConfidenceLevel confidence)
    {
        var item = new Claim(Guid.NewGuid(), new() { Statement = "Fictional analytical proposition", ConfidenceLevel = confidence });
        Assert.Equal(confidence, item.ConfidenceLevel); Assert.Equal(ReviewStatus.PROPOSED, item.Status);
        Assert.Equal(DateTimeKind.Utc, item.CreatedAt.Kind);
    }
    [Fact]
    public void ApprovalRequiresSupportReasoningAndExplicitUnresolvedAcknowledgment()
    {
        var item = new Claim(Guid.NewGuid(), new() { Statement = "Fictional claim" });
        Assert.Throws<ValidationException>(() => item.Review(true, new(true, false, item.Revision), [EvidenceRelationship.SUPPORTS]));
        item.Update(new() { Statement = "Fictional claim", Reasoning = "Evidence is incomplete; dates conflict." });
        Assert.Throws<ValidationException>(() => item.Review(true, new(true, false, item.Revision), [EvidenceRelationship.CONTEXT]));
        Assert.Throws<ValidationException>(() => item.Review(true, new(false, true, item.Revision), [EvidenceRelationship.SUPPORTS]));
        Assert.Throws<ValidationException>(() => item.Review(true, new(true, false, item.Revision), [EvidenceRelationship.SUPPORTS, EvidenceRelationship.CONTRADICTS]));
        item.Review(true, new(true, true, item.Revision), [EvidenceRelationship.SUPPORTS, EvidenceRelationship.CONTRADICTS]);
        Assert.Equal(ReviewStatus.APPROVED, item.Status); Assert.False(item.ContradictionsResolved);
    }
    [Fact]
    public void EvidenceChangesClearResolutionAndApprovalButRetainManualConfidence()
    {
        var item = new Claim(Guid.NewGuid(), new()
        {
            Statement = "Fictional claim",
            Reasoning = "Compared both sources",
            ConfidenceLevel = ConfidenceLevel.MODERATE,
            ContradictionsResolved = true,
            ContradictionResolution = "Different local dates in the two reports."
        });
        item.Review(true, new(true, false, item.Revision), [EvidenceRelationship.SUPPORTS, EvidenceRelationship.CONTRADICTS]);
        var revision = item.Revision; item.EvidenceChanged();
        Assert.Equal(ReviewStatus.PROPOSED, item.Status); Assert.False(item.ContradictionsResolved); Assert.Empty(item.ContradictionResolution);
        Assert.Equal(ConfidenceLevel.MODERATE, item.ConfidenceLevel); Assert.True(item.Revision > revision);
    }
    [Fact]
    public void ResolutionRequiresAnExplanationAndRemovalRetainsContent()
    {
        Assert.Throws<ValidationException>(() => new Claim(Guid.NewGuid(), new() { Statement = "Claim", ContradictionsResolved = true }));
        var item = new Claim(Guid.NewGuid(), new() { Statement = "Retained proposition", Reasoning = "Retained reasoning" });
        item.Remove(); Assert.NotNull(item.DeletedAt); Assert.Equal("Retained proposition", item.Statement);
        Assert.Throws<ValidationException>(() => item.Update(new() { Statement = "Replacement" }));
    }
    [Theory]
    [InlineData("[link](javascript:alert(1))")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("\n## injected heading\n![image](https://example.test)")]
    [InlineData("`code` *bold* _emphasis_ \\ [label]")]
    public void MarkdownEscapingKeepsUserContentLiteral(string value)
    {
        var text = MarkdownReport.Text(value); Assert.DoesNotContain("<script>", text); Assert.DoesNotContain("\n", text);
        if (value.Contains('[')) Assert.Contains("\\[", text);
        if (value.Contains('`')) Assert.Contains("\\`", text);
    }
    [Fact]
    public void FormatterRejectsInvalidOrUnapprovedMaterials()
    {
        var inv = new Investigation(); inv.Update(new() { Title = "Fictional" });
        var validation = new ReportValidation(false, ["No approved claims"], [], 0, 0, 0, 0, 0, 0);
        var material = new ReportMaterial(inv.ToResponse(), [], [], [], [], [], [], validation);
        Assert.Throws<InvalidOperationException>(() => MarkdownReport.Render(material, Guid.NewGuid(), DateTime.UtcNow));
    }
}
