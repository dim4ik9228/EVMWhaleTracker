using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Application.Hyperliquid;
using Microsoft.Extensions.Options;

namespace Infrastructure.Hyperliquid;

/// <summary>Public ClientWebSocket factory; fragmented messages remain bounded before decoding.</summary>
public sealed class HyperliquidSocketFactory : IHyperliquidSocketFactory
{
    public IHyperliquidSocket Create() => new HyperliquidSocket();
}

/// <summary>Serializes sends because ClientWebSocket permits only one concurrent send.</summary>
public sealed class HyperliquidSocket : IHyperliquidSocket
{
    private readonly WebSocket _socket;
    private readonly SemaphoreSlim _send = new(1);
    public HyperliquidSocket() : this(new ClientWebSocket()) { }
    internal HyperliquidSocket(WebSocket socket) => _socket = socket;
    public Task ConnectAsync(Uri uri, CancellationToken ct) => ((ClientWebSocket)_socket).ConnectAsync(uri, ct);
    public async Task SendAsync(object message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await _send.WaitAsync(ct);
        try { await _socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, ct); }
        finally { _send.Release(); }
    }
    public async Task<string> ReceiveAsync(int maximumBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[Math.Min(maximumBytes, 16384)];
        while (true)
        {
            var result = await _socket.ReceiveAsync(chunk.AsMemory(), ct);
            if (result.MessageType != WebSocketMessageType.Text) throw new IOException(HyperliquidTransportsDiagnostics.HyperliquidSocketClosedOrSentANonText);
            if (buffer.Length + result.Count > maximumBytes) throw new IOException(HyperliquidTransportsDiagnostics.HyperliquidFrameExceedsConfiguredLimit);
            buffer.Write(chunk, 0, result.Count);
            if (result.EndOfMessage) return new UTF8Encoding(false, true).GetString(buffer.ToArray());
        }
    }
    private int _disposed;
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) { _socket.Abort(); _socket.Dispose(); }
        // A pending send releases this gate after Abort; disposing it here races that finally block.
        return ValueTask.CompletedTask;
    }
}

/// <summary>Single-flight shared weighted HTTP budget conservatively charges history at maximum response weight.</summary>
public sealed class HyperliquidInfoClient(HttpClient client, IOptions<HyperliquidOptions> options) : IHyperliquidInfoClient
{
    private readonly SemaphoreSlim _gate = new(1);
    private DateTimeOffset _window = DateTimeOffset.UtcNow;
    private int _used;
    public async Task<string> QueryAsync(object request, int weight, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (weight is < 1 or > 1200) throw new ArgumentOutOfRangeException(nameof(weight));
            var now = DateTimeOffset.UtcNow;
            if (now - _window >= TimeSpan.FromMinutes(1)) { _window = now; _used = 0; }
            if (_used + weight > 1100)
            {
                await Task.Delay(TimeSpan.FromMinutes(1) - (now - _window), ct);
                _window = DateTimeOffset.UtcNow; _used = 0;
            }
            _used += weight;
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            using var message = new HttpRequestMessage(HttpMethod.Post, options.Value.InfoUrl) { Content = content };
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token);
            if ((int)response.StatusCode == 429)
            {
                var retry = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(30));
                await Task.Delay(retry > TimeSpan.Zero ? retry : TimeSpan.Zero, ct);
                throw new HttpRequestException(HyperliquidTransportsDiagnostics.HyperliquidHTTPThrottledRecovery);
            }
            response.EnsureSuccessStatusCode();
            // Bound HTTP payloads independently of the websocket limit.
            await using var stream = await response.Content.ReadAsStreamAsync(requestTimeout.Token);
            using var bytes = new MemoryStream();
            var chunk = new byte[16384];
            int count;
            while ((count = await stream.ReadAsync(chunk, requestTimeout.Token)) > 0)
            {
                if (bytes.Length + count > 8388608) throw new IOException(HyperliquidTransportsDiagnostics.HyperliquidHTTPResponseTooLarge);
                bytes.Write(chunk, 0, count);
            }
            var text = Encoding.UTF8.GetString(bytes.ToArray());
            // History adds one unit per twenty returned items. Ledger limits may count distinct blocks,
            // so debit unexpectedly large responses before admitting another request.
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.ValueKind == JsonValueKind.Array)
                _used += Math.Max(0, 20 + (json.RootElement.GetArrayLength() + 19) / 20 - weight);
            return text;
        }
        finally { _gate.Release(); }
    }
}
