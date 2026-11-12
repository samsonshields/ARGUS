using System.ComponentModel.DataAnnotations;
using Argus.Core.Enums;

namespace Argus.Core.DTOs;

public sealed class EventEditRequest
{
    [Required, StringLength(200, MinimumLength = 1)] public string Title { get; set; } = "";
    [StringLength(5000)] public string Description { get; set; } = "";
    [EnumDataType(typeof(DatePrecision))] public DatePrecision DatePrecision { get; set; } = DatePrecision.UNKNOWN;
    [StringLength(10)] public string? DateValue { get; set; }
    [StringLength(10)] public string? EndDateValue { get; set; }
    [EnumDataType(typeof(EventType))] public EventType Type { get; set; }
    public Guid? LocationId { get; set; }
    [MaxLength(100)] public List<Guid> EntityIds { get; set; } = [];
    public int Revision { get; set; }
}
public sealed class LocationEditRequest
{
    public Guid EntityId { get; set; }
    [EnumDataType(typeof(GeoPrecision))] public GeoPrecision Precision { get; set; } = GeoPrecision.UNKNOWN;
    [Range(-90, 90)] public double? Latitude { get; set; }
    [Range(-180, 180)] public double? Longitude { get; set; }
    [Range(0, 20000000)] public double? UncertaintyMeters { get; set; }
    [EnumDataType(typeof(CoordinateBasis))] public CoordinateBasis Basis { get; set; }
    [StringLength(5000)] public string Notes { get; set; } = "";
    public bool ConfirmCoordinates { get; set; }
    public int Revision { get; set; }
}
public sealed record CitationRequest(Guid SnapshotId, int Start, int End, int Revision);
public sealed record ReviewTemporalRequest(bool Confirm, int Revision);
public sealed record PassageResponse(Guid SnapshotId, Guid SourceId, string SourceTitle, string TextHash, int Start, int End, string Quote);
public sealed record CitationResponse(Guid Id, Guid SnapshotId, Guid SourceId, string SourceTitle, string TextHash, int Start, int End, string Quote);
public sealed record EventResponse(Guid Id, Guid InvestigationId, string Title, string Description, DateOnly? StartAt, DateOnly? EndAt,
    DatePrecision DatePrecision, EventType Type, ReviewStatus Status, Guid? LocationId, List<Guid> EntityIds,
    string? OriginalDateText, string? Interpretation, int Revision, List<CitationResponse> Evidence);
public sealed record LocationResponse(Guid Id, Guid InvestigationId, Guid EntityId, string Name, GeoPrecision Precision,
    double? Latitude, double? Longitude, double? UncertaintyMeters, CoordinateBasis Basis, string Notes, ReviewStatus Status,
    int Revision, List<CitationResponse> Evidence);
public sealed record MapFocusRequest([Range(-90, 90)] double Latitude, [Range(-180, 180)] double Longitude,
    [Range(1, 18)] int Zoom);
public sealed record MapResponse(List<LocationResponse> Locations, List<EventResponse> Events, MapFocusRequest? Focus);
public sealed record EventCandidate(string DateText, int Start, int End, int SentenceStart, int SentenceEnd, string Quote,
    DatePrecision Precision, string? DateValue, string Reason, double? Confidence);
public sealed record EventProcessingResponse(string ContractVersion, Guid SourceId, string TextHash, string OffsetConvention,
    string Method, List<EventCandidate> Events);
