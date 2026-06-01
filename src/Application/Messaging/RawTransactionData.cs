namespace Application.Messaging;

public sealed record RawTransactionData(
    string TransactionHash,
    string? FromAddress,
    string? ToAddress,
    decimal Value);
