using System.ComponentModel.DataAnnotations;
using Argus.Core.Enums;
namespace Argus.Core.DTOs;

public sealed class InvestigationRequest : IValidatableObject
{
    [Required, StringLength(200)] public string Title { get; set; } = "";
    [Required(AllowEmptyStrings = true), StringLength(5000)] public string Description { get; set; } = "";
    [EnumDataType(typeof(InvestigationStatus))] public InvestigationStatus Status { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    [StringLength(200)] public string? Region { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (StartDate.HasValue && EndDate.HasValue && EndDate < StartDate)
            yield return new("End date must be on or after start date.", [nameof(EndDate)]);
    }
}
