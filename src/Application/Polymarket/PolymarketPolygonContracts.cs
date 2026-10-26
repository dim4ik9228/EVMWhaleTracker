namespace Application.Polymarket;

/// <summary>Complete chain log identity retained across websocket overlap and historical recovery.</summary>
public sealed record PolymarketChainLog(string Contract, long BlockNumber, string BlockHash,
    string TransactionHash, int TransactionIndex, int LogIndex, string[] Topics, string Data, bool Removed)
{
    public string Id => $"137:{BlockHash}:{TransactionHash}:{LogIndex}";
}

/// <summary>Bounded public Polygon reads; historical logs are requested only for explicit recovery windows.</summary>
public interface IPolymarketPolygonRpc
{
    Task<long> HeadAsync(CancellationToken cancellationToken);
    /// <summary>Returns the provider's consensus-finalized Polygon boundary; failure never weakens finality.</summary>
    Task<long> FinalizedHeadAsync(CancellationToken cancellationToken);
    Task VerifyChainAsync(CancellationToken cancellationToken);
    Task<PolymarketChainBlock> BlockAsync(long number, CancellationToken cancellationToken);
    Task<IReadOnlyList<PolymarketChainLog>> LogsAsync(object filter, CancellationToken cancellationToken);
}

/// <summary>Canonical block ancestry and timestamp used for finality and source time.</summary>
public sealed record PolymarketChainBlock(long Number, string Hash, string ParentHash, DateTimeOffset Time);

/// <summary>Market enrichment may be unavailable without discarding the underlying settlement.</summary>
public sealed record PolymarketMarket(string Question, string Outcome, string? Slug, string Version);

/// <summary>Shared bounded cache keyed by registry family and position token.</summary>
public interface IPolymarketMarketResolver
{
    Task<PolymarketMarket?> ResolveAsync(string family, string tokenId, CancellationToken cancellationToken);
}
