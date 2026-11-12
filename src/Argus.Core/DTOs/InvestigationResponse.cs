using Argus.Core.Enums;
namespace Argus.Core.DTOs;

public sealed record InvestigationResponse(Guid Id, string Title, string Description,
    InvestigationStatus Status, DateTime CreatedAt, DateTime UpdatedAt,
    DateOnly? StartDate, DateOnly? EndDate, string? Region);
