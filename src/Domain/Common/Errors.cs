namespace Domain.Common;

public static class Errors
{
    public static class Address
    {
        public static readonly Error Empty = Error.Validation(
            "Address.Empty",
            "An EVM address is required.");

        public static readonly Error Length = Error.Validation(
            "Address.Length",
            "An EVM address must be 42 characters long, including the 0x prefix.");

        public static readonly Error Prefix = Error.Validation(
            "Address.Prefix",
            "An EVM address must start with the 0x prefix.");

        public static readonly Error Hex = Error.Validation(
            "Address.Hex",
            "An EVM address can only contain hexadecimal characters after the 0x prefix.");

        public static readonly Error Checksum = Error.Validation(
            "Address.Checksum",
            "The address must use the EIP-55 checksum format.");
    }

    public static class TrackedWallet
    {
        public static readonly Error AddressRequired = Error.Validation(
            "TrackedWallet.Address",
            "A tracked wallet address is required.");

        public static readonly Error UserIdMustBePositive = Error.Validation(
            "TrackedWallet.UserId",
            "Telegram user ids must be positive.");

        public static readonly Error MinimumTransactionValueMustBeNonNegative = Error.Validation(
            "TrackedWallet.MinimumTransactionValue",
            "Minimum transaction value cannot be negative.");

        public static readonly Error AlreadyTracked = Error.Validation(
            "TrackedWallet.AlreadyTracked",
            "This Telegram user already tracks the specified address.");

        public static readonly Error NotFound = Error.Validation(
            "TrackedWallet.NotFound",
            "No tracked wallet was found for this Telegram user and address.");
    }
}
