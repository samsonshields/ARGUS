using System.Globalization;
using Argus.Core.DTOs;
using Argus.Core.Enums;

namespace Argus.Core.Services;

public static class TemporalDisplay
{
    public static string Date(EventResponse item) => item.DatePrecision switch
    {
        DatePrecision.EXACT => item.StartAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + (item.EndAt != item.StartAt ? " to " + item.EndAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""),
        DatePrecision.MONTH => item.StartAt?.ToString("yyyy-MM", CultureInfo.InvariantCulture) + " (month only)",
        DatePrecision.YEAR => item.StartAt?.ToString("yyyy", CultureInfo.InvariantCulture) + " (year only)",
        _ => "Unknown date"
    } ?? "Unknown date";
    public static string? DateValue(EventResponse item) => item.DatePrecision switch
    { DatePrecision.EXACT => item.StartAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), DatePrecision.MONTH => item.StartAt?.ToString("yyyy-MM", CultureInfo.InvariantCulture), DatePrecision.YEAR => item.StartAt?.ToString("yyyy", CultureInfo.InvariantCulture), _ => null };
}
