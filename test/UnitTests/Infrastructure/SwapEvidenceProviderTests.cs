using System.Net;
using System.Text;
using System.Text.Json;
using Application.Swaps;
using Domain.Common;
using Infrastructure.Blockchain;
using Infrastructure.Swaps;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace UnitTests.Infrastructure;

public sealed class SwapEvidenceProviderTests
{
    [Theory]
    [InlineData("ok", "")]
    [InlineData("unsupported", "Swap.Unsupported")]
    [InlineData("429", "Swap.Limited")]
    [InlineData("reorg", "Swap.NonCanonical")]
    [InlineData("truncated", "Swap.Inconsistent")]
    [InlineData("oversized", "Swap.Limited")]
    [InlineData("calldata", "Swap.Inconsistent")]
    public async Task ProviderVerifiesCanonicalBracketsAndRejectsIncompleteResponses(string scenario, string code)
    {
        var handler = new RpcFixture(scenario); using var http = new HttpClient(handler);
        var options = new SwapEnrichmentOptions { Enabled = true, ProviderQualified = true, RequestSpacing = TimeSpan.FromMilliseconds(1), MaximumResponseBytes = scenario == "oversized" ? 1024 : 4194304 };
        var provider = new EthereumExecutionEvidenceProvider(http, [new() { Chain = SupportedChain.Ethereum, HttpRpcUrl = "https://unit.invalid/" }], Options.Create(options), NullLogger<EthereumExecutionEvidenceProvider>.Instance);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var result = await provider.GetAsync(SwapEnrichmentTests.Reference().Identity, budget.Token);
        if (scenario == "ok")
        {
            Assert.True(result.IsSuccess); Assert.True(new UniswapV3Classifier().Classify(result.Value, SwapEnrichmentTests.Wallet).IsSuccess);
            Assert.Equal(2, handler.Methods.Count(x => x == "eth_getTransactionReceipt"));
            Assert.Equal(2, handler.Methods.Count(x => x == "eth_getBlockByNumber"));
        }
        else { Assert.True(result.IsFailure); Assert.Equal(code, result.Error.Code); }
        Assert.DoesNotContain("debug_traceTransaction", handler.Methods);
        if (scenario is "unsupported" or "429")
        {
            var before = handler.Methods.Count;
            Assert.True((await provider.GetAsync(SwapEnrichmentTests.Reference().Identity, budget.Token)).IsFailure);
            Assert.Equal(before, handler.Methods.Count);
            if (scenario == "unsupported")
            {
                var time = await provider.GetBlockTimeAsync(SwapEnrichmentTests.Reference().Identity, budget.Token);
                Assert.True(time.IsSuccess); Assert.Equal(SwapEnrichmentTests.Reference().BlockTime, time.Value);
                Assert.Equal(before, handler.Methods.Count);
            }
        }
    }
    [Fact]
    public async Task ProviderHonorsCancellationDuringTrace()
    {
        var handler = new RpcFixture("slow"); using var http = new HttpClient(handler);
        var provider = new EthereumExecutionEvidenceProvider(http, [new() { HttpRpcUrl = "https://unit.invalid/" }],
            Options.Create(new SwapEnrichmentOptions { Enabled = true, ProviderQualified = true, RequestSpacing = TimeSpan.FromMilliseconds(1) }), NullLogger<EthereumExecutionEvidenceProvider>.Instance);
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetAsync(SwapEnrichmentTests.Reference().Identity, budget.Token));
        Assert.Single(handler.Methods, x => x == "trace_transaction");
    }
    private sealed class RpcFixture(string scenario) : HttpMessageHandler
    {
        public List<string> Methods = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var method = payload.RootElement.GetProperty("method").GetString()!; Methods.Add(method);
            var e = SwapEnrichmentTests.Reference(); object? result;
            switch (method)
            {
                case "eth_chainId": result = "0x1"; break;
                case "eth_getTransactionReceipt":
                    using (var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Swaps", "ondo-receipt.json"))))
                        result = receipt.RootElement.GetProperty("result").Clone();
                    break;
                case "eth_getTransactionByHash":
                    using (var tx = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Swaps", "ondo-transaction.json"))))
                        result = tx.RootElement.GetProperty("result").Clone();
                    break;
                case "eth_getBlockByNumber": result = new { hash = scenario == "reorg" ? "0x" + new string('f', 64) : e.Identity.BlockHash, timestamp = "0x6ae3d46f" }; break;
                case "trace_transaction":
                    if (scenario == "slow") await Task.Delay(10000, cancellationToken);
                    if (scenario == "429") return new(HttpStatusCode.TooManyRequests);
                    if (scenario == "unsupported") return Response(new { error = new { code = -32601, message = "unavailable" } });
                    result = e.Calls.Select(x => new { type = "call", action = new { callType = x.Type, from = x.From, to = x.To, value = "0x" + x.Value.ToString("x"), input = scenario == "calldata" && x.Path.Length == 0 ? "0x" : x.Input },
                        traceAddress = x.Path, subtraces = scenario == "truncated" ? 99 : e.Calls.Count(y => y.Path.Length == x.Path.Length + 1 && y.Path.Take(x.Path.Length).SequenceEqual(x.Path)) }).ToArray();
                    break;
                case "eth_call":
                    var data = payload.RootElement.GetProperty("params")[0].GetProperty("data").GetString()!;
                    result = data switch { "0x0dfe1681" => Word(UniswapV3Classifier.Weth[2..]), "0xd21220a7" => Word(SwapEnrichmentTests.Ondo[2..]), "0xddca3f43" => Word("bb8"), _ => Word(SwapEnrichmentTests.Pool[2..]) }; break;
                default: throw new InvalidOperationException(method);
            }
            return Response(new { result });
        }
        private static string Word(string value) => "0x" + value.PadLeft(64, '0');
        private static HttpResponseMessage Response(object payload) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
    }
}
