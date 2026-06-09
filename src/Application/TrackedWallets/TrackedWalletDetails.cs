using Domain.Entities;
using Domain.Common;

namespace Application.TrackedWallets;

public sealed record TrackedWalletDetails(
    Guid Id,
    string Address,
    long UserId,
    decimal MinimumTransactionValue,
    bool TrackIncomingTransfers,
    bool TrackOutgoingTransfers,
    bool TrackTokenTransfers,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string? Alias = null,
    IReadOnlyCollection<SupportedChain>? EnabledChains = null)
{
    public static TrackedWalletDetails FromEntity(TrackedWallet trackedWallet)
        => new(
            trackedWallet.Id,
            trackedWallet.Address.Value,
            trackedWallet.UserId,
            trackedWallet.MinimumTransactionValue,
            trackedWallet.TrackIncomingTransfers,
            trackedWallet.TrackOutgoingTransfers,
            trackedWallet.TrackTokenTransfers,
            trackedWallet.CreatedAtUtc,
            trackedWallet.UpdatedAtUtc,
            trackedWallet.Alias,
            trackedWallet.EnabledChains.ToArray());
}
