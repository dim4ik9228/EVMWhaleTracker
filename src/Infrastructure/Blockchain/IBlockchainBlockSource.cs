using Application.Messaging;

namespace Infrastructure.Blockchain;

public interface IBlockchainBlockSource
{
    Task<ulong> GetLatestBlockNumberAsync(CancellationToken cancellationToken);

    Task<RawBlockData?> GetBlockAsync(ulong blockNumber, CancellationToken cancellationToken);

    IAsyncEnumerable<ulong> StreamNewBlockNumbersAsync(CancellationToken cancellationToken);
}
