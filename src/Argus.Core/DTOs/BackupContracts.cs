namespace Argus.Core.DTOs;

public sealed record BackupFile(string Path, long Length, string Sha256);
public sealed record BackupManifest(int FormatVersion, Guid Id, DateTime CreatedAt, IReadOnlyList<string> Migrations, IReadOnlyList<BackupFile> Files);
public sealed record RestoreResult(Guid BackupId, int DocumentCount, string DataDirectory);
