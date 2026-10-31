using System.Numerics;
using System.Text.Json;
using Application.Messaging;
using Application.Abstractions;
using Application.Swaps;
using Domain.Common;
using Infrastructure.Swaps;
using Microsoft.Extensions.Options;

namespace UnitTests.Infrastructure;

public sealed class SwapEnrichmentTests
{
    [Fact]
    public async Task MetadataAdmissionRemainsBoundedUntilIgnoringProviderActuallyCompletes()
    {
        var provider = new StalledMetadata(); var requests = new SwapMetadataRequests(provider, 4);
        using var abandoned = new CancellationTokenSource();
        var first = requests.GetAsync(SupportedChain.Ethereum, "0x1", abandoned.Token);
        var shared = requests.GetAsync(SupportedChain.Ethereum, "0x1", default);
        abandoned.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var occupied = new[] { shared }.Concat(Enumerable.Range(2, 3).Select(x => requests.GetAsync(SupportedChain.Ethereum, "0x" + x, default))).ToArray();
        Assert.Equal(4, provider.Calls);
        for (var i = 5; i < 100; i++) Assert.True((await requests.GetAsync(SupportedChain.Ethereum, "0x" + i, default)).IsFailure);
        Assert.Equal(4, provider.Calls); Assert.All(occupied, x => Assert.False(x.IsCompleted));
        provider.Completion.SetResult(Result<TokenMetadata>.Failure(TokenMetadataErrors.Unavailable));
        await Task.WhenAll(occupied);
        await requests.GetAsync(SupportedChain.Ethereum, "0xnew", default); Assert.Equal(5, provider.Calls);
    }
    private sealed class StalledMetadata : ITokenMetadataProvider
    {
        public int Calls;
        public TaskCompletionSource<Result<TokenMetadata>> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Result<TokenMetadata>> GetAsync(SupportedChain chain, string address, CancellationToken ct)
        { Interlocked.Increment(ref Calls); return Completion.Task; }
    }
    public const string Wallet = "0x42cdf00c85005630cc8605731b9364f712077810";
    public const string Router = "0x68b3465833fb72a70ecdf485e0e4c7bd8665fc45";
    public const string Pool = "0x7b1e5d984a43ee732de195628d20d05cfabc3cc7";
    public const string Ondo = "0xfaba6f8e4a5e8ab82f62fe7c39859fa577269be3";
    public static readonly BigInteger Input = BigInteger.Parse("115160747406552094646927");
    public static readonly BigInteger Output = BigInteger.Parse("17721444116587579749");
    public static TransactionExecutionEvidence Reference()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Swaps", "ondo-receipt.json")));
        var receipt = doc.RootElement.GetProperty("result"); var hash = receipt.GetProperty("transactionHash").GetString()!;
        var block = receipt.GetProperty("blockHash").GetString()!;
        var logs = receipt.GetProperty("logs").EnumerateArray().Select(x => new RawLogData(hash, x.GetProperty("address").GetString()!,
            x.GetProperty("topics").EnumerateArray().Select(t => t.GetString()!).ToArray(), x.GetProperty("data").GetString()!,
            Convert.ToUInt64(x.GetProperty("logIndex").GetString()![2..], 16), false, block)).ToArray();
        // Synthetic execution tree: public receipt above is real; trace availability is not claimed.
        using var tx = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Swaps", "ondo-transaction.json")));
        ExecutionCall[] calls = [new([], "call", Wallet, Router, 0, tx.RootElement.GetProperty("result").GetProperty("input").GetString()!, false),
            new([0], "call", Router, Pool, 0, "0x128acb08", false),
            new([1], "call", Router, UniswapV3Classifier.Weth, 0, "0x2e1a7d4d" + Word(Output), false),
            new([1, 0], "call", UniswapV3Classifier.Weth, Router, Output, "0x", false),
            new([2], "call", Router, Wallet, Output, "0x", false)];
        return new(new(SupportedChain.Ethereum, hash, block), Wallet, Router, 0, 26301642,
            DateTimeOffset.FromUnixTimeSeconds(1793315951), logs, calls, [new(Pool, UniswapV3Classifier.Weth, Ondo, 3000)]);
    }
    private static string Word(BigInteger x) => x.ToString("x").PadLeft(64, '0')[^64..];
    internal static TransactionExecutionEvidence ExactInput(bool native)
    {
        var e = Reference(); var input = BigInteger.Pow(10, 18) * 100; var output = BigInteger.Pow(10, 18) * 50;
        string AddressWord(string address) => address[2..].PadLeft(64, '0');
        RawLogData Transfer(string token, string from, string to, BigInteger amount, ulong index)
            => new(e.Identity.TransactionHash, token, [global::Infrastructure.Blockchain.Erc20TransferLogParser.TransferEventTopic,
                "0x" + AddressWord(from), "0x" + AddressWord(to)], "0x" + Word(amount), index, false, e.Identity.BlockHash);
        var calldata = "0x04e45aaf" + AddressWord(UniswapV3Classifier.Weth) + AddressWord(Ondo) + Word(3000)
            + AddressWord(Wallet) + Word(input) + Word(0) + Word(0);
        var poolSwap = new RawLogData(e.Identity.TransactionHash, Pool, [UniswapV3Classifier.SwapTopic, "0x" + AddressWord(Router), "0x" + AddressWord(Wallet)],
            "0x" + Word(input) + Word(BigInteger.Pow(2, 256) - output) + Word(1) + Word(1) + Word(1), 3, false, e.Identity.BlockHash);
        var logs = new List<RawLogData> { Transfer(UniswapV3Classifier.Weth, native ? Router : Wallet, Pool, input, 1), Transfer(Ondo, Pool, Wallet, output, 2), poolSwap };
        var calls = new List<ExecutionCall> { new([], "call", Wallet, Router, native ? input + 20 : 0, calldata, false), new([0], "call", Router, Pool, 0, "0x128acb08", false) };
        if (native) { calls.Add(new([1], "call", Router, UniswapV3Classifier.Weth, input, "0xd0e30db0", false)); calls.Add(new([2], "call", Router, Wallet, 20, "0x", false)); }
        return e with { Value = native ? input + 20 : 0, Logs = logs, Calls = calls };
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SupportedTokenAndNativeInputsReconcileWalletNetAndExcludeRefundAndGas(bool native)
    {
        var result = new UniswapV3Classifier().Classify(ExactInput(native), Wallet);
        Assert.True(result.IsSuccess);
        Assert.Equal(native ? UniswapV3Classifier.Native : UniswapV3Classifier.Weth, result.Value.InputAsset);
        Assert.Equal(BigInteger.Pow(10, 18) * 100, result.Value.InputAmount);
        Assert.Equal(Ondo, result.Value.OutputAsset); Assert.Equal(BigInteger.Pow(10, 18) * 50, result.Value.OutputAmount);
    }
    [Fact]
    public void SupportedMultihopReconcilesEachPoolWithoutCountingRouterIntermediateAssets()
    {
        var e = ExactInput(false); var secondPool = "0x1111111111111111111111111111111111111111";
        var outputToken = "0xffffffffffffffffffffffffffffffffffffffff";
        string A(string address) => address[2..].PadLeft(64, '0');
        var path = UniswapV3Classifier.Weth[2..] + "000bb8" + Ondo[2..] + "0001f4" + outputToken[2..];
        var calldata = "0xb858183f" + Word(32) + Word(128) + A(Wallet) + Word(BigInteger.Pow(10, 18) * 100) + Word(0)
            + Word(path.Length / 2) + path.PadRight(((path.Length + 63) / 64) * 64, '0');
        var intermediate = e.Logs[1] with { Topics = [e.Logs[1].Topics[0], "0x" + A(Pool), "0x" + A(Router)] };
        var paid = intermediate with { Topics = [intermediate.Topics[0], "0x" + A(Router), "0x" + A(secondPool)], LogIndex = 4 };
        var received = e.Logs[1] with { ContractAddress = outputToken, Topics = [e.Logs[1].Topics[0], "0x" + A(secondPool), "0x" + A(Wallet)],
            Data = "0x" + Word(BigInteger.Pow(10, 18) * 25), LogIndex = 5 };
        var swap = e.Logs[2] with { ContractAddress = secondPool, LogIndex = 6,
            Data = "0x" + Word(BigInteger.Pow(10, 18) * 50) + Word(BigInteger.Pow(2, 256) - BigInteger.Pow(10, 18) * 25) + Word(1) + Word(1) + Word(1) };
        e = e with { Logs = [e.Logs[0], intermediate, e.Logs[2], paid, received, swap],
            Pools = [e.Pools[0], new(secondPool, Ondo, outputToken, 500)],
            Calls = [e.Calls[0] with { Input = calldata }, e.Calls[1], new([1], "call", Router, secondPool, 0, "0x128acb08", false)] };
        var result = new UniswapV3Classifier().Classify(e, Wallet);
        Assert.True(result.IsSuccess); Assert.Equal(outputToken, result.Value.OutputAsset);
        Assert.Equal(BigInteger.Pow(10, 18) * 25, result.Value.OutputAmount);
        Assert.True(new UniswapV3Classifier().Classify(e with { Pools = [e.Pools[0]] }, Wallet).IsFailure);
    }
    [Fact]
    public void ReferenceReceiptAndSyntheticExecutionPreserveExactRawAmounts()
    {
        var result = new UniswapV3Classifier().Classify(Reference(), Wallet);
        Assert.True(result.IsSuccess); Assert.Equal(Input, result.Value.InputAmount); Assert.Equal(Output, result.Value.OutputAmount);
        Assert.Equal(Ondo, result.Value.InputAsset); Assert.Equal(UniswapV3Classifier.Native, result.Value.OutputAsset);
    }
    [Theory]
    [InlineData("router")][InlineData("pool")][InlineData("amount")][InlineData("chain")][InlineData("refund")][InlineData("reverted-parent")][InlineData("extra-asset")]
    public void AmbiguousOrUnprovenEvidenceFallsBack(string mutation)
    {
        var e = Reference();
        e = mutation switch
        {
            "router" => e with { Destination = Wallet },
            "pool" => e with { Pools = [] },
            "amount" => e with { Calls = e.Calls.Select(x => x.To == Wallet ? x with { Value = Output + 1 } : x).ToArray() },
            "chain" => e with { Identity = e.Identity with { Chain = SupportedChain.Base } },
            "refund" => e with { Calls = e.Calls.Where(x => x.To != UniswapV3Classifier.Weth).ToArray() },
            "reverted-parent" => e with { Calls = e.Calls.Select(x => x.Path.Length == 0 ? x with { Failed = true } : x).ToArray() },
            "extra-asset" => e with { Calls = e.Calls.Append(new([3], "call", Wallet, Router, 1, "0x", false)).ToArray() },
            _ => e
        };
        Assert.True(new UniswapV3Classifier().Classify(e, Wallet).IsFailure);
    }
    [Fact]
    public void DelegatecallValueIsNotCountedAndFailedDescendantsAreExcluded()
    {
        var e = Reference();
        e = e with { Calls = e.Calls.Concat([
            new ExecutionCall([4], "delegatecall", Wallet, Router, Output, "0x", false),
            new ExecutionCall([5], "call", Router, Wallet, 100, "0x", true),
            new ExecutionCall([5, 0], "call", Router, Wallet, 200, "0x", false)]).ToArray() };
        Assert.True(new UniswapV3Classifier().Classify(e, Wallet).IsSuccess);
    }
    [Fact]
    public void DisplayBoundaryRetainsReferencePrecisionAndRejectsOverflow()
    {
        Assert.Equal(115160.747406552094646927m, SwapEnrichmentWorker.ConvertAmount(Input, 18));
        Assert.Equal(17.721444116587579749m, SwapEnrichmentWorker.ConvertAmount(Output, 18));
        Assert.Throws<OverflowException>(() => SwapEnrichmentWorker.ConvertAmount(BigInteger.Pow(10, 80), 18));
        Assert.Throws<OverflowException>(() => SwapEnrichmentWorker.ConvertAmount(BigInteger.One, 29));
        Assert.Equal(0.0000000000000000000000000001m, SwapEnrichmentWorker.ConvertAmount(BigInteger.One, 28));
    }
    [Fact]
    public void DefaultOffAndOnlyEthereumCanBeQualified()
    {
        var o = new SwapEnrichmentOptions(); Assert.True(o.IsValid()); Assert.False(o.IsEnabled(SupportedChain.Ethereum));
        o.Enabled = true; Assert.False(o.IsEnabled(SupportedChain.Ethereum));
        o.ProviderQualified = true; Assert.True(o.IsEnabled(SupportedChain.Ethereum)); Assert.False(o.IsEnabled(SupportedChain.Base));
        o.EnabledChains.Add(SupportedChain.Base); Assert.False(o.IsValid());
    }
    [Fact]
    public async Task PublicEvidenceSingleflightIsIndependentOfRecipientsAndBlockVersions()
    {
        var provider = new FakeProvider(); var cache = new SwapEvidenceCache(provider, Options.Create(new SwapEnrichmentOptions()));
        var e = Reference(); var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        var first = cache.GetAsync(e.Identity, deadline, default); var second = cache.GetAsync(e.Identity, deadline, default);
        Assert.Same(first, second); Assert.Equal(1, provider.Calls);
        provider.Source.SetResult(Result<TransactionExecutionEvidence>.Success(e)); await Task.WhenAll(first, second);
        await cache.GetAsync(e.Identity with { Chain = SupportedChain.Base }, deadline, default); Assert.Equal(2, provider.Calls);
        await cache.GetAsync(e.Identity with { BlockHash = "new-block" }, deadline, default); Assert.Equal(3, provider.Calls);
    }
    private sealed class FakeProvider : ITransactionExecutionEvidenceProvider
    {
        public int Calls;
        public TaskCompletionSource<Result<TransactionExecutionEvidence>> Source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Result<TransactionExecutionEvidence>> GetAsync(ExecutionIdentity identity, CancellationToken cancellationToken)
        { Interlocked.Increment(ref Calls); return Source.Task; }
        public Task<Result<DateTimeOffset>> GetBlockTimeAsync(ExecutionIdentity identity, CancellationToken cancellationToken)
            => Task.FromResult(Result<DateTimeOffset>.Success(Reference().BlockTime));
    }
}
