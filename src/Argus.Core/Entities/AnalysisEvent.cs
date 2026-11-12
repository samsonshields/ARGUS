using System.ComponentModel.DataAnnotations;
using Argus.Core.DTOs;
using Argus.Core.Enums;
using Argus.Core.Services;

namespace Argus.Core.Entities;

public sealed class AnalysisEvent
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid InvestigationId { get; private set; }
    public string Title { get; private set; } = "";
    public string Description { get; private set; } = "";
    public DateOnly? StartAt { get; private set; }
    public DateOnly? EndAt { get; private set; }
    public DatePrecision DatePrecision { get; private set; }
    public EventType Type { get; private set; }
    public ReviewStatus Status { get; private set; } = ReviewStatus.PROPOSED;
    public Guid? LocationId { get; private set; }
    public string? OriginKey { get; private set; }
    public string? OriginalDateText { get; private set; }
    public string? Interpretation { get; private set; }
    public Guid? ProcessingJobId { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; private set; }
    public int Revision { get; private set; }
    private AnalysisEvent() { }
    public AnalysisEvent(Guid investigationId, EventEditRequest request) { InvestigationId = investigationId; Update(request); }
    public AnalysisEvent(Guid investigationId, EventCandidate candidate, Guid jobId, string key) : this(investigationId,
        new() { Title = $"Date mention: {candidate.DateText}", DatePrecision = candidate.Precision, DateValue = candidate.DateValue })
    { OriginalDateText = candidate.DateText; Interpretation = candidate.Reason; ProcessingJobId = jobId; OriginKey = key; }
    public void Update(EventEditRequest request)
    {
        Validator.ValidateObject(request, new(request), true);
        if (DeletedAt.HasValue) throw new InvalidOperationException("This event was removed.");
        var dates = TemporalRules.Dates(request.DatePrecision, request.DateValue, request.EndDateValue);
        Title = request.Title.Trim(); Description = request.Description.Trim();
        StartAt = dates.Start; EndAt = dates.End; DatePrecision = request.DatePrecision; Type = request.Type;
        LocationId = request.LocationId; Status = ReviewStatus.PROPOSED; Revision++;
    }
    public void Review(bool approve, bool hasEvidence)
    {
        if (DeletedAt.HasValue || (approve && !hasEvidence)) throw new InvalidOperationException("Approval requires an active event with a source citation.");
        Status = approve ? ReviewStatus.APPROVED : ReviewStatus.REJECTED; Revision++;
    }
    public void Touch() { Status = ReviewStatus.PROPOSED; Revision++; }
    public void Remove() { DeletedAt = DateTime.UtcNow; Revision++; }
}
public sealed class EventEntity
{
    public Guid EventId { get; private set; }
    public Guid EntityId { get; private set; }
    public Guid InvestigationId { get; private set; }
    private EventEntity() { }
    public EventEntity(AnalysisEvent item, Guid entityId) { EventId = item.Id; InvestigationId = item.InvestigationId; EntityId = entityId; }
}
