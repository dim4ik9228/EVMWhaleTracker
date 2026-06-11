using System.Globalization;
using System.Numerics;
using Application.Messaging;
using Domain.Common;
using Domain.ValueObjects;

namespace Infrastructure.Blockchain;

/// <summary>
/// Decodes standard ERC-20/BEP-20 Transfer logs into typed transfer data.
/// </summary>
public sealed class Erc20TransferLogParser
{
    /// <summary>
    /// Keccak-256 event signature for Transfer(address,address,uint256).
    /// </summary>
    public const string TransferEventTopic = "0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef";

    /// <summary>
    /// Attempts to parse a raw log as an ERC-20 transfer.
    /// </summary>
    public bool TryParse(RawLogData log, SupportedChain chain, out Erc20TransferLog transferLog)
    {
        ArgumentNullException.ThrowIfNull(log);

        transferLog = default!;
        if (log.Topics.Count < 3 ||
            !string.Equals(log.Topics[0], TransferEventTopic, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryDecodeAddressTopic(log.Topics[1], out var fromAddress) ||
            !TryDecodeAddressTopic(log.Topics[2], out var toAddress) ||
            !TryDecodeUint256(log.Data, out var rawAmount))
        {
            return false;
        }

        transferLog = new Erc20TransferLog(
            chain,
            log.TransactionHash,
            log.ContractAddress,
            fromAddress,
            toAddress,
            rawAmount);

        return true;
    }

    private static bool TryDecodeAddressTopic(string topic, out Address address)
    {
        address = default!;
        var normalizedTopic = NormalizeHex(topic);
        if (normalizedTopic.Length != 64)
        {
            return false;
        }

        var addressValue = $"0x{normalizedTopic[^40..]}";
        var addressResult = Address.Normalize(addressValue);
        if (addressResult.IsFailure)
        {
            return false;
        }

        address = addressResult.Value;
        return true;
    }

    private static bool TryDecodeUint256(string data, out BigInteger value)
    {
        value = BigInteger.Zero;
        var normalizedData = NormalizeHex(data);
        if (normalizedData.Length == 0 || normalizedData.Length > 64)
        {
            return false;
        }

        return BigInteger.TryParse(
            $"0{normalizedData}",
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static string NormalizeHex(string value)
        => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value[2..]
            : value;
}

/// <summary>
/// Parsed ERC-20 transfer with decoded indexed addresses and raw token amount.
/// </summary>
public sealed record Erc20TransferLog(
    SupportedChain Chain,
    string TransactionHash,
    string TokenContractAddress,
    Address FromAddress,
    Address ToAddress,
    BigInteger RawAmount);
