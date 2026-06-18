namespace Presentation.Telegram.Commands.StartPayloads;

/// <summary>
/// Parses Telegram deep-link /start payloads used by notification edit links.
/// </summary>
public static class TelegramStartPayloadParser
{
    private const string EditWalletPrefix = "edit_wallet_";

    /// <summary>
    /// Extracts the wallet id from an edit-wallet deep-link payload in Guid N format.
    /// </summary>
    public static bool TryParseEditWalletPayload(string payload, out Guid walletId)
    {
        walletId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(payload)
            || !payload.StartsWith(EditWalletPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rawWalletId = payload[EditWalletPrefix.Length..];
        return Guid.TryParseExact(rawWalletId, "N", out walletId);
    }
}
