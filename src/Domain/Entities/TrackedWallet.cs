using Domain.Common;
using Domain.ValueObjects;

namespace Domain.Entities;

public sealed class TrackedWallet
{
    public Guid Id { get; private set; }

    public Address Address { get; private set; } = null!;

    public long UserId { get; private set; }

    public decimal MinimumTransactionValue { get; private set; }

    public bool TrackIncomingTransfers { get; private set; }

    public bool TrackOutgoingTransfers { get; private set; }

    public bool TrackTokenTransfers { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    private TrackedWallet()
    {
        // Required by EF Core.
    }

    private TrackedWallet(
        Guid id,
        Address address,
        long userId,
        decimal minimumTransactionValue,
        bool trackIncomingTransfers,
        bool trackOutgoingTransfers,
        bool trackTokenTransfers,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Address = address;
        UserId = userId;
        MinimumTransactionValue = minimumTransactionValue;
        TrackIncomingTransfers = trackIncomingTransfers;
        TrackOutgoingTransfers = trackOutgoingTransfers;
        TrackTokenTransfers = trackTokenTransfers;
        CreatedAtUtc = createdAtUtc;
    }

    public static Result<TrackedWallet> Create(
        Address? address,
        long userId,
        decimal minimumTransactionValue = 0m,
        bool trackIncomingTransfers = true,
        bool trackOutgoingTransfers = true,
        bool trackTokenTransfers = true)
    {
        if (address is null)
        {
            return Result<TrackedWallet>.Failure(Error.Validation("TrackedWallet.Address", "A tracked wallet address is required."));
        }

        if (userId <= 0)
        {
            return Result<TrackedWallet>.Failure(Error.Validation("TrackedWallet.UserId", "Telegram user ids must be positive."));
        }

        if (minimumTransactionValue < 0m)
        {
            return Result<TrackedWallet>.Failure(Error.Validation("TrackedWallet.MinimumTransactionValue", "Minimum transaction value cannot be negative."));
        }

        return Result<TrackedWallet>.Success(new TrackedWallet(
            Guid.NewGuid(),
            address,
            userId,
            minimumTransactionValue,
            trackIncomingTransfers,
            trackOutgoingTransfers,
            trackTokenTransfers,
            DateTimeOffset.UtcNow));
    }

    public static Result<TrackedWallet> Create(
        string address,
        long userId,
        decimal minimumTransactionValue = 0m,
        bool trackIncomingTransfers = true,
        bool trackOutgoingTransfers = true,
        bool trackTokenTransfers = true)
    {
        if (userId <= 0)
        {
            return Result<TrackedWallet>.Failure(Error.Validation("TrackedWallet.UserId", "Telegram user ids must be positive."));
        }

        if (minimumTransactionValue < 0m)
        {
            return Result<TrackedWallet>.Failure(Error.Validation("TrackedWallet.MinimumTransactionValue", "Minimum transaction value cannot be negative."));
        }

        var addressResult = Address.Create(address);
        if (addressResult.IsFailure)
        {
            return Result<TrackedWallet>.Failure(addressResult.Error);
        }

        return Create(
            addressResult.Value,
            userId,
            minimumTransactionValue,
            trackIncomingTransfers,
            trackOutgoingTransfers,
            trackTokenTransfers);
    }

    public Result UpdateTrackingConfiguration(
        decimal minimumTransactionValue,
        bool trackIncomingTransfers,
        bool trackOutgoingTransfers,
        bool trackTokenTransfers)
    {
        if (minimumTransactionValue < 0m)
        {
            return Result.Failure(Error.Validation("TrackedWallet.MinimumTransactionValue", "Minimum transaction value cannot be negative."));
        }

        MinimumTransactionValue = minimumTransactionValue;
        TrackIncomingTransfers = trackIncomingTransfers;
        TrackOutgoingTransfers = trackOutgoingTransfers;
        TrackTokenTransfers = trackTokenTransfers;
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        return Result.Success();
    }
}
