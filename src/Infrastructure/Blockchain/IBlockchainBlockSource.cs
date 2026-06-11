using Application.Messaging;

namespace Infrastructure.Blockchain;

/// <summary>
/// Source abstraction for optional native block scanning.
/// </summary>
public interface IBlockchainBlockSource
{
    Task<ulong> GetLatestBlockNumberAsync(CancellationToken cancellationToken);

    Task<RawBlockData?> GetBlockAsync(ulong blockNumber, CancellationToken cancellationToken);

    IAsyncEnumerable<ulong> StreamNewBlockNumbersAsync(CancellationToken cancellationToken);
}
