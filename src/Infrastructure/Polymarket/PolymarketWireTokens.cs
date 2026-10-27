namespace Infrastructure.Polymarket;

/// <summary>Provider dispatch and protocol values; bytes must remain compatible with upstream APIs.</summary>
internal static class PolymarketWireTokens
{
    public const string CorrectionSuffix = ":correction";
    public const string CorrectionIdentitySuffix = ":correction:";
    public const string ChainId = "eth_chainId";
    public const string BlockNumber = "eth_blockNumber";
    public const string BlockByNumber = "eth_getBlockByNumber";
    public const string Logs = "eth_getLogs";
    public const string Subscribe = "eth_subscribe";
    public const string LogSubscription = "logs";
    public const string Heads = "newHeads";
    public const string FinalizedBlock = "finalized";
    public const string FinalizedMode = "Finalized";
    public const string ConfirmationsMode = "Confirmations";
    public const string CurrentCtf = "ctf-current";
    public const string LegacyCtf = "ctf-legacy";
    public const string CurrentNegativeCtf = "ctf-negative-current";
    public const string LegacyNegativeCtf = "ctf-negative-legacy";
    public const string PositionV2 = "position-v2";
    public const string CtfRedemption = "ctf-redemption";
    public const string MarketsV1 = "v1";
    public const string MarketsV2 = "v2";
    public const string PolygonChainQuantity = "0x89";
}
