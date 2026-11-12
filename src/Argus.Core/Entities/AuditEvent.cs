namespace Argus.Core.Entities;

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvestigationId { get; set; }
    public string RecordType { get; set; } = "Source";
    public Guid RecordId { get; set; }
    public string Action { get; set; } = "";
    public string? BeforeJson { get; set; }
    public string AfterJson { get; set; } = "";
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = "Local application user";
}
