namespace Presentation.Telegram.Services;

/// <summary>Named presentation text and formatting templates for TelegramBotUsernameProvider.</summary>
internal static class TelegramBotUsernameProviderText
{
    public const string UsernameMissing = "Telegram getMe did not return a bot username; edit deep links will be omitted.";
    public const string UsernameLookupFailed = "Could not resolve Telegram bot username via getMe; edit deep links will be omitted.";
}
