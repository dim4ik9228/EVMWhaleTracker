namespace Domain.Common;

/// <summary>Named diagnostic messages and structured log templates for Result.</summary>
internal static class ResultDiagnostics
{
    public const string SuccessWithError = "A successful result cannot contain an error.";
    public const string FailureWithoutError = "A failed result must contain an error.";
}
