using Domain.ValueObjects;

namespace Infrastructure.Blockchain;

public sealed record Erc20TransferLogFilter(
    IReadOnlyList<object?> Topics);

public static class Erc20TransferLogFilterBuilder
{
    public const int DefaultAddressBatchSize = 100;

    public static IReadOnlyList<Erc20TransferLogFilter> Build(
        IReadOnlyCollection<Address> trackedAddresses,
        int addressBatchSize = DefaultAddressBatchSize)
    {
        ArgumentNullException.ThrowIfNull(trackedAddresses);

        if (addressBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(addressBatchSize), "Address batch size must be positive.");
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

    public static string ToAddressTopic(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return $"0x{new string('0', 24)}{address.Value[2..].ToLowerInvariant()}";
    }
}
