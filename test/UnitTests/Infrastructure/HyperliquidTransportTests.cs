using System.Net;
using System.Net.WebSockets;
using System.Text;
using Infrastructure.Hyperliquid;
using Microsoft.Extensions.Options;

namespace UnitTests.Infrastructure;

public sealed class HyperliquidTransportTests
{
    [Fact]
    public async Task FragmentedUtf8DecodesAfterCompleteBoundedMessage()
    {
        var text = "{\"channel\":\"pong\",\"text\":\"€\"}";
        var bytes = Encoding.UTF8.GetBytes(text);
        var euro = Array.IndexOf(bytes, (byte)0xe2);
        await using var socket = new HyperliquidSocket(new Fragments([bytes[..(euro + 1)], bytes[(euro + 1)..]]));
        Assert.Equal(text, await socket.ReceiveAsync(1024, default));
    }

    [Fact]
    public async Task OversizedFragmentedMessageIsRejectedBeforeDecoding()
    {
        await using var socket = new HyperliquidSocket(new Fragments([new byte[20], new byte[20]]));
        await Assert.ThrowsAsync<IOException>(() => socket.ReceiveAsync(32, default));
    }

    [Fact]
    public async Task ClosedSocketIsNotAcceptedAsAnEmptyEvent()
    {
        await using var socket = new HyperliquidSocket(new Fragments([[]], WebSocketMessageType.Close));
        await Assert.ThrowsAsync<IOException>(() => socket.ReceiveAsync(1024, default));
    }

    [Fact]
    public async Task SocketDisposalIsIdempotentAcrossEpochCleanup()
    {
        var transport = new Fragments([[]]);
        var socket = new HyperliquidSocket(transport);
        await socket.DisposeAsync();
        await socket.DisposeAsync();
        Assert.Equal(1, transport.Disposals);
    }

    [Fact]
    public async Task PublicInfoAdapterUsesConfiguredEndpointAndExactRequestAndBoundsResponse()
    {
        var handler = new Handler();
        using var client = new HttpClient(handler);
        var adapter = new HyperliquidInfoClient(client, Options.Create(new HyperliquidOptions { InfoUrl = "https://fixture.invalid/info" }));
        Assert.Equal("[]", await adapter.QueryAsync(new { type = "userFillsByTime", user = "fixture", startTime = 1L, endTime = 2L, aggregateByTime = false }, 100, default));
        Assert.Equal("https://fixture.invalid/info", handler.Url);
        Assert.Contains("\"aggregateByTime\":false", handler.Body);
        Assert.Contains("\"startTime\":1", handler.Body);
        handler.Response = new string('x', 8388609);
        await Assert.ThrowsAsync<IOException>(() => adapter.QueryAsync(new { type = "meta" }, 20, default));
    }

    [Fact]
    public async Task HttpCancellationDoesNotBecomeRecoverySuccess()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        using var client = new HttpClient(new Handler());
        var adapter = new HyperliquidInfoClient(client, Options.Create(new HyperliquidOptions()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.QueryAsync(new { type = "meta" }, 20, cts.Token));
    }

    [Fact]
    public async Task HttpReadsHeadersBeforeOpeningTheBoundedResponseStream()
    {
        using var client = new HttpClient(new StreamingHandler());
        var adapter = new HyperliquidInfoClient(client, Options.Create(new HyperliquidOptions()));
        Assert.Equal("[]", await adapter.QueryAsync(new { type = "meta" }, 20, default));
    }

    private sealed class StreamingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamingContent() });
    }

    private sealed class StreamingContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new InvalidOperationException("HTTP content must not be pre-buffered before the size bound.");
        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("[]")));
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string Response = "[]"; public string? Url; public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.ToString(); Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Response) };
        }
    }
    private sealed class Fragments(byte[][] chunks, WebSocketMessageType type = WebSocketMessageType.Text) : WebSocket
    {
        private int _index;
        public int Disposals;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? description, CancellationToken ct) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? description, CancellationToken ct) => Task.CompletedTask;
        public override void Dispose() { Disposals++; }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
        {
            var chunk = chunks[_index++]; chunk.CopyTo(buffer.Array!, buffer.Offset);
            return Task.FromResult(new WebSocketReceiveResult(chunk.Length, type, _index == chunks.Length));
        }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken ct) => Task.CompletedTask;
    }
}
