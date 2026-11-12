using System.Diagnostics;
using System.Globalization;
using Argus.Core.Interfaces;
using Argus.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Argus.Api.Services;

public sealed class DocumentExtractor(IOptions<DocumentOptions> options) : IDocumentExtractor, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<string> ExtractAsync(string path, bool pdf, CancellationToken ct)
    {
        var limits = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(limits.ExtractionTimeoutSeconds));
        await gate.WaitAsync(timeout.Token);
        try
        {
            if (!pdf) return TextExtraction.Decode(await File.ReadAllBytesAsync(path, timeout.Token), limits.MaxExtractedCharacters);
            var output = path + ".extract-" + Guid.NewGuid().ToString("N");
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
            // Only our own executable is launched; uploaded files are always data arguments.
            foreach (var arg in new[] { typeof(Program).Assembly.Location, "--extract-pdf", path, output,
                limits.MaxPdfPages.ToString(CultureInfo.InvariantCulture), limits.MaxExtractedCharacters.ToString(CultureInfo.InvariantCulture) })
                process.StartInfo.ArgumentList.Add(arg);
            process.StartInfo.Environment["DOTNET_GCHeapHardLimit"] = "10000000"; // 256 MiB managed heap
            try
            {
                process.Start();
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                    throw;
                }
                if (process.ExitCode != 0) throw new InvalidDataException("PDF extraction failed.");
                return TextExtraction.Normalize(await File.ReadAllTextAsync(output, timeout.Token), limits.MaxExtractedCharacters);
            }
            finally { File.Delete(output); }
        }
        finally { gate.Release(); }
    }

    public void Dispose() => gate.Dispose();
}
