using Argus.Core.Services;

namespace Argus.Core.Entities;

public sealed class EventEvidence
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid SnapshotId { get; private set; }
    public int Start { get; private set; }
    public int End { get; private set; }
    public bool Active { get; private set; } = true;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    private EventEvidence() { }
    public EventEvidence(Guid eventId, SourceTextSnapshot snapshot, int start, int end)
    { TemporalRules.Citation(snapshot.Text, start, end); EventId = eventId; SnapshotId = snapshot.Id; SourceId = snapshot.SourceId; Start = start; End = end; }
    public void SetActive(bool active) => Active = active;
}
public sealed class LocationEvidence
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid LocationId { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid SnapshotId { get; private set; }
    public int Start { get; private set; }
    public int End { get; private set; }
    public bool Active { get; private set; } = true;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    private LocationEvidence() { }
    public LocationEvidence(Guid locationId, SourceTextSnapshot snapshot, int start, int end)
    { TemporalRules.Citation(snapshot.Text, start, end); LocationId = locationId; SnapshotId = snapshot.Id; SourceId = snapshot.SourceId; Start = start; End = end; }
    public void SetActive(bool active) => Active = active;
}
