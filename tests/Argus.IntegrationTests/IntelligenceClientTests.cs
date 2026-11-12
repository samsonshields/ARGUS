using System.Net;
using System.Text;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Exceptions;
using Argus.Infrastructure.ExternalServices;
using Microsoft.Extensions.Options;

namespace Argus.IntegrationTests;

public sealed class IntelligenceClientTests
{
    private static NlpRequest Request() => new("1", Guid.NewGuid(), SourceTextSnapshot.Hash("Alice"), "Alice");
    private static IntelligenceClient Client(HttpMessageHandler handler) => new(new HttpClient(handler)
    {
        BaseAddress = new Uri("http://localhost:8000/"),
        Timeout = Timeout.InfiniteTimeSpan
    }, Options.Create(new IntelligenceOptions { TimeoutSeconds = 1 }));

    [Fact]
    public async Task DeadlineCoversResponseBodyAfterHeaders()
    {
        var client = Client(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new DelayedStream()) }));
        var error = await Assert.ThrowsAsync<ProcessingException>(() => client.ProcessAsync(Request(), CancellationToken.None));
        Assert.Equal("nlp_timeout", error.Code);
        Assert.True(error.Retryable);
    }
    [Theory]
    [InlineData(503, true)]
    [InlineData(422, false)]
    public async Task HttpFailuresHaveSafeMessagesAndRetryPolicy(int status, bool retryable)
    {
        var client = Client(new Handler(_ => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("PRIVATE_DOCUMENT") }));
        var error = await Assert.ThrowsAsync<ProcessingException>(() => client.ProcessAsync(Request(), CancellationToken.None));
        Assert.Equal(retryable, error.Retryable);
        Assert.DoesNotContain("PRIVATE_DOCUMENT", error.Message);
    }
    [Fact]
    public async Task MalformedJsonIsNotRetriedOrPersisted()
    {
        var client = Client(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{bad", Encoding.UTF8, "application/json") }));
        var error = await Assert.ThrowsAsync<ProcessingException>(() => client.ProcessAsync(Request(), CancellationToken.None));
        Assert.Equal("invalid_contract", error.Code);
        Assert.False(error.Retryable);
    }
    [Fact]
    public async Task OriginalCancellationIsNotConvertedToRetryableFailure()
    {
        var client = Client(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new DelayedStream()) }));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ProcessAsync(Request(), cts.Token));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
    private sealed class DelayedStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { await Task.Delay(Timeout.Infinite, ct); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
