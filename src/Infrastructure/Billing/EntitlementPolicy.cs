using System.Collections.Concurrent;
using Application.Billing;
using Microsoft.Extensions.Options;

namespace Infrastructure.Billing;

/// <summary>Fail-closed, time-bounded permission snapshots; a source can serve multiple independent owners.</summary>
public sealed class EntitlementPolicy(IOptions<BillingOptions> options, TimeProvider clock) : IEntitlementPolicy
{
    private readonly ConcurrentDictionary<Guid, WalletEntitlement> entries = new();
    private readonly Dictionary<long, (long Revision, DateTimeOffset Stamp)> published = new();
    private readonly Lock gate = new();
    public bool EnforcementEnabled => options.Value.EnforcementEnabled;
    public WalletEntitlement? Get(Guid walletId) => entries.GetValueOrDefault(walletId);
    public bool Allows(Guid walletId, TrackingMode mode, DateTimeOffset? activityAt = null, long? revision = null)
    {
        if (!EnforcementEnabled) return true;
        var e = Get(walletId);
        return e is not null && clock.GetUtcNow() < e.ValidUntil && (revision is null || revision == e.Revision)
            && (activityAt is null || activityAt >= e.ActiveSince)
            && (mode != TrackingMode.Polymarket || e.PolymarketUntil is null || clock.GetUtcNow() < e.PolymarketUntil)
            && (mode != TrackingMode.Hyperliquid || e.HyperliquidUntil is null || clock.GetUtcNow() < e.HyperliquidUntil)
            && (mode switch { TrackingMode.Hyperliquid => e.Hyperliquid, TrackingMode.Polymarket => e.Polymarket, TrackingMode.Evm => e.Evm, _ => false });
    }
    public void Replace(long owner, long accountRevision, DateTimeOffset stamp, IEnumerable<WalletEntitlement> replacement)
    {
        lock (gate)
        {
        if (published.TryGetValue(owner, out var previous) && (previous.Revision > accountRevision || previous.Revision == accountRevision && previous.Stamp > stamp)) return;
        published[owner] = (accountRevision, stamp);
        var next = replacement.ToDictionary(x => x.WalletId);
        foreach (var old in entries.Values.Where(x => x.UserId == owner && !next.ContainsKey(x.WalletId))) entries.TryRemove(old.WalletId, out _);
        foreach (var item in next.Values) entries[item.WalletId] = item;
        }
    }
}
