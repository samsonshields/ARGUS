using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Argus.Core.DTOs;
using Argus.Core.Enums;
using Argus.Core.Exceptions;

namespace Argus.Core.Services;

public static class TemporalRules
{
    public static void Citation(string text, int start, int end)
    {
        if (start < 0 || end <= start || end > text.Length || end - start > 8000 || !EntityText.Boundary(text, start) ||
            !EntityText.Boundary(text, end) || string.IsNullOrWhiteSpace(text[start..end]))
            throw new ValidationException("Select a nonempty exact snapshot passage of at most 8000 UTF-16 units.");
    }
    public static (DateOnly? Start, DateOnly? End) Dates(DatePrecision precision, string? value, string? end)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value;
        end = string.IsNullOrWhiteSpace(end) ? null : end;
        if (!Enum.IsDefined(precision)) throw new ValidationException("Unknown date precision.");
        if (precision == DatePrecision.UNKNOWN)
        {
            if (value is not null || end is not null) throw new ValidationException("Unknown dates must not assert calendar values.");
            return (null, null);
        }
        var format = precision switch { DatePrecision.EXACT => "yyyy-MM-dd", DatePrecision.MONTH => "yyyy-MM", _ => "yyyy" };
        var fullValue = precision switch { DatePrecision.MONTH => value + "-01", DatePrecision.YEAR => value + "-01-01", _ => value };
        if (value?.Length != format.Length || !DateOnly.TryParseExact(fullValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            throw new ValidationException($"Enter a valid {format} date.");
        var finish = precision switch
        {
            DatePrecision.MONTH => new DateOnly(start.Year, start.Month, DateTime.DaysInMonth(start.Year, start.Month)),
            DatePrecision.YEAR => new DateOnly(start.Year, 12, 31),
            _ => start
        };
        if (end is not null)
        {
            if (precision != DatePrecision.EXACT || !DateOnly.TryParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out finish) || finish < start)
                throw new ValidationException("End date requires an exact date on or after the start.");
        }
        return (start, finish);
    }
    public static void Validate(NlpRequest request, EventProcessingResponse result)
    {
        void Invalid() => throw new ProcessingException("invalid_contract", "Event response failed source/date/span validation; no candidates were stored.");
        if (result.ContractVersion != "1" || result.SourceId != request.SourceId || result.TextHash != request.TextHash ||
            result.OffsetConvention != "UTF16_CODE_UNITS" || result.Method != "date_mentions_v1" || result.Events is null || result.Events.Count > 2000) Invalid();
        foreach (var item in result.Events!)
        {
            if (item is null || item.Start < 0 || item.End <= item.Start || item.End > request.Text.Length || item.SentenceStart < 0 ||
                item.SentenceEnd > request.Text.Length || item.SentenceStart > item.Start || item.SentenceEnd < item.End ||
                item.SentenceEnd - item.SentenceStart > 8000 || item.DateText is null || item.DateText.Length > 200 ||
                string.IsNullOrWhiteSpace(item.Reason) || item.Reason.Length > 2000 || item.Confidence is not null ||
                !EntityText.Boundary(request.Text, item.Start) || !EntityText.Boundary(request.Text, item.End) ||
                !EntityText.Boundary(request.Text, item.SentenceStart) || !EntityText.Boundary(request.Text, item.SentenceEnd)) { Invalid(); continue; }
            if (request.Text[item.Start..item.End] != item.DateText || request.Text[item.SentenceStart..item.SentenceEnd] != item.Quote) Invalid();
            try { Dates(item.Precision, item.DateValue, null); } catch (ValidationException) { Invalid(); }
            // Relative/ambiguous expressions are never trusted as absolute calendar dates.
            if (item.Precision != DatePrecision.UNKNOWN && !ExplicitMatches(item.DateText, item.Precision, item.DateValue)) Invalid();
        }
    }
    private static bool ExplicitMatches(string text, DatePrecision precision, string? value)
    {
        var cleaned = System.Text.RegularExpressions.Regex.Replace(text, @"(\d)(st|nd|rd|th)\b", "$1", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Replace(",", "");
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ");
        if (precision == DatePrecision.YEAR) return text == value;
        if (precision == DatePrecision.MONTH)
        {
            if (text == value) return true;
            return DateOnly.TryParseExact(cleaned + " 01", "MMMM yyyy dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)
                && month.ToString("yyyy-MM", CultureInfo.InvariantCulture) == value;
        }
        return DateOnly.TryParseExact(cleaned, ["yyyy-MM-dd", "MMMM d yyyy", "d MMMM yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact)
            && exact.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == value;
    }
}
