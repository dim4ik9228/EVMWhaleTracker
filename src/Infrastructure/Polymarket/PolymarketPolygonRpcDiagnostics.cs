namespace Infrastructure.Polymarket;

/// <summary>Named diagnostic messages and structured log templates for PolymarketPolygonRpc.</summary>
internal static class PolymarketPolygonRpcDiagnostics
{
    public const string PolymarketEndpointIsNotPolygon = "Polymarket endpoint is not Polygon 137.";
    public const string FinalizedBoundaryUnavailable = "Polygon finalized boundary unavailable; finalization remains paused.";
    public const string InvalidPolygonLogsResponse = "Invalid Polygon logs response.";
    public const string PolygonAcquisitionIsOff = "Polygon acquisition is off.";
    public const string ProviderThrottled = "Polygon provider throttled; shared RPC cooldown is active.";
    public static string ProviderHttpFailure(int statusCode) => $"Polygon provider HTTP {statusCode}; check network access and credentials.";
    public const string PolygonResponseExceedsConfiguredLimit = "Polygon response exceeds configured limit.";
    public const string MoreThan = "more than";
    public const string BlockRange = "block range";
    public const string ResponseSize = "response size";
    public static string ProviderRpcFailure(string method, int code) => $"Polygon {method} rejected (RPC code {code}); recovery remains pending.";
}
