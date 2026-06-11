namespace Application.Messaging;

/// <summary>
/// Raw native transaction envelope used by the optional native-transfer scanner.
/// </summary>
public sealed record RawTransactionData(
    string TransactionHash,
    string? FromAddress,
    string? ToAddress,
    decimal Value);
