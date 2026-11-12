using System.Net;
using System.Text;
using Infrastructure.Polymarket;
using Microsoft.Extensions.Options;

namespace UnitTests.Infrastructure.Polymarket;

public sealed class PolymarketPolygonRpcTests
{
    [Theory]
    [InlineData(false, "96169153")]
    [InlineData(true, "finalized")]
    public async Task BlockRejectionIncludesSafeTargetAndCodeOnly(bool finalized, string target)
    {
        using var http = new HttpClient(new RejectedBlockHandler());
        var rpc = new PolymarketPolygonRpc(http, Options.Create(new PolymarketPolygonOptions
        {
            Enabled = true, HttpRpcUrl = "https://secret-host.invalid/private-key", RpcApiKey = "private-api-key"
        }));

        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            if (finalized) await rpc.FinalizedHeadAsync(CancellationToken.None);
            else await rpc.BlockAsync(96169153, CancellationToken.None);
        });

        Assert.Equal($"Polygon eth_getBlockByNumber target {target} rejected (RPC code -32603); recovery remains pending.", error.Message);
        Assert.Null(error.InnerException);
    }

    private sealed class RejectedBlockHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"error":{"code":-32603,"message":"https://provider.invalid/provider-secret arbitrary backend text"}}""", Encoding.UTF8, "application/json")
            });
    }
}
