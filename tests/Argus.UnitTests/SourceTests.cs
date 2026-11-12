using System.ComponentModel.DataAnnotations;
using System.Text;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Exceptions;
using Argus.Infrastructure.Storage;

namespace Argus.UnitTests;

public sealed class SourceTests
{
    [Theory]
    [InlineData("\r\n alpha\r\nbeta\r ", "alpha\nbeta")]
    [InlineData("\uFEFFhello", "hello")]
    public void TextNormalizationIsDeterministic(string input, string expected)
        => Assert.Equal(expected, TextExtraction.Normalize(input, 100));

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16")]
    [InlineData("utf32")]
    public void UnicodeTextIsDecodedWithoutChangingOriginal(string kind)
    {
        Encoding encoding = kind switch { "utf16" => Encoding.Unicode, "utf32" => Encoding.UTF32, _ => new UTF8Encoding(false, true) };
        var content = encoding.GetPreamble().Concat(encoding.GetBytes("Meridian \u03A9\r\nreport")).ToArray();
        var original = content.ToArray();
        Assert.Equal("Meridian \u03A9\nreport", TextExtraction.Decode(content, 100));
        Assert.Equal(original, content);
    }

    [Fact]
    public void BinaryInvalidEncodingEmptyAndOversizedTextAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => TextExtraction.Decode([0, 1, 2], 100));
        Assert.Throws<DecoderFallbackException>(() => TextExtraction.Decode([0xFF, 0xFE, 0x42], 100));
        Assert.Throws<DecoderFallbackException>(() => TextExtraction.Decode([0xC3, 0x28], 100));
        Assert.Throws<InvalidDataException>(() => TextExtraction.Normalize("  \r\n", 100));
        Assert.Throws<InvalidDataException>(() => TextExtraction.Normalize("too long", 2));
    }

    [Theory]
    [InlineData("../../secret.txt", "secret.txt")]
    [InlineData("C:\\users\\secret.pdf", "secret.pdf")]
    [InlineData("<script>.txt", "script.txt")]
    public void OriginalDisplayNamesAreSanitized(string name, string expected)
        => Assert.Equal(expected, DocumentStorage.SafeName(name));

    [Theory]
    [InlineData("../file.txt")]
    [InlineData("C:\\file.pdf")]
    [InlineData("file.txt")]
    public void StorageRejectsNonGeneratedKeys(string key)
        => Assert.Throws<InvalidOperationException>(() => new DocumentStorage(new("unused")).Resolve(key));

    [Fact]
    public void UnsupportedFileExtensionIsRejected()
        => Assert.Throws<SourceException>(() => DocumentStorage.IsPdf("report.txt.exe"));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/secret")]
    [InlineData("relative/path")]
    public void SourceAttributionRequiresHttpUrl(string url)
        => Assert.Throws<ValidationException>(() => Source.Create(Guid.NewGuid(), SourceType.Text,
            new() { Title = "Test", OriginalUrl = url }, "document.txt", 10, new('a', 64)));

    [Fact]
    public void FailedRetryPreservesPriorTextIdentityAndOriginalReference()
    {
        var source = Source.Create(Guid.NewGuid(), SourceType.Text, new() { Title = "Test" }, "test.txt", 10, new('a', 64));
        var id = source.Id;
        var key = source.FilePath;
        source.CompleteExtraction("Evidence", "v1");
        source.FailExtraction("Timeout");
        Assert.Equal("Evidence", source.ExtractedText);
        Assert.Equal("v1", source.ExtractionVersion);
        Assert.NotNull(source.ExtractedAt);
        Assert.Equal(id, source.Id);
        Assert.Equal(key, source.FilePath);
        Assert.Equal(SourceProcessingStatus.Failed, source.ProcessingStatus);
        source.CompleteExtraction("Evidence v2", "v2");
        Assert.Null(source.ProcessingError);
        Assert.Equal(SourceProcessingStatus.Ready, source.ProcessingStatus);
    }

    [Fact]
    public void MetadataEditsAreValidatedAndDoNotChangeHash()
    {
        var source = Source.Create(Guid.NewGuid(), SourceType.Text, new() { Title = "Test" }, "test.txt", 10, new('a', 64));
        Assert.Throws<ValidationException>(() => source.UpdateMetadata(new() { Title = " " }));
        Assert.Throws<ValidationException>(() => source.UpdateMetadata(new() { Title = "Good", Reliability = (SourceReliability)42 }));
        source.UpdateMetadata(new() { Title = " Updated ", Publisher = " Fictional ", Reliability = SourceReliability.B });
        Assert.Equal("Updated", source.Title);
        Assert.Equal("Fictional", source.Publisher);
        Assert.Equal(new string('a', 64), source.ContentHash);
    }
}
