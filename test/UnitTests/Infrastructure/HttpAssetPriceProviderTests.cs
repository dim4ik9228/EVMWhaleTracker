using System.Net;
using Infrastructure.Pricing;
using Domain.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace UnitTests.Infrastructure;

public sealed class HttpAssetPriceProviderTests
{
    [Fact]
    public async Task NewChains_FilterPairsAndCachePricesSeparatelyForSameToken()
    {
        using var client = new HttpClient(new PairHandler());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new HttpAssetPriceProvider(client, cache, Options.Create(new AssetPriceOptions()), NullLogger<HttpAssetPriceProvider>.Instance);
        var robinhood = await provider.GetUsdPriceAsync(SupportedChain.Robinhood, "0xtoken", "TEST", CancellationToken.None);
        var ethereum = await provider.GetUsdPriceAsync(SupportedChain.Ethereum, "0xtoken", "TEST", CancellationToken.None);
        Assert.True(robinhood.IsSuccess);
        Assert.True(ethereum.IsSuccess);
        Assert.Equal(2m, robinhood.Value.PriceUsd);
        Assert.Equal(3m, ethereum.Value.PriceUsd);
    }

    private sealed class PairHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"pairs":[{"chainId":"bsc","priceUsd":"99"},{"chainId":"robinhood","priceUsd":"2"},{"chainId":"ethereum","priceUsd":"3"}]}""")
            });
    }
}
