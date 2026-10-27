using Domain.ValueObjects;

namespace Infrastructure.Blockchain;

/// <summary>
/// Topic-only websocket filter for ERC-20 Transfer logs.
/// </summary>
public sealed record Erc20TransferLogFilter(
    IReadOnlyList<object?> Topics);

/// <summary>
/// Builds incoming and outgoing Transfer-log filters for tracked wallet addresses.
/// </summary>
public static class Erc20TransferLogFilterBuilder
{
    /// <summary>
    /// Maximum number of tracked address topics included in one subscription filter.
    /// </summary>
    public const int DefaultAddressBatchSize = 100;

    /// <summary>
    /// Creates paired filters for tracked-wallet receivers and senders.
    /// </summary>
    public static IReadOnlyList<Erc20TransferLogFilter> Build(
        IReadOnlyCollection<Address> trackedAddresses,
        int addressBatchSize = DefaultAddressBatchSize)
    {
        ArgumentNullException.ThrowIfNull(trackedAddresses);

        if (addressBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(addressBatchSize), Erc20TransferLogFilterBuilderDiagnostics.AddressBatchSizeMustBePositive);
        }

        if (trackedAddresses.Count == 0)
        {
            return Array.Empty<Erc20TransferLogFilter>();
        }

        var addressTopics = trackedAddresses
            .Select(ToAddressTopic)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var filters = new List<Erc20TransferLogFilter>();
        foreach (var batch in addressTopics.Chunk(addressBatchSize))
        {
            filters.Add(new Erc20TransferLogFilter(
                new object?[]
                {
                    Erc20TransferLogParser.TransferEventTopic,
                    null,
                    batch
                }));

            filters.Add(new Erc20TransferLogFilter(
                new object?[]
                {
                    Erc20TransferLogParser.TransferEventTopic,
                    batch,
                    null
                }));
        }

        return filters;
    }

    /// <summary>
    /// Converts a 20-byte address into the 32-byte indexed-topic representation used by EVM logs.
    /// </summary>
    public static string ToAddressTopic(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return $"0x{new string('0', 24)}{address.Value[2..].ToLowerInvariant()}";
    }
}
