using System.Text.RegularExpressions;
using Argus.Core.Exceptions;

namespace Argus.Infrastructure.Storage;

public sealed partial class DocumentStorage(DocumentStorageRoot root)
{
    public string Resolve(string key)
    {
        if (!StorageKey().IsMatch(key)) throw new InvalidOperationException("Invalid document storage key.");
        return System.IO.Path.Combine(root.Path, key);
    }

    public async Task SaveAsync(string key, byte[] content, CancellationToken ct)
    {
        Directory.CreateDirectory(root.Path);
        var path = Resolve(key);
        var created = false;
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            created = true;
            await file.WriteAsync(content, ct);
        }
        catch
        {
            if (created) File.Delete(path);
            throw;
        }
    }

    public void Remove(string key) => File.Delete(Resolve(key));

    public static string SafeName(string name)
    {
        var leaf = name.Replace('\\', '/').Split('/').Last();
        leaf = new string(leaf.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or ' ').ToArray()).Trim(' ', '.');
        if (leaf.Length > 120) leaf = leaf[..100] + System.IO.Path.GetExtension(leaf);
        return string.IsNullOrWhiteSpace(leaf) ? "document.txt" : leaf;
    }

    public static bool IsPdf(string fileName)
    {
        var extension = System.IO.Path.GetExtension(fileName.Replace('\\', '/')).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => true,
            ".txt" => false,
            _ => throw new SourceException(415, "Only .txt and .pdf files are supported.")
        };
    }

    [GeneratedRegex("^[a-f0-9]{32}\\.(txt|pdf)$", RegexOptions.CultureInvariant)]
    private static partial Regex StorageKey();
}
