using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using Application.Messaging;
using Application.Swaps;
using Domain.Common;
using Infrastructure.Blockchain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Swaps;

/// <summary>One explicitly selected trace method; bounded response, pacing and canonical receipt brackets.</summary>
public sealed class EthereumExecutionEvidenceProvider(HttpClient http, IReadOnlyList<BlockchainNetworkOptions> networks,
    IOptions<SwapEnrichmentOptions> options, ILogger<EthereumExecutionEvidenceProvider> logger) : ITransactionExecutionEvidenceProvider
{
    private readonly SwapEnrichmentOptions _options = options.Value;
    private readonly SemaphoreSlim _pacing = new(1, 1);
    private DateTimeOffset _nextRequest;
    private DateTimeOffset _cooldown;
    private DateTimeOffset _traceCooldown;
    private readonly object _timeGate = new();
    private readonly Dictionary<ExecutionIdentity, (DateTimeOffset Time, DateTimeOffset Expires)> _blockTimes = [];
    public async Task<Result<TransactionExecutionEvidence>> GetAsync(ExecutionIdentity identity, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew(); var calls = 0;
        try
        {
            if (!_options.IsEnabled(identity.Chain)) return Result<TransactionExecutionEvidence>.Failure(SwapErrors.Unsupported);
            var endpoint = networks.SingleOrDefault(x => x.Chain == SupportedChain.Ethereum)?.BuildHttpRpcUrl();
            if (endpoint is null) return Result<TransactionExecutionEvidence>.Failure(SwapErrors.Unavailable);
            async Task<JsonElement> Rpc(string method, params object[] parameters)
            { calls++; return await RpcAsync(endpoint, method, parameters, cancellationToken); }
            if (DateTimeOffset.UtcNow < _traceCooldown) throw new EvidenceException(SwapErrors.Unsupported);
            if (Hex(await Rpc("eth_chainId")) != 1) throw new EvidenceException(SwapErrors.Inconsistent);
            var receipt = await Rpc("eth_getTransactionReceipt", identity.TransactionHash);
            ValidateReceipt(receipt, identity);
            var blockNumber = Text(receipt, "blockNumber");
            var header = await Rpc("eth_getBlockByNumber", blockNumber, false);
            if (!Equal(Text(header, "hash"), identity.BlockHash)) throw new EvidenceException(SwapErrors.NonCanonical);
            RememberBlockTime(identity, DateTimeOffset.FromUnixTimeSeconds(checked((long)Hex(header.GetProperty("timestamp")))));
            var tx = await Rpc("eth_getTransactionByHash", identity.TransactionHash);
            Require(Equal(Text(tx, "hash"), identity.TransactionHash) && Equal(Text(tx, "blockHash"), identity.BlockHash)
                && Text(tx, "blockNumber") == blockNumber);
            var trace = await Rpc("trace_transaction", identity.TransactionHash);
            Require(trace.ValueKind == JsonValueKind.Array && trace.GetArrayLength() is > 0 and <= 10000);
            var frames = new List<ExecutionCall>();
            foreach (var frame in trace.EnumerateArray())
            {
                // Unknown execution kinds cannot establish a complete native movement ledger.
                var type = Text(frame, "type"); Require(type == "call");
                var action = frame.GetProperty("action");
                var path = frame.GetProperty("traceAddress").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                Require(path.Length <= 128 && path.All(x => x >= 0));
                frames.Add(new(path, Text(action, "callType"), Text(action, "from").ToLowerInvariant(),
                    Text(action, "to").ToLowerInvariant(), Hex(action.GetProperty("value")), Text(action, "input"), frame.TryGetProperty("error", out _)));
            }
            foreach (var frame in trace.EnumerateArray())
            {
                var path = frame.GetProperty("traceAddress").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                var children = frames.Where(x => x.Path.Length == path.Length + 1 && x.Path.Take(path.Length).SequenceEqual(path)).ToArray();
                Require(children.Length == frame.GetProperty("subtraces").GetInt32()
                    && children.Select(x => x.Path[^1]).Order().SequenceEqual(Enumerable.Range(0, children.Length)));
            }
            Require(frames.Count(x => x.Path.Length == 0) == 1);
            var root = frames.Single(x => x.Path.Length == 0);
            Require(!root.Failed && root.Type == "call" && Equal(root.From, Text(tx, "from")) && Equal(root.To, Text(tx, "to"))
                && root.Value == Hex(tx.GetProperty("value")) && Equal(root.Input, Text(tx, "input")));
            Require(frames.Select(x => string.Join('.', x.Path)).Distinct().Count() == frames.Count);
            foreach (var frame in frames.Where(x => x.Path.Length > 0))
                Require(frames.Any(parent => parent.Path.SequenceEqual(frame.Path[..^1])));
            var logs = receipt.GetProperty("logs").EnumerateArray().Select(x =>
            {
                Require(Equal(Text(x, "transactionHash"), identity.TransactionHash) && Equal(Text(x, "blockHash"), identity.BlockHash)
                    && !(x.TryGetProperty("removed", out var removed) && removed.GetBoolean()));
                return new RawLogData(identity.TransactionHash, Text(x, "address").ToLowerInvariant(),
                    x.GetProperty("topics").EnumerateArray().Select(y => y.GetString()!).ToArray(), Text(x, "data"),
                    checked((ulong)Hex(x.GetProperty("logIndex"))), false, identity.BlockHash);
            }).ToArray();
            Require(logs.Select(x => x.LogIndex).Distinct().Count() == logs.Length);
            var pools = new List<VerifiedPool>();
            foreach (var address in logs.Where(x => x.Topics.Count > 0 && Equal(x.Topics[0], UniswapV3Classifier.SwapTopic)).Select(x => x.ContractAddress).Distinct())
            {
                if (pools.Count >= 8) throw new EvidenceException(SwapErrors.Limited);
                async Task<string> Call(string to, string data) => (await Rpc("eth_call", new { to, data }, blockNumber)).GetString()!;
                var token0 = DecodeAddress(await Call(address, "0x0dfe1681"));
                var token1 = DecodeAddress(await Call(address, "0xd21220a7"));
                var fee = checked((int)HexString(await Call(address, "0xddca3f43")));
                Require(fee is > 0 and < 1000000 && string.CompareOrdinal(token0, token1) < 0);
                var getPool = "0x1698ee82" + token0[2..].PadLeft(64, '0') + token1[2..].PadLeft(64, '0') + fee.ToString("x").PadLeft(64, '0');
                Require(Equal(DecodeAddress(await Call(UniswapV3Classifier.Factory, getPool)), address));
                pools.Add(new(address, token0, token1, fee));
            }
            var after = await Rpc("eth_getTransactionReceipt", identity.TransactionHash); ValidateReceipt(after, identity);
            Require(receipt.GetRawText() == after.GetRawText());
            var afterHeader = await Rpc("eth_getBlockByNumber", blockNumber, false);
            if (!Equal(Text(afterHeader, "hash"), identity.BlockHash)) throw new EvidenceException(SwapErrors.NonCanonical);
            var evidence = new TransactionExecutionEvidence(identity, root.From, root.To, root.Value,
                checked((ulong)HexString(blockNumber)), DateTimeOffset.FromUnixTimeSeconds(checked((long)Hex(header.GetProperty("timestamp")))), logs, frames, pools);
            logger.LogDebug(SwapDiagnostics.Provider, "verified", watch.ElapsedMilliseconds, calls);
            return Result<TransactionExecutionEvidence>.Success(evidence);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (EvidenceException ex) { logger.LogDebug(SwapDiagnostics.Provider, ex.Error.Code, watch.ElapsedMilliseconds, calls); return Result<TransactionExecutionEvidence>.Failure(ex.Error); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
        { logger.LogDebug(SwapDiagnostics.Provider, SwapErrors.Inconsistent.Code, watch.ElapsedMilliseconds, calls); return Result<TransactionExecutionEvidence>.Failure(SwapErrors.Inconsistent); }
    }
    /// <summary>Canonical block time is required even for fallback, so replay cannot masquerade as current activity.</summary>
    public async Task<Result<DateTimeOffset>> GetBlockTimeAsync(ExecutionIdentity identity, CancellationToken ct)
    {
        lock (_timeGate)
            if (_blockTimes.TryGetValue(identity, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
                return Result<DateTimeOffset>.Success(cached.Time);
        try
        {
            var endpoint = networks.SingleOrDefault(x => x.Chain == SupportedChain.Ethereum)?.BuildHttpRpcUrl();
            if (identity.Chain != SupportedChain.Ethereum || endpoint is null) return Result<DateTimeOffset>.Failure(SwapErrors.Unavailable);
            if (Hex(await RpcAsync(endpoint, "eth_chainId", [], ct)) != 1) throw new EvidenceException(SwapErrors.Inconsistent);
            var receipt = await RpcAsync(endpoint, "eth_getTransactionReceipt", [identity.TransactionHash], ct);
            ValidateReceipt(receipt, identity);
            var header = await RpcAsync(endpoint, "eth_getBlockByNumber", [Text(receipt, "blockNumber"), false], ct);
            if (!Equal(Text(header, "hash"), identity.BlockHash)) throw new EvidenceException(SwapErrors.NonCanonical);
            var time = DateTimeOffset.FromUnixTimeSeconds(checked((long)Hex(header.GetProperty("timestamp"))));
            RememberBlockTime(identity, time);
            return Result<DateTimeOffset>.Success(time);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Result<DateTimeOffset>.Failure(SwapErrors.Unavailable); }
        catch (EvidenceException ex) { return Result<DateTimeOffset>.Failure(ex.Error); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
        { return Result<DateTimeOffset>.Failure(SwapErrors.Inconsistent); }
    }
    private void RememberBlockTime(ExecutionIdentity identity, DateTimeOffset time)
    {
        lock (_timeGate)
        {
            if (_blockTimes.Count >= _options.CacheCapacity)
                _blockTimes.Remove(_blockTimes.MinBy(x => x.Value.Expires).Key);
            _blockTimes[identity] = (time, DateTimeOffset.UtcNow + _options.CacheTtl);
        }
    }
    private async Task<JsonElement> RpcAsync(string endpoint, string method, object[] parameters, CancellationToken cancellationToken)
    {
                await _pacing.WaitAsync(cancellationToken);
                try
                {
                    if (DateTimeOffset.UtcNow < _cooldown) throw new EvidenceException(SwapErrors.Limited);
                    var delay = _nextRequest - DateTimeOffset.UtcNow;
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
                    _nextRequest = DateTimeOffset.UtcNow + _options.RequestSpacing;
                }
                finally { _pacing.Release(); }
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                { Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = parameters }) };
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? _options.ProviderCooldown;
                    _cooldown = DateTimeOffset.UtcNow + (retry > _options.ProviderCooldown ? retry : _options.ProviderCooldown);
                    throw new EvidenceException(SwapErrors.Limited);
                }
                if (!response.IsSuccessStatusCode) throw new EvidenceException(SwapErrors.Unavailable);
                if (response.Content.Headers.ContentLength > _options.MaximumResponseBytes) throw new EvidenceException(SwapErrors.Limited);
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var buffer = new MemoryStream(); var chunk = new byte[8192]; int count;
                while ((count = await source.ReadAsync(chunk, cancellationToken)) != 0)
                {
                    if (buffer.Length + count > _options.MaximumResponseBytes) throw new EvidenceException(SwapErrors.Limited);
                    buffer.Write(chunk, 0, count);
                }
                using var document = JsonDocument.Parse(buffer.ToArray());
                if (document.RootElement.TryGetProperty("error", out var error))
                {
                    var code = error.TryGetProperty("code", out var codeElement) ? codeElement.GetInt32() : 0;
                    if (code is -32601 or -32004 or -32005)
                    { _traceCooldown = DateTimeOffset.UtcNow + _options.ProviderCooldown; throw new EvidenceException(SwapErrors.Unsupported); }
                    throw new EvidenceException(SwapErrors.Unavailable);
                }
                var result = document.RootElement.GetProperty("result");
                if (result.ValueKind == JsonValueKind.Null) throw new EvidenceException(SwapErrors.Unavailable);
                return result.Clone();
    }
    private static void ValidateReceipt(JsonElement receipt, ExecutionIdentity identity)
    {
        if (!Equal(Text(receipt, "blockHash"), identity.BlockHash)) throw new EvidenceException(SwapErrors.NonCanonical);
        Require(Hex(receipt.GetProperty("status")) == 1 && Equal(Text(receipt, "transactionHash"), identity.TransactionHash));
    }
    internal static BigInteger HexString(string text) => BigInteger.Parse("0" + text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    private static BigInteger Hex(JsonElement value) => HexString(value.GetString()!);
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static string DecodeAddress(string word) { Require(word.Length == 66 && word[2..26].All(x => x == '0')); return "0x" + word[26..].ToLowerInvariant(); }
    private static bool Equal(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static void Require(bool condition) { if (!condition) throw new EvidenceException(SwapErrors.Inconsistent); }
    private sealed class EvidenceException(Error error) : Exception { public Error Error { get; } = error; }
}
