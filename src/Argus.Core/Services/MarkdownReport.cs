using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Argus.Core.DTOs;
using Argus.Core.Enums;

namespace Argus.Core.Services;

public sealed record ReportSource(Guid Id, string Title, string? Publisher, SourceReliability Reliability, DateOnly? PublishedAt,
    DateTime RetrievedAt, string ContentHash, string? OriginalUrl);
public sealed record ReportEntity(Guid Id, string Name, EntityType Type, IReadOnlyList<CitationResponse> Evidence);
public sealed record ReportMaterial(InvestigationResponse Investigation, IReadOnlyList<ClaimDetail> Claims, IReadOnlyList<ReportEntity> Entities,
    IReadOnlyList<EventResponse> Events, IReadOnlyList<LocationResponse> Locations, IReadOnlyList<ReportSource> Sources,
    IReadOnlyList<CitationResponse> Passages, ReportValidation Validation);

public static class MarkdownReport
{
    public static string Text(string value) => Regex.Replace(WebUtility.HtmlEncode(Regex.Replace(value, @"[\r\n\t]+", " ")),
        @"([\\`*_{}\[\]()#+.!|>~\-])", @"\$1");
    public static string Render(ReportMaterial material, Guid reportId, DateTime generatedAt)
    {
        if (!material.Validation.IsValid || material.Claims.Count == 0 || material.Claims.Any(x => x.Claim.Status != ReviewStatus.APPROVED))
            throw new InvalidOperationException("Only validated, approved findings may be exported.");
        var output = new StringBuilder();
        var refs = material.Passages.Select((x, i) => (Key: (x.SnapshotId, x.Start, x.End), Number: i + 1)).ToDictionary(x => x.Key, x => x.Number);
        string References(IEnumerable<CitationResponse> evidence) => string.Join(" ", evidence.Select(x => refs[(x.SnapshotId, x.Start, x.End)]).Distinct().Order().Select(x => $"[P{x}](#evidence-{x})"));
        string ClaimReferences(ClaimDetail claim) => References(claim.Evidence.Select(x => new CitationResponse(x.Id, x.SnapshotId, x.SourceId, x.SourceTitle, x.TextHash, x.Start, x.End, x.Quote)));
        void Line(string value = "") => output.Append(value).Append('\n');
        void Section(string name) { Line(); Line("## " + name); Line(); }
        var inv = material.Investigation;
        Line("# " + Text(inv.Title)); Line();
        Line($"Generated (UTC): {generatedAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}  ");
        Line($"Report ID: {reportId}  "); Line($"Investigation ID: {inv.Id}"); Line();
        Line("Analyst-reviewed assessment snapshot. Approval records analyst review; it does not establish independent verification or a probability of truth.");
        Section("Executive Summary");
        Line($"This report contains {material.Claims.Count} approved analytical finding(s), citing {material.Sources.Count} preserved source(s). Confidence is selected by the analyst."); Line();
        foreach (var claim in material.Claims) Line($"- {Text(claim.Claim.Statement)} {ClaimReferences(claim)}");
        Section("Scope and Research Questions");
        Line("Analyst-provided scope: " + (string.IsNullOrWhiteSpace(inv.Description) ? "No research questions or scope description were supplied." : Text(inv.Description)));
        Line(); Line("Analyst-defined region: " + Text(inv.Region ?? "Unspecified"));
        Line($"Research dates: {inv.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Unknown"} to {inv.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Unknown"}.");
        Section("Key Findings");
        for (var i = 0; i < material.Claims.Count; i++)
        {
            var claim = material.Claims[i]; Line($"### Finding {i + 1}"); Line();
            Line($"Analyst claim: {Text(claim.Claim.Statement)} {ClaimReferences(claim)}"); Line();
            Line($"Analyst confidence: **{claim.Claim.ConfidenceLevel}** (not a computed probability)."); Line();
            Line("Analyst interpretation and reasoning: " + Text(claim.Reasoning)); Line();
            foreach (var evidence in claim.Evidence)
            {
                var reference = refs[(evidence.SnapshotId, evidence.Start, evidence.End)];
                Line($"- {evidence.Relationship}: [P{reference}](#evidence-{reference})" + (evidence.Notes.Length > 0 ? " — Analyst evidence note: " + Text(evidence.Notes) : ""));
            }
            Line(); Line($"Claim ID: {claim.Claim.Id}; approved revision: {claim.Claim.Revision}."); Line();
        }
        Section("Relevant Entities");
        if (material.Entities.Count == 0) Line("No approved entity mentions are available; no identities are inferred.");
        foreach (var entity in material.Entities) Line($"- Analyst-reviewed canonical name: {Text(entity.Name)} ({entity.Type}). {References(entity.Evidence)} Entity ID: {entity.Id}.");
        Section("Chronological Events");
        if (material.Events.Count == 0) Line("No approved events are available. Temporal information is insufficient.");
        foreach (var item in material.Events)
        { Line($"- {TemporalDisplay.Date(item)} — {Text(item.Title)} ({item.Type}). {References(item.Evidence)}"); if (item.Description.Length > 0) Line("  Analyst description: " + Text(item.Description)); }
        Section("Geographic Context");
        if (material.Locations.Count == 0) Line("No approved geographic profiles are available; coordinates are not inferred.");
        foreach (var item in material.Locations)
        {
            var coordinate = item.Precision == GeoPrecision.UNKNOWN ? "Coordinates unknown" :
                $"Selected/verified coordinates: {item.Latitude!.Value.ToString("0.######", CultureInfo.InvariantCulture)}, {item.Longitude!.Value.ToString("0.######", CultureInfo.InvariantCulture)}; {item.Precision}";
            if (item.Precision == GeoPrecision.APPROXIMATE) coordinate += $"; uncertainty radius {item.UncertaintyMeters!.Value.ToString("0.##", CultureInfo.InvariantCulture)} metres";
            Line($"- {Text(item.Name)}: {coordinate}; basis {item.Basis}. {References(item.Evidence)}");
            if (item.Notes.Length > 0) Line("  Analyst geographic note: " + Text(item.Notes));
        }
        Section("Source Evaluation");
        Line("Reliability grades A–E are analyst assessments of sources at generation time; they are not confirmation of the claims."); Line();
        foreach (var source in material.Sources)
        { Line($"- {Text(source.Title)} — manual reliability {source.Reliability}; publisher {Text(source.Publisher ?? "Unknown")}; publication date {source.PublishedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Unknown"}. Source ID: {source.Id}."); }
        Section("Conflicting Information");
        var conflicts = material.Claims.Where(x => x.Evidence.Any(e => e.Relationship == EvidenceRelationship.CONTRADICTS)).ToList();
        if (conflicts.Count == 0) Line("No contradictory passages are attached to the included claims. This does not establish that no contradictory information exists.");
        foreach (var claim in conflicts)
        {
            Line($"- {Text(claim.Claim.Statement)} — {(claim.ContradictionsResolved ? "Addressed by analyst" : "UNRESOLVED CONTRADICTION")}. {ClaimReferences(claim)}");
            Line("  Analyst explanation: " + Text(claim.ContradictionsResolved ? claim.ContradictionResolution : "Unresolved; see reasoning and contradictory evidence. No resolution is inferred."));
        }
        Section("Analytical Limitations");
        Line("- Findings and source grades reflect local analyst review, not independent verification.");
        Line("- Structural associations alone do not demonstrate intent, wrongdoing or threat.");
        Line("- Unknown dates and coordinates remain unknown; partial dates retain their precision.");
        Line("- A preserved quotation shows what a source states, not that its statement is true.");
        Line("- This immutable snapshot reflects approvals and metadata at generation time. Later corrections do not rewrite this report.");
        foreach (var warning in material.Validation.Warnings) Line("- " + Text(warning));
        Section("References");
        for (var i = 0; i < material.Passages.Count; i++)
        {
            var passage = material.Passages[i]; var source = material.Sources.Single(x => x.Id == passage.SourceId);
            Line($"<a id=\"evidence-{i + 1}\"></a>"); Line(); Line($"### P{i + 1}: {Text(source.Title)}"); Line();
            Line($"Source record: /sources/{source.Id}  "); Line($"Source ID: {source.Id}  "); Line($"Original SHA-256: {source.ContentHash}  ");
            Line($"Snapshot ID: {passage.SnapshotId}  "); Line($"Snapshot SHA-256: {passage.TextHash}  "); Line($"UTF-16 passage: [{passage.Start}, {passage.End})  ");
            Line($"Retrieved (UTC): {source.RetrievedAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}");
            if (source.OriginalUrl is not null) Line("Attributed URL (not fetched): " + Text(source.OriginalUrl));
            Line(); foreach (var line in passage.Quote.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')) Line("> " + Text(line) + "  "); Line();
        }
        return output.ToString();
    }
}
