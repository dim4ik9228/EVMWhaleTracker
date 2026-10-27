using Domain.Common;
using System.Globalization;
using System.Numerics;
using Application.Polymarket;
using Nethereum.Util;

namespace Infrastructure.Polymarket;

/// <summary>Pinned Polygon ABI families; only own maker fills are canonical, never OrdersMatched/counterparty summaries.</summary>
public static class PolymarketContractRegistry
{
    public const string Version = "polygon-2026-10-25.1";
    public const string Positions = "0x4d97dcd97ec945f40cf65f87097ace5ea0476045";
    public const string PositionManager = "0x006f54f7f9a22e0000cc2ab60031000000ae9fef";
    public const string Pusd = "0xc011a7e12a19f7b1f670d46f03b03f3342e82dfb";
    public const string Usdc = "0x2791bca1f2de4661ed88a30c99a7a9449aa84174";
    public const string NativeUsdc = "0x3c499c542cef5e3811e1192ce70d8cc03d5c3359";
    public const string AutoRedeemer = "0xa1200000d0002264c9a1698e001292d00e1b00af";
    public static readonly string AutoRedemption = Topic("Redemption(address,uint256,uint256)");
    public static readonly string AutoBinaryRedemption = Topic("BinaryRedemption(address,bytes32,uint256)");
    public static readonly string AutoNegativeRedemption = Topic("NegRiskRedemption(address,bytes32,uint256)");
    public static readonly string PositionRedemption = Topic("PositionRedeemed(address,uint256,address,uint256,uint256)");
    public static readonly string CurrentFill = Topic("OrderFilled(bytes32,address,address,uint8,uint256,uint256,uint256,uint256,bytes32,bytes32)");
    public static readonly string LegacyFill = Topic("OrderFilled(bytes32,address,address,uint256,uint256,uint256,uint256,uint256)");
    public static readonly string Redemption = Topic("PayoutRedemption(address,address,bytes32,bytes32,uint256[],uint256)");
    public static readonly string TransferSingle = Topic("TransferSingle(address,address,address,uint256,uint256)");
    public static readonly string TransferBatch = Topic("TransferBatch(address,address,address,uint256[],uint256[])");
    public sealed record Exchange(string Family, string Address, bool Current);
    public static readonly Exchange[] Exchanges =
    [
        new(PolymarketWireTokens.CurrentCtf, "0xe111180000d2663c0091e4f400237545b87b996b", true),
        new(PolymarketWireTokens.CurrentNegativeCtf, "0xe2222d279d744050d28e00520010520000310f59", true),
        new(PolymarketWireTokens.PositionV2, "0xe3333700ca9d93003f00f0f71f8515005f6c00aa", true),
        new(PolymarketWireTokens.LegacyCtf, "0x4bfb41d5b3570defd03c39a9a4d8de6bd8b8982e", false),
        new(PolymarketWireTokens.LegacyNegativeCtf, "0xc5d563a36ae78145c45a50134d48a1215220f80a", false)
    ];
    public static string Topic(string signature) => "0x" + Sha3Keccack.Current.CalculateHash(signature);
    public static string WalletTopic(string address) => "0x" + address[2..].PadLeft(64, '0');
    public static string AddressTopic(string topic) => "0x" + topic[^40..];
    public static BigInteger Word(string data, int index) => BigInteger.Parse("0" + data.Substring(2 + index * 64, 64), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    public static decimal Units(BigInteger value) => checked((decimal)value / 1000000m);

    public static (PolymarketActivity? Activity, string State) Decode(PolymarketChainLog log, DateTimeOffset time, HashSet<string> sources, string[] families)
    {
        var exchange = Exchanges.SingleOrDefault(e => e.Address == log.Contract && families.Contains(e.Family));
        if (exchange is null || log.Topics.Length != 4) return (null, RuntimeStates.Normalization.Unsupported);
        if (log.Topics[0] != (exchange.Current ? CurrentFill : LegacyFill)) return (null, RuntimeStates.Normalization.Unsupported);
        var maker = AddressTopic(log.Topics[2]);
        if (!sources.Contains(maker)) return (null, RuntimeStates.Normalization.Context);
        if (maker == AddressTopic(log.Topics[3])) return (null, RuntimeStates.Normalization.SelfTradeSuppressed);
        try
        {
            bool buy; BigInteger token, cash, shares;
            if (exchange.Current)
            {
                if (log.Data.Length != 2 + 7 * 64) return (null, RuntimeStates.Normalization.Malformed);
                var side = Word(log.Data, 0);
                if (side != 0 && side != 1) return (null, RuntimeStates.Normalization.Unsupported);
                buy = side == 0; token = Word(log.Data, 1);
                cash = Word(log.Data, buy ? 2 : 3); shares = Word(log.Data, buy ? 3 : 2);
            }
            else
            {
                if (log.Data.Length != 2 + 5 * 64) return (null, RuntimeStates.Normalization.Malformed);
                var makerAsset = Word(log.Data, 0); var takerAsset = Word(log.Data, 1);
                if ((makerAsset == 0) == (takerAsset == 0)) return (null, RuntimeStates.Normalization.Unsupported);
                buy = makerAsset == 0; token = buy ? takerAsset : makerAsset;
                cash = Word(log.Data, buy ? 2 : 3); shares = Word(log.Data, buy ? 3 : 2);
            }
            if (shares <= 0 || token <= 0) return (null, RuntimeStates.Normalization.Malformed);
            if (exchange.Family == PolymarketWireTokens.PositionV2 && (token >> 248) is var module && module != 1 && module != 2)
                return (null, RuntimeStates.Normalization.UnsupportedPositionModule);
            var quantity = Units(shares); var amount = Units(cash);
            // Gross executed collateral excludes the separately emitted fee. Never present this as net proceeds or profit.
            return (new(log.Id + ":" + maker, maker, time, buy ? PolymarketTokens.Actions.Buy : PolymarketTokens.Actions.Sell, token.ToString(CultureInfo.InvariantCulture),
                null, null, quantity, amount / quantity, amount, exchange.Current ? PolymarketTokens.Currencies.Pusd : PolymarketTokens.Currencies.BridgedUsdc, null, log.TransactionHash,
                exchange.Current ? Units(Word(log.Data, 4)) : null), RuntimeStates.Normalization.Decoded);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) { return (null, RuntimeStates.Normalization.Malformed); }
    }
}
