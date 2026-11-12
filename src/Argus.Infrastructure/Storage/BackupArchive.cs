using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Argus.Core.DTOs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Argus.Infrastructure.Data;

namespace Argus.Infrastructure.Storage;

public static partial class BackupArchive
{
    public const long MaxBytes = 512L * 1024 * 1024;
    public const int MaxDocuments = 5000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static async Task<BackupManifest> CreateAsync(string databasePath, string documentsPath, string archivePath, CancellationToken ct)
    {
        var snapshotPath = archivePath + ".db";
        try
        {
            await using (var source = Connection(databasePath))
            await using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = snapshotPath, Pooling = false }.ToString()))
            {
                await source.OpenAsync(ct); await target.OpenAsync(ct);
                using var command = source.CreateCommand(); command.CommandText = "SELECT page_count * page_size FROM pragma_page_count(), pragma_page_size()";
                if (Convert.ToInt64(await command.ExecuteScalarAsync(ct)) > MaxBytes) throw Invalid("Database exceeds the backup size limit.");
                // SQLite's online backup includes committed WAL pages without copying live database files.
                source.BackupDatabase(target);
                using var journal = target.CreateCommand(); journal.CommandText = "PRAGMA journal_mode=DELETE";
                await journal.ExecuteScalarAsync(ct);
            }
            var (migrations, originals) = await ReadDatabaseAsync(snapshotPath, ct);
            if (originals.Count > MaxDocuments || originals.Sum(x => x.Length) + new FileInfo(snapshotPath).Length > MaxBytes)
                throw Invalid("Backup exceeds 5000 documents or 512 MiB uncompressed.");
            var files = new List<BackupFile>();
            await using (var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                using var archive = new ZipArchive(output, ZipArchiveMode.Create, true);
                files.Add(await AddFileAsync(archive, "database/argus.db", snapshotPath, null, ct));
                foreach (var original in originals)
                {
                    var file = await AddFileAsync(archive, "documents/" + original.Path, System.IO.Path.Combine(documentsPath, original.Path), original, ct);
                    files.Add(file);
                }
                var manifest = new BackupManifest(1, Guid.NewGuid(), DateTime.UtcNow, migrations, files);
                var entry = archive.CreateEntry("manifest.json", CompressionLevel.Fastest);
                await using (var stream = entry.Open()) await JsonSerializer.SerializeAsync(stream, manifest, Json, ct);
                return manifest;
            }
        }
        finally
        {
            foreach (var temporary in new[] { snapshotPath, snapshotPath + "-wal", snapshotPath + "-shm" })
                if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    public static async Task<RestoreResult> RestoreAsync(string archivePath, string destination, CancellationToken ct)
    {
        destination = System.IO.Path.GetFullPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw Invalid("Restore requires a new destination that does not already exist.");
        var parent = Directory.GetParent(destination)?.FullName ?? throw Invalid("Choose a named restore directory.");
        Directory.CreateDirectory(parent);
        var staging = System.IO.Path.Combine(parent, ".argus-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            await using var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            if (input.Length > MaxBytes + 8 * 1024 * 1024) throw Invalid("Archive exceeds the backup size limit.");
            using var archive = new ZipArchive(input, ZipArchiveMode.Read);
            if (archive.Entries.Count is < 2 or > MaxDocuments + 2) throw Invalid("Unexpected archive entry count.");
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                if (!AllowedPath(entry.FullName) || !entries.TryAdd(entry.FullName, entry) || entry.Length < 0 || entry.Length > MaxBytes ||
                    ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw Invalid("Archive contains an unexpected, duplicate or unsafe entry.");
            }
            if (!entries.TryGetValue("manifest.json", out var manifestEntry) || manifestEntry.Length > 2 * 1024 * 1024) throw Invalid("A bounded manifest is required.");
            BackupManifest? manifest;
            try { await using var stream = manifestEntry.Open(); manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, Json, ct); }
            catch (JsonException) { throw Invalid("Backup manifest is invalid."); }
            if (manifest is null || manifest.FormatVersion != 1 || manifest.Id == Guid.Empty || manifest.CreatedAt.Kind != DateTimeKind.Utc ||
                manifest.Files is null || manifest.Migrations is null || manifest.Files.Count != entries.Count - 1 || manifest.Files.Count > MaxDocuments + 1 ||
                manifest.Files.Any(x => x is null) || manifest.Migrations.Count > 128 || manifest.Migrations.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200) ||
                manifest.Files.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Count ||
                !manifest.Files.Any(x => x.Path == "database/argus.db") || manifest.Files.Any(x => x.Path is null || x.Sha256 is null || !AllowedFile(x.Path) || x.Length < 0 || x.Length > MaxBytes || !HashPattern().IsMatch(x.Sha256)) ||
                manifest.Files.Sum(x => x.Length) > MaxBytes)
                throw Invalid("Backup manifest has invalid identities, paths or sizes.");
            foreach (var file in manifest.Files)
            {
                if (!entries.TryGetValue(file.Path, out var entry) || entry.Length != file.Length) throw Invalid("Backup entries do not match the manifest.");
                var relative = file.Path == "database/argus.db" ? "argus.db" : file.Path;
                var targetPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(staging, relative));
                if (!targetPath.StartsWith(staging + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw Invalid("Restore path escaped its staging directory.");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath)!);
                await using var source = entry.Open();
                await using var output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                var digest = await CopyHashAsync(source, output, file.Length, ct);
                if (digest != file.Sha256) throw Invalid("Backup checksum validation failed.");
            }
            var (migrations, originals) = await ReadDatabaseAsync(System.IO.Path.Combine(staging, "argus.db"), ct);
            if (!migrations.SequenceEqual(manifest.Migrations) || originals.Count != manifest.Files.Count - 1 || originals.Any(x =>
                    !manifest.Files.Any(f => f.Path == "documents/" + x.Path && f.Length == x.Length && f.Sha256 == x.Sha256)))
                throw Invalid("Database document references do not match the backup manifest.");
            Directory.CreateDirectory(System.IO.Path.Combine(staging, "documents"));
            Directory.Move(staging, destination); // Never replace an existing workspace.
            return new(manifest.Id, originals.Count, destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    private static async Task<(List<string> Migrations, List<BackupFile> Originals)> ReadDatabaseAsync(string path, CancellationToken ct)
    {
        await using var connection = Connection(path); await connection.OpenAsync(ct);
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA integrity_check";
        if (!string.Equals((string?)await command.ExecuteScalarAsync(ct), "ok", StringComparison.Ordinal)) throw Invalid("Database integrity check failed.");
        command.CommandText = "PRAGMA foreign_key_check";
        await using (var reader = await command.ExecuteReaderAsync(ct)) if (await reader.ReadAsync(ct)) throw Invalid("Database foreign-key check failed.");
        var migrations = new List<string>(); command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId";
        await using (var reader = await command.ExecuteReaderAsync(ct)) while (await reader.ReadAsync(ct)) migrations.Add(reader.GetString(0));
        if (migrations.Count == 0) throw Invalid("Backup database has no ARGUS migration history.");
        using (var context = new ArgusDbContext(new DbContextOptionsBuilder<ArgusDbContext>().UseSqlite(connection).Options))
            if (!context.Database.GetMigrations().Take(migrations.Count).SequenceEqual(migrations)) throw Invalid("Backup migration history is incompatible with this ARGUS version.");
        var originals = new List<BackupFile>(); command.CommandText = "SELECT Id, FilePath, FileSize, ContentHash FROM Sources ORDER BY FilePath";
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var key = reader.GetString(1); var length = reader.GetInt64(2); var hash = reader.GetString(3);
                if (!DocumentPattern().IsMatch(key) || !Guid.TryParse(reader.GetString(0), out var sourceId) || !key.StartsWith(sourceId.ToString("N"), StringComparison.Ordinal) ||
                    length <= 0 || length > 100L * 1024 * 1024 || !HashPattern().IsMatch(hash) || originals.Count >= MaxDocuments)
                    throw Invalid("Database contains invalid original document references or exceeds the document limit.");
                originals.Add(new(key, length, hash));
            }
        }
        return (migrations, originals);
    }
    private static SqliteConnection Connection(string path) => new(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
    private static async Task<BackupFile> AddFileAsync(ZipArchive archive, string entryName, string path, BackupFile? expected, CancellationToken ct)
    {
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (source.Length > MaxBytes || (expected is not null && source.Length != expected.Length)) throw Invalid("An original document has an unexpected size.");
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest); await using var target = entry.Open();
        var hash = await CopyHashAsync(source, target, source.Length, ct);
        if (expected is not null && hash != expected.Sha256) throw Invalid("An original document failed its SHA-256 check.");
        return new(entryName, source.Length, hash);
    }
    private static async Task<string> CopyHashAsync(Stream input, Stream output, long expectedLength, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[81920]; long total = 0; int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            total += read; if (total > expectedLength || total > MaxBytes) throw Invalid("Backup entry exceeded its declared size.");
            hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (total != expectedLength) throw Invalid("Backup entry is truncated.");
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private static bool AllowedPath(string path) => path == "manifest.json" || AllowedFile(path);
    private static bool AllowedFile(string path) => path == "database/argus.db" || (path.StartsWith("documents/", StringComparison.Ordinal) && DocumentPattern().IsMatch(path[10..]));
    private static InvalidDataException Invalid(string message) => new(message);
    [GeneratedRegex("^[a-f0-9]{32}\\.(txt|pdf)$", RegexOptions.CultureInvariant)] private static partial Regex DocumentPattern();
    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)] private static partial Regex HashPattern();
}
