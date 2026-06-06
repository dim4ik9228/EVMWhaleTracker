using Domain.Entities;

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
    DateTimeOffset? UpdatedAtUtc)
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
            trackedWallet.UpdatedAtUtc);
}
