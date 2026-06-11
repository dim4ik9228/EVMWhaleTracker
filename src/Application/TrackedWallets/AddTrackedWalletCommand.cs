using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

/// <summary>
/// Registers a wallet for a Telegram user and seeds its notification preferences.
/// </summary>
public sealed record AddTrackedWalletCommand(
    long TelegramUserId,
    string RawAddress,
    decimal MinimumUsdValue = 0m,
    bool TrackIncomingTransfers = true,
    bool TrackOutgoingTransfers = true,
    bool TrackTokenTransfers = true,
    string? Alias = null,
    IReadOnlyCollection<SupportedChain>? EnabledChains = null) : IRequest<Result<TrackedWalletDetails>>;
