using System.ComponentModel.DataAnnotations;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Exceptions;
using Argus.Core.Services;

namespace Argus.UnitTests;

public sealed class TemporalTests
{
    [Theory]
    [InlineData(DatePrecision.EXACT, "2024-02-29", "2024-02-29", "2024-02-29")]
    [InlineData(DatePrecision.MONTH, "2024-02", "2024-02-01", "2024-02-29")]
    [InlineData(DatePrecision.YEAR, "2026", "2026-01-01", "2026-12-31")]
    public void PartialDatesAreIntervalsWithExplicitPrecision(DatePrecision precision, string value, string start, string end)
    { var dates = TemporalRules.Dates(precision, value, null); Assert.Equal(DateOnly.Parse(start), dates.Start); Assert.Equal(DateOnly.Parse(end), dates.End); }
    [Theory]
    [InlineData(DatePrecision.UNKNOWN, "2026-01-01", null)]
    [InlineData(DatePrecision.EXACT, "2026-02-30", null)]
    [InlineData(DatePrecision.MONTH, "2026-13", null)]
    [InlineData(DatePrecision.YEAR, "0000", null)]
    [InlineData(DatePrecision.MONTH, "2026-02", "2026-03-01")]
    [InlineData(DatePrecision.EXACT, "2026-02-01", "2026-01-01")]
    public void InvalidDatesDoNotBecomeFacts(DatePrecision precision, string value, string? end)
        => Assert.Throws<ValidationException>(() => TemporalRules.Dates(precision, value, end));
    [Fact] public void UnknownDatesHaveNoCalendarBounds() => Assert.Equal((null, null), TemporalRules.Dates(DatePrecision.UNKNOWN, null, null));
    [Fact]
    public void EditingReviewedEventRequiresAnotherReviewAndRetainsMachineProvenance()
    {
        var item = new AnalysisEvent(Guid.NewGuid(), new EventCandidate("yesterday", 0, 9, 0, 9, "yesterday", DatePrecision.UNKNOWN, null, "No reference date.", null), Guid.NewGuid(), "stable-key");
        Assert.Throws<InvalidOperationException>(() => item.Review(true, false)); item.Review(true, true);
        item.Update(new() { Title = "Reviewed title", DatePrecision = DatePrecision.MONTH, DateValue = "2026-02" });
        Assert.Equal(ReviewStatus.PROPOSED, item.Status); Assert.Equal("yesterday", item.OriginalDateText); Assert.Equal("stable-key", item.OriginKey);
        item.Remove(); Assert.Throws<InvalidOperationException>(() => item.Review(true, true));
    }
    [Theory]
    [InlineData(GeoPrecision.APPROXIMATE, null, 0, 0, true)]
    [InlineData(GeoPrecision.APPROXIMATE, 0.0, 0, 0, true)]
    [InlineData(GeoPrecision.EXACT, 100.0, 0, 0, true)]
    [InlineData(GeoPrecision.EXACT, null, 91, 0, true)]
    [InlineData(GeoPrecision.EXACT, null, 0, 181, true)]
    [InlineData(GeoPrecision.EXACT, null, 0, 0, false)]
    [InlineData(GeoPrecision.UNKNOWN, null, 0, 0, true)]
    public void CoordinatesRequireExplicitSelectionAndHonestPrecision(GeoPrecision precision, double? radius, double lat, double lon, bool confirm)
        => Assert.Throws<ValidationException>(() => new LocationProfile(Guid.NewGuid(), new() { EntityId = Guid.NewGuid(), Precision = precision, Latitude = lat, Longitude = lon, UncertaintyMeters = radius, Notes = "Test basis", ConfirmCoordinates = confirm }));
    [Fact]
    public void ApproximateLocationRetainsRadiusAndReviewIsResetOnEdit()
    {
        var request = new LocationEditRequest { EntityId = Guid.NewGuid(), Precision = GeoPrecision.APPROXIMATE, Latitude = 1, Longitude = 2, UncertaintyMeters = 50000, ConfirmCoordinates = true, Notes = "Selected region center; not a precise point." };
        var item = new LocationProfile(Guid.NewGuid(), request); item.Review(true, true); item.Update(request);
        Assert.Equal(ReviewStatus.PROPOSED, item.Status); Assert.Equal(50000, item.UncertaintyMeters);
    }
    [Fact]
    public void EventContractRejectsInventedDatesAndForgedEvidence()
    {
        var request = new NlpRequest("1", Guid.NewGuid(), "hash", "😀 Yesterday.");
        EventProcessingResponse Response(EventCandidate candidate) => new("1", request.SourceId, request.TextHash, "UTF16_CODE_UNITS", "date_mentions_v1", [candidate]);
        var relative = new EventCandidate("Yesterday", 3, 12, 0, 13, request.Text, DatePrecision.UNKNOWN, null, "No reference date", null);
        TemporalRules.Validate(request, Response(relative));
        Assert.Throws<ProcessingException>(() => TemporalRules.Validate(request, Response(relative with { Precision = DatePrecision.EXACT, DateValue = "2026-02-01" })));
        Assert.Throws<ProcessingException>(() => TemporalRules.Validate(request, Response(relative with { Start = 1 })));
        Assert.Throws<ProcessingException>(() => TemporalRules.Validate(request, Response(relative with { Quote = "Forged" })));
        var absolute = request with { Text = "2026-02-01" };
        Assert.Throws<ProcessingException>(() => TemporalRules.Validate(absolute, new("1", request.SourceId, "hash", "UTF16_CODE_UNITS", "date_mentions_v1", [new("2026-02-01", 0, 10, 0, 10, absolute.Text, DatePrecision.EXACT, "2026-02-02", "Explicit", null)])));
    }
}
