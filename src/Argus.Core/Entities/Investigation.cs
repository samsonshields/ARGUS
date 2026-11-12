using Argus.Core.DTOs;
using Argus.Core.Enums;
namespace Argus.Core.Entities;

public sealed class Investigation
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Title { get; private set; } = "";
    public string Description { get; private set; } = "";
    public InvestigationStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public string? Region { get; private set; }
    public void Update(InvestigationRequest request)
    {
        System.ComponentModel.DataAnnotations.Validator.ValidateObject(request, new(request), true);
        Title = request.Title.Trim();
        Description = request.Description.Trim();
        Status = request.Status;
        StartDate = request.StartDate;
        EndDate = request.EndDate;
        Region = string.IsNullOrWhiteSpace(request.Region) ? null : request.Region.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
    public InvestigationResponse ToResponse() => new(Id, Title, Description, Status,
        CreatedAt, UpdatedAt, StartDate, EndDate, Region);
}
