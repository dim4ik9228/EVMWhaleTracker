namespace Presentation.Telegram.Commands;

/// <summary>Telegram command and callback wire tokens shared by emitters and parsers. Preserve values for existing messages.</summary>
internal static class TelegramInteractionTokens
{
    public const string EditWalletPrefix = "edit_wallet_";
    internal static class Commands
    {
        public const string Add = "add";
        public const string List = "list";
        public const string Remove = "remove";
        public const string Start = "start";
        public const string Help = "help";
    }
    internal static class Callbacks
    {
        public const string Polymarket = "pm";
        public const string HyperliquidToggle = "hc";
        public const string Wallet = "tw";
        public const string Version = "v1";
        public const string Toggle = "t";
        public const string Bind = "b";
        public const string Minimum = "m";
        public const string Alias = "a";
        public const string Source = "s";
        public const string Predictions = "p";
        public const string Hyperliquid = "h";
        public const string Overview = "o";
        public const string Networks = "n";
        public const string Rename = "r";
        public const string Delete = "d";
        public const string DeleteConfirm = "dc";
        public const string Close = "b";
        public const string DeselectNetworks = "na";
        public const string NetworkToggle = "nt";
        public const string Enabled = "1";
        public const string Disabled = "0";
    }
}
