using Domain.Common;

namespace Infrastructure.AddressLabels;

/// <summary>Validation failures for reviewed address-label seed data.</summary>
internal static class AddressLabelSeedErrors
{
    public static readonly Error InvalidDocument = Error.Validation("AddressLabelSeed.Schema", "Address label seed JSON must be an object with schemaVersion, reviewStatus, scopePolicy, and entries.");
    public static readonly Error ReviewStatusRequired = Error.Validation("AddressLabelSeed.Schema", "Address label seed reviewStatus is required.");
    public static readonly Error CandidateNotAllowed = Error.Validation("AddressLabelSeed.Schema", "Candidate address label seeds require --allow-candidate.");
    public static readonly Error InvalidReviewStatus = Error.Validation("AddressLabelSeed.Schema", "Address label seed reviewStatus must be 'reviewed' or 'candidate'.");
    public static readonly Error ScopePolicyRequired = Error.Validation("AddressLabelSeed.Schema", "Address label seed scopePolicy object is required.");
    public static readonly Error EntriesRequired = Error.Validation("AddressLabelSeed.Schema", "Address label seed entries array is required.");
    public static Error UnsupportedSchemaVersion(int version) => Error.Validation("AddressLabelSeed.Schema", $"Address label seed schemaVersion must be {version}.");
    public static Error UnsupportedChain(string chain) => Error.Validation("AddressLabel.Chain", $"Unsupported chain '{chain}'.");
    public static Error UnsupportedKind(string kind) => Error.Validation("AddressLabel.Kind", $"Unsupported address label kind '{kind}'.");
    public static readonly Error SourceReferenceRequired = Error.Validation("AddressLabel.SourceReference", "Imported seed entries must include sourceReference.");
    public static readonly Error ExchangeMustBeGlobal = Error.Validation("AddressLabel.Chain", "Exchange seed entries must be global and omit chain.");
    public static Error ChainRequired(Domain.Entities.AddressLabelKind kind) => Error.Validation("AddressLabel.Chain", $"{kind} seed entries must include chain.");
    public static Error PropertyRequired(string propertyName) => Error.Validation("AddressLabelSeed.Required", $"Missing required JSON property '{propertyName}'.");
}
