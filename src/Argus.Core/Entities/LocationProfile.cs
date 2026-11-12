using System.ComponentModel.DataAnnotations;
using Argus.Core.DTOs;
using Argus.Core.Enums;

namespace Argus.Core.Entities;

public sealed class LocationProfile
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid InvestigationId { get; private set; }
    public Guid EntityId { get; private set; }
    public GeoPrecision Precision { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public double? UncertaintyMeters { get; private set; }
    public CoordinateBasis Basis { get; private set; }
    public string Notes { get; private set; } = "";
    public ReviewStatus Status { get; private set; } = ReviewStatus.PROPOSED;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; private set; }
    public int Revision { get; private set; }
    private LocationProfile() { }
    public LocationProfile(Guid investigationId, LocationEditRequest request) { InvestigationId = investigationId; Update(request); }
    public void Update(LocationEditRequest request)
    {
        Validator.ValidateObject(request, new(request), true);
        if (DeletedAt.HasValue) throw new InvalidOperationException("This location was removed.");
        if (request.Precision == GeoPrecision.UNKNOWN)
        {
            if (request.Latitude.HasValue || request.Longitude.HasValue || request.UncertaintyMeters.HasValue)
                throw new ValidationException("Unknown locations must not assert coordinates or a radius.");
        }
        else if (!request.ConfirmCoordinates || !request.Latitude.HasValue || !request.Longitude.HasValue ||
            !double.IsFinite(request.Latitude.Value) || !double.IsFinite(request.Longitude.Value) ||
            (request.Precision == GeoPrecision.APPROXIMATE && !(request.UncertaintyMeters > 0)) ||
            (request.Precision == GeoPrecision.EXACT && request.UncertaintyMeters is > 0) || string.IsNullOrWhiteSpace(request.Notes))
            throw new ValidationException("Confirm selected/verified coordinates and explain their basis; approximate locations require an uncertainty radius.");
        EntityId = request.EntityId; Precision = request.Precision; Latitude = request.Latitude; Longitude = request.Longitude;
        UncertaintyMeters = request.UncertaintyMeters; Basis = request.Basis; Notes = request.Notes.Trim(); Touch();
    }
    public void Review(bool approve, bool hasEvidence)
    {
        if (DeletedAt.HasValue || (approve && !hasEvidence)) throw new InvalidOperationException("Approval requires an active location with a source citation.");
        Status = approve ? ReviewStatus.APPROVED : ReviewStatus.REJECTED; Revision++;
    }
    public void Touch() { Status = ReviewStatus.PROPOSED; Revision++; }
    public void Redirect(Guid entityId) { EntityId = entityId; Revision++; }
    public void Remove() { DeletedAt = DateTime.UtcNow; Revision++; }
}
public sealed class GeographicFocus
{
    public Guid InvestigationId { get; private set; }
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public int Zoom { get; private set; }
    private GeographicFocus() { }
    public GeographicFocus(Guid id, MapFocusRequest request) { InvestigationId = id; Update(request); }
    public void Update(MapFocusRequest request)
    {
        if (!double.IsFinite(request.Latitude) || !double.IsFinite(request.Longitude) || request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180 || request.Zoom is < 1 or > 18)
            throw new ValidationException("Geographic focus is outside valid coordinate/zoom bounds.");
        Latitude = request.Latitude; Longitude = request.Longitude; Zoom = request.Zoom;
    }
}
