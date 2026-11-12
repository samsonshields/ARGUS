using System.Security.Cryptography;
using System.Text;

namespace Argus.Core.Entities;

public sealed class GeneratedReport
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid InvestigationId { get; private set; }
    public DateTime GeneratedAt { get; private set; }
    public string Markdown { get; private set; } = "";
    public string ContentHash { get; private set; } = "";
    public string ValidationJson { get; private set; } = "";
    private GeneratedReport() { }
    public GeneratedReport(Guid id, Guid investigationId, DateTime generatedAt, string markdown, string validationJson)
    {
        Id = id; InvestigationId = investigationId; GeneratedAt = generatedAt; Markdown = markdown; ValidationJson = validationJson;
        ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(markdown))).ToLowerInvariant();
    }
}
