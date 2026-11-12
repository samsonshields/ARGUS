using System.Security.Cryptography;
using System.Text;

namespace Argus.Core.Entities;

public sealed class SourceTextSnapshot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid SourceId { get; private set; }
    public string Text { get; private set; } = "";
    public string TextHash { get; private set; } = "";
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    private SourceTextSnapshot() { }
    public SourceTextSnapshot(Source source)
    {
        SourceId = source.Id; Text = source.ExtractedText; TextHash = Hash(Text);
    }
    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
