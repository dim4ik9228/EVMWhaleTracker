using Domain.Entities;
using Domain.Common;

namespace Application.TrackedWallets;

/// <summary>
/// Read model returned to Telegram command flows after tracked-wallet operations.
/// </summary>
public sealed record TrackedWalletDetails(
    Guid Id,
    string Address,
    long UserId,
    decimal MinimumUsdValue,
    bool TrackIncomingTransfers,
    bool TrackOutgoingTransfers,
    bool TrackTokenTransfers,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string? Alias = null,
    IReadOnlyCollection<SupportedChain>? EnabledChains = null)
{
    /// <summary>
    /// Projects the domain aggregate into a command-facing read model.
    /// </summary>
    public static TrackedWalletDetails FromEntity(TrackedWallet trackedWallet)
        => new(
            trackedWallet.Id,
            trackedWallet.Address.Value,
            trackedWallet.UserId,
            trackedWallet.MinimumUsdValue,
            trackedWallet.TrackIncomingTransfers,
            trackedWallet.TrackOutgoingTransfers,
            trackedWallet.TrackTokenTransfers,
            trackedWallet.CreatedAtUtc,
            trackedWallet.UpdatedAtUtc,
            trackedWallet.Alias,
            trackedWallet.EnabledChains.ToArray());
}
