using System.ComponentModel.DataAnnotations;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
namespace Argus.UnitTests;

public sealed class InvestigationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BlankTitleIsRejected(string? title)
    {
        var entity = new Investigation();
        Assert.Throws<ValidationException>(() => entity.Update(new() { Title = title! }));
    }
    [Fact]
    public void InvalidUpdateDoesNotMutateEntity()
    {
        var entity = new Investigation();
        entity.Update(new() { Title = "Original" });
        Assert.Throws<ValidationException>(() => entity.Update(new()
        {
            Title = "Replacement",
            StartDate = new(2026, 2, 2),
            EndDate = new(2026, 1, 1)
        }));
        Assert.Equal("Original", entity.Title);
    }
    [Theory]
    [InlineData(201, 0, 0)]
    [InlineData(1, 5001, 0)]
    [InlineData(1, 0, 201)]
    public void LengthLimitsAreEnforced(int title, int description, int region)
        => Assert.Throws<ValidationException>(() => new Investigation().Update(new()
        {
            Title = new('x', title),
            Description = new('x', description),
            Region = new('x', region)
        }));
    [Fact]
    public void InvalidStatusIsRejected()
        => Assert.Throws<ValidationException>(() => new Investigation().Update(new() { Title = "Test", Status = (InvestigationStatus)42 }));
    [Fact]
    public void MetadataIsNormalizedAndIdentityPreserved()
    {
        var entity = new Investigation();
        var id = entity.Id;
        var created = entity.CreatedAt;
        entity.Update(new() { Title = "  Test  ", Description = "  Scope  ", Region = "  ", Status = InvestigationStatus.Archived });
        Assert.Equal("Test", entity.Title);
        Assert.Equal("Scope", entity.Description);
        Assert.Null(entity.Region);
        Assert.Equal(id, entity.Id);
        Assert.Equal(created, entity.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, entity.UpdatedAt.Kind);
        Assert.Equal(InvestigationStatus.Archived, entity.Status);
    }
    [Fact]
    public void SameDayScopeAndUnknownDatesAreValid()
    {
        var entity = new Investigation();
        entity.Update(new() { Title = "Test", StartDate = new(2026, 1, 1), EndDate = new(2026, 1, 1) });
        entity.Update(new() { Title = "Test" });
        Assert.Null(entity.StartDate);
    }
}
