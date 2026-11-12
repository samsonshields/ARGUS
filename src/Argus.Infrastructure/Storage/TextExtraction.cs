using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Argus.Infrastructure.Storage;

public static class TextExtraction
{
    public const string Version = "local-text-v1/pdfpig-0.1.16";

    public static string Decode(byte[] bytes, int maxCharacters)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        // BOM-detected UTF-16/32 decoders must also reject malformed sequences.
        reader.Peek();
        var encoding = (Encoding)reader.CurrentEncoding.Clone();
        encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
        var preamble = reader.CurrentEncoding.GetPreamble();
        var offset = bytes.AsSpan().StartsWith(preamble) ? preamble.Length : 0;
        var text = encoding.GetString(bytes, offset, bytes.Length - offset);
        if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t' or '\f')))
            throw new InvalidDataException("The file contains binary control characters.");
        return Normalize(text, maxCharacters);
    }

    public static string Normalize(string text, int maxCharacters)
    {
        if (text.Length > maxCharacters) throw new InvalidDataException("Extracted text exceeds the configured character limit.");
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\uFEFF', ' ', '\n', '\t', '\f');
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("No extractable text was found. OCR is not supported.");
        return text;
    }

    // Invoked in an isolated API executable mode. The caller can kill this process on timeout.
    public static int RunPdfWorker(string[] args)
    {
        try
        {
            var maxPages = int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
            var maxCharacters = int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
            using var document = PdfDocument.Open(args[1], new ParsingOptions { UseLenientParsing = false, MaxStackDepth = 64 });
            if (document.NumberOfPages > maxPages) throw new InvalidDataException("PDF page limit exceeded.");
            var text = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                var pageText = ContentOrderTextExtractor.GetText(page);
                if ((long)text.Length + pageText.Length + 1 > maxCharacters) throw new InvalidDataException("PDF text limit exceeded.");
                text.AppendLine(pageText);
            }
            File.WriteAllText(args[2], Normalize(text.ToString(), maxCharacters), new UTF8Encoding(false));
            return 0;
        }
        catch
        {
            // Parser diagnostics can contain document contents, so return only a fixed error code.
            return 2;
        }
    }
}
