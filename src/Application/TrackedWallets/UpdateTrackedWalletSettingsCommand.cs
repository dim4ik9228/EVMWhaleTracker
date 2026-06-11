using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

/// <summary>
/// Updates alert preferences for an existing tracked wallet.
/// </summary>
public sealed record UpdateTrackedWalletSettingsCommand(
    long TelegramUserId,
    string RawAddress,
    decimal MinimumTransactionValue,
    bool TrackIncomingTransfers,
    bool TrackOutgoingTransfers,
    bool TrackTokenTransfers,
    string? Alias = null,
    IReadOnlyCollection<SupportedChain>? EnabledChains = null) : IRequest<Result<TrackedWalletDetails>>;
