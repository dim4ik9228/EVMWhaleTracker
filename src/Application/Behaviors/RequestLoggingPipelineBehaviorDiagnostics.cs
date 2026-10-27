namespace Application.Behaviors;

/// <summary>Named diagnostic messages and structured log templates for RequestLoggingPipelineBehavior.</summary>
internal static class RequestLoggingPipelineBehaviorDiagnostics
{
    public const string ProcessingRequest = "Processing request {requestName}";
    public const string CompletedRequest = "Completed request {RequestName}";
    public const string CompletedRequestWithError = "Completed request {RequestName} with error";
    public const string ErrorProperty = "Error";
}
