namespace Infrastructure.Polymarket;

/// <summary>Provider proof is a code-reviewed capability, never a switch that can bypass missing execution identity.</summary>
public static class PolymarketCapabilities
{
    public static bool ActivityVerified => true;
}
