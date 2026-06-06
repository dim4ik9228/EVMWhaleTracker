using Application.Abstractions;
using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

public sealed class ListTrackedWalletsQueryHandler(ITrackedWalletRepository trackedWalletRepository)
    : IRequestHandler<ListTrackedWalletsQuery, Result<IReadOnlyList<TrackedWalletDetails>>>
{
    public async Task<Result<IReadOnlyList<TrackedWalletDetails>>> Handle(
        ListTrackedWalletsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trackedWallets = await trackedWalletRepository.ListByUserIdAsync(
            request.TelegramUserId,
            cancellationToken);

        var response = trackedWallets
            .OrderBy(wallet => wallet.Address.Value, StringComparer.Ordinal)
            .ThenBy(wallet => wallet.CreatedAtUtc)
            .Select(TrackedWalletDetails.FromEntity)
            .ToArray();

        return Result<IReadOnlyList<TrackedWalletDetails>>.Success(response);
    }
}
