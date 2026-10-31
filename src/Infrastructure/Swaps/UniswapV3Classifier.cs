using System.Numerics;
using Application.Swaps;
using Domain.Common;
using Infrastructure.Blockchain;

namespace Infrastructure.Swaps;

/// <summary>Conservative pinned V3 path proof, reconciled with wallet and pool raw movements.</summary>
public sealed class UniswapV3Classifier
{
    public const string Factory = "0x1f98431c8ad98523631ae4a59f267346ea31f984";
    public const string Weth = "0xc02aaa39b223fe8d0a0e5c4f27ead9083c756cc2";
    public const string Native = "native";
    public const string SwapTopic = "0xc42079f94a6350d7e6235f29174924f928cc2ac818eb64fed8004e115fbcca67";
    public static readonly IReadOnlySet<string> Routers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "0xe592427a0aece92de3edee1f18e0157c05861564", "0x68b3465833fb72a70ecdf485e0e4c7bd8665fc45" };
    private readonly Erc20TransferLogParser _parser = new();

    public Result<RawSwap> Classify(TransactionExecutionEvidence e, string wallet)
    {
        Result<RawSwap> Fail() => Result<RawSwap>.Failure(SwapErrors.NotSwap);
        wallet = wallet.ToLowerInvariant();
        if (e.Identity.Chain != SupportedChain.Ethereum || e.Sender != wallet || !Routers.Contains(e.Destination) || e.Pools.Count == 0) return Fail();
        var routerPath = UniswapV3RouterDecoder.Decode(e, wallet);
        if (routerPath is null) return Fail();
        var successful = e.Calls.Where(x => !e.Calls.Any(parent => parent.Failed && IsPrefix(parent.Path, x.Path))).ToArray();
        // Only ordinary CALL carries value. Delegatecall inherits msg.value, but moves no ETH.
        if (successful.Any(x => x.Type is not ("call" or "staticcall" or "delegatecall"))) return Fail();
        var net = new Dictionary<string, BigInteger>(StringComparer.OrdinalIgnoreCase);
        var poolNet = new Dictionary<(string Pool, string Token), BigInteger>();
        void Add(string asset, BigInteger value) { net[asset] = net.GetValueOrDefault(asset) + value; }
        foreach (var log in e.Logs)
        {
            if (!_parser.TryParse(log, e.Identity.Chain, out var transfer)) continue;
            if (transfer.FromAddress.Value.Equals(wallet, StringComparison.OrdinalIgnoreCase)) Add(log.ContractAddress, -transfer.RawAmount);
            if (transfer.ToAddress.Value.Equals(wallet, StringComparison.OrdinalIgnoreCase)) Add(log.ContractAddress, transfer.RawAmount);
            foreach (var pool in e.Pools)
            {
                var key = (pool.Address, log.ContractAddress);
                if (transfer.ToAddress.Value.Equals(pool.Address, StringComparison.OrdinalIgnoreCase)) poolNet[key] = poolNet.GetValueOrDefault(key) + transfer.RawAmount;
                if (transfer.FromAddress.Value.Equals(pool.Address, StringComparison.OrdinalIgnoreCase)) poolNet[key] = poolNet.GetValueOrDefault(key) - transfer.RawAmount;
            }
        }
        foreach (var call in successful.Where(x => x.Type == "call"))
        {
            if (call.From == wallet) Add(Native, -call.Value);
            if (call.To == wallet) Add(Native, call.Value);
        }
        var debits = net.Where(x => x.Value < 0).ToArray(); var credits = net.Where(x => x.Value > 0).ToArray();
        if (debits.Length != 1 || credits.Length != 1 || debits[0].Key == credits[0].Key) return Fail();
        var hops = new List<(string Input, BigInteger Spent, string Output, BigInteger Received)>();
        foreach (var pool in e.Pools)
        {
            var logs = e.Logs.Where(x => x.ContractAddress == pool.Address && x.Topics.Count > 0 && x.Topics[0].Equals(SwapTopic, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (logs.Length != 1 || logs[0].Topics.Count != 3 || logs[0].Data.Length != 322) return Fail();
            var log = logs[0];
            if (e.Logs.Any(x => x.ContractAddress == pool.Address && (x.Topics.Count == 0 || !x.Topics[0].Equals(SwapTopic, StringComparison.OrdinalIgnoreCase)))) return Fail();
            var a0 = Signed(log.Data.Substring(2, 64)); var a1 = Signed(log.Data.Substring(66, 64));
            if (a0.IsZero || a1.IsZero || a0.Sign == a1.Sign) return Fail();
            if (poolNet.GetValueOrDefault((pool.Address, pool.Token0)) != a0 || poolNet.GetValueOrDefault((pool.Address, pool.Token1)) != a1) return Fail();
            // A successful router-driven swap call and pool callback must both occur in the tree.
            if (!successful.Any(x => x.Type == "call" && x.To == pool.Address && x.Input.StartsWith("0x128acb08", StringComparison.OrdinalIgnoreCase))) return Fail();
            if (a0 > 0) hops.Add((pool.Token0, a0, pool.Token1, -a1)); else hops.Add((pool.Token1, a1, pool.Token0, -a0));
        }
        var input = debits[0].Key == Native ? Weth : debits[0].Key;
        var output = credits[0].Key == Native ? Weth : credits[0].Key;
        var amount = -debits[0].Value; var current = input;
        if (routerPath.Tokens[0] != input || routerPath.Tokens[^1] != output || routerPath.AmountIn != amount) return Fail();
        while (hops.Count > 0)
        {
            var next = hops.Where(x => x.Input == current && x.Spent == amount).ToArray();
            if (next.Length != 1) return Fail();
            hops.Remove(next[0]); current = next[0].Output; amount = next[0].Received;
        }
        if (current != output || amount != credits[0].Value) return Fail();
        // Native endpoints require matching WETH deposit/withdrawal and successful router payment;
        // an unrelated refund cannot stand in for the pool output.
        if (debits[0].Key == Native && !successful.Any(x => x.To == Weth && x.From == e.Destination && x.Input == "0xd0e30db0" && x.Value == -debits[0].Value)) return Fail();
        if (credits[0].Key == Native && !successful.Any(x => x.To == Weth && x.From == e.Destination
            && x.Input.Length == 74 && x.Input.StartsWith("0x2e1a7d4d") && EthereumExecutionEvidenceProvider.HexString("0x" + x.Input[10..]) == credits[0].Value)) return Fail();
        return Result<RawSwap>.Success(new(debits[0].Key, -debits[0].Value, credits[0].Key, credits[0].Value));
    }
    private static bool IsPrefix(int[] prefix, int[] path) => prefix.Length <= path.Length && path.Take(prefix.Length).SequenceEqual(prefix);
    private static BigInteger Signed(string word)
    { var unsigned = EthereumExecutionEvidenceProvider.HexString("0x" + word); return unsigned >= BigInteger.Pow(2, 255) ? unsigned - BigInteger.Pow(2, 256) : unsigned; }
}
