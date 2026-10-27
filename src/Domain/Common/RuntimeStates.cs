namespace Domain.Common;

/// <summary>Stable persisted and lookup state tokens shared across runtime layers. Values are compatibility contracts.</summary>
public static class RuntimeStates
{
    /// <summary>Delivery state values; constants remain usable in patterns and translated predicates.</summary>
    public static class Delivery
    {
        public const string Pending = "Pending";
        public const string Sent = "Sent";
        public const string Failed = "Failed";
        public const string Cancelled = "Cancelled";
    }
    /// <summary>Profile state values; constants remain usable in patterns and translated predicates.</summary>
    public static class Profile
    {
        public const string Off = "Off";
        public const string Unresolved = "Unresolved";
        public const string Found = "Found";
        public const string Missing = "Missing";
        public const string Unavailable = "Unavailable";
    }
    /// <summary>Checkpoint state values; constants remain usable in patterns and translated predicates.</summary>
    public static class Checkpoint
    {
        public const string Starting = "Starting";
        public const string Connecting = "Connecting";
        public const string Live = "Live";
        public const string Recovering = "Recovering";
        public const string Active = "Active";
        public const string Degraded = "Degraded";
    }
    /// <summary>Normalization state values; constants remain usable in patterns and translated predicates.</summary>
    public static class Normalization
    {
        public const string Pending = "Pending";
        public const string Context = "Context";
        public const string SelfTradeSuppressed = "SelfTradeSuppressed";
        public const string Malformed = "Malformed";
        public const string UnsupportedPositionModule = "UnsupportedPositionModule";
        public const string Enriching = "Enriching";
        public const string Orphaned = "Orphaned";
        public const string UnsupportedCollateral = "UnsupportedCollateral";
        public const string UnsupportedNestedRedemption = "UnsupportedNestedRedemption";
        public const string UnsupportedRoutedRedemption = "UnsupportedRoutedRedemption";
        public const string Decoded = "Decoded";
        public const string Unsupported = "Unsupported";
        public const string Ambiguous = "Ambiguous";
        public const string Rejected = "Rejected";
    }
}
