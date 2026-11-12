using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;
using Argus.Core.DTOs;
using Argus.Core.Enums;

namespace Argus.Core.Entities;

public sealed partial class IntelligenceEntity
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid InvestigationId { get; private set; }
    public string Name { get; private set; } = "";
    public string NormalizedName { get; private set; } = "";
    public EntityType Type { get; private set; }
    public string AliasesJson { get; private set; } = "[]";
    public Guid? MergedIntoId { get; private set; }
    public int Revision { get; private set; }
    private IntelligenceEntity() { }
    public IntelligenceEntity(Guid investigationId, EntityEditRequest request)
    {
        InvestigationId = investigationId; Update(request);
    }
    public void Update(EntityEditRequest request)
    {
        Validator.ValidateObject(request, new(request), true);
        if (Name.Length > 0 && Name != request.Name.Trim()) AddAlias(Name);
        Name = Whitespace().Replace(request.Name.Trim(), " "); NormalizedName = Normalize(Name); Type = request.Type; Revision++;
    }
    public IReadOnlyList<string> Aliases() => JsonSerializer.Deserialize<List<string>>(AliasesJson)!;
    public void AddAlias(string alias)
    {
        var aliases = Aliases().ToList();
        if (!aliases.Any(x => Normalize(x) == Normalize(alias))) aliases.Add(alias);
        AliasesJson = JsonSerializer.Serialize(aliases);
    }
    public void MergeInto(IntelligenceEntity target)
    {
        if (Id == target.Id || InvestigationId != target.InvestigationId || Type != target.Type || MergedIntoId.HasValue || target.MergedIntoId.HasValue)
            throw new InvalidOperationException("Merge requires distinct active entities of the same investigation and type.");
        target.AddAlias(Name);
        foreach (var alias in Aliases()) target.AddAlias(alias);
        MergedIntoId = target.Id;
        Revision++; target.Revision++;
    }
    public void TouchForAssociation() => Revision++;
    public static string Normalize(string name) => Whitespace().Replace(name.Trim(), " ").ToUpperInvariant();
    public EntityResponse ToResponse(int mentions) => new(Id, InvestigationId, Name, Type, Aliases(), mentions, MergedIntoId);
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)] private static partial Regex Whitespace();
}
