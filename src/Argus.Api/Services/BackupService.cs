using Argus.Core.Exceptions;
using Argus.Infrastructure.Storage;

namespace Argus.Api.Services;

public sealed class BackupService(ApplicationDataRoot root, DocumentStorageRoot documents, ILogger<BackupService> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<(FileStream Stream, string FileName)> ExportAsync(CancellationToken ct)
    {
        if (!await gate.WaitAsync(0, ct)) throw new SourceException(409, "Another backup is being prepared. Wait for it to finish.");
        var temp = System.IO.Path.Combine(root.Path, "exports");
        var path = System.IO.Path.Combine(temp, Guid.NewGuid().ToString("N") + ".zip");
        var handedOff = false;
        try
        {
            Directory.CreateDirectory(temp);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(2));
            var manifest = await BackupArchive.CreateAsync(System.IO.Path.Combine(root.Path, "argus.db"), documents.Path, path, deadline.Token);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
            logger.LogInformation("Backup {BackupId} prepared with {FileCount} files", manifest.Id, manifest.Files.Count);
            handedOff = true;
            return (stream, $"argus-backup-{manifest.Id:N}.zip");
        }
        catch (InvalidDataException ex) { throw new SourceException(422, ex.Message); }
        catch (IOException) { throw new SourceException(422, "Backup could not read the workspace originals or write its temporary archive. Check storage and permissions."); }
        catch (Microsoft.Data.Sqlite.SqliteException) { throw new SourceException(422, "Backup database validation failed. Check the workspace database."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new SourceException(422, "Backup preparation exceeded its two-minute deadline."); }
        finally
        {
            // Successful streams own deletion; failures leave no partial archive.
            if (!handedOff && File.Exists(path)) File.Delete(path);
            gate.Release();
        }
    }
}
