using System.Globalization;
using System.Net;
using Domain.Common;

namespace Presentation.Telegram.Commands.Wallets;

/// <summary>
/// Shared HTML formatting helpers for Telegram command responses.
/// </summary>
public static class TelegramCommandText
{
    /// <summary>
    /// Encodes domain errors for HTML command responses.
    /// </summary>
    public static string FormatError(Error error)
        => HtmlEncode(error.Description);

    /// <summary>
    /// Formats decimal values using invariant culture for command responses.
    /// </summary>
    public static string FormatDecimal(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Encodes arbitrary text for Telegram HTML parse mode.
    /// </summary>
    public static string HtmlEncode(string value)
        => WebUtility.HtmlEncode(value);

    /// <summary>
    /// Formats boolean tracking flags as compact on/off text.
    /// </summary>
    public static string FormatOnOff(bool value) => value ? "on" : "off";

    /// <summary>
    /// Formats explicit chain filters, preserving empty-as-all display semantics.
    /// </summary>
    public static string FormatChains(IReadOnlyCollection<SupportedChain>? chains)
        => chains is null || chains.Count == 0
            ? "all"
            : string.Join(", ", chains.Select(chain => chain.ToString()));
}
