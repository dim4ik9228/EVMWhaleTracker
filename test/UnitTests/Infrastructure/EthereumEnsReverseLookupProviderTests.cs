using System.Net;
using System.Text;
using Domain.Common;
using Domain.ValueObjects;
using Infrastructure.Blockchain;
using Infrastructure.Ens;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace UnitTests.Infrastructure;

public sealed class EthereumEnsReverseLookupProviderTests
{
    [Fact]
    public async Task ResolveNameAsync_ReturnsEnsNameAndCachesSuccess()
    {
        var handler = new FakeEnsRpcHandler();
        using var httpClient = new HttpClient(handler);
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var provider = new EthereumEnsReverseLookupProvider(
            httpClient,
            Options.Create(new BlockchainNetworksOptions
            {
                Networks =
                [
                    new BlockchainNetworkOptions
                    {
                        Chain = SupportedChain.Ethereum,
                        HttpRpcUrl = "https://ethereum-rpc.test/"
                    }
                ]
            }),
            Options.Create(new EnsReverseLookupOptions()),
            memoryCache,
            NullLogger<EthereumEnsReverseLookupProvider>.Instance);

        var address = Address.Normalize("0xd8da6bf26964af9d7eed9e03e53415d37aa96045").Value;

        var first = await provider.ResolveNameAsync(address, CancellationToken.None);
        var second = await provider.ResolveNameAsync(address, CancellationToken.None);

        Assert.True(first is not null, $"Expected ENS name. Calls: {handler.Calls}. Bodies: {string.Join(" | ", handler.Bodies)}");
        Assert.Equal("vitalik.eth", first);
        Assert.Equal("vitalik.eth", second);
        Assert.Equal(2, handler.Calls);
    }

    private sealed class FakeEnsRpcHandler : HttpMessageHandler
    {
        private const string ResolverSelector = "0178b8bf";
        private const string NameSelector = "691f3431";
        private const string ResolverAddress = "1234567890123456789012345678901234567890";

        public int Calls { get; private set; }
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Bodies.Add(body);
            var result = body.Contains(ResolverSelector, StringComparison.OrdinalIgnoreCase)
                ? "0x" + new string('0', 24) + ResolverAddress
                : body.Contains(NameSelector, StringComparison.OrdinalIgnoreCase)
                    ? EncodeAbiString("vitalik.eth")
                    : "0x";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"jsonrpc":"2.0","id":1,"result":"{{result}}"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }

        private static string EncodeAbiString(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var offset = new string('0', 62) + "20";
            var length = bytes.Length.ToString("x").PadLeft(64, '0');
            var data = Convert.ToHexString(bytes).ToLowerInvariant().PadRight(64, '0');
            return "0x" + offset + length + data;
        }
    }
}
