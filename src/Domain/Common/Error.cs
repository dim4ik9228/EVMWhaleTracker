namespace Domain.Common;

/// <summary>
/// Stable error code and human-readable description used by Result-based flows.
/// </summary>
public sealed record Error(string Code, string Description)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error Validation(string code, string description) => new(code, description);
}
