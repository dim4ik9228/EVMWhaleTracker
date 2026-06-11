using System.Text.Json;
using Domain.Common;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// User-owned tracking aggregate with address, alert thresholds, aliases, direction flags, and chain restrictions.
/// </summary>
public sealed class TrackedWallet
{
    private const int MaxAliasLength = 100;
    private string _enabledChainsJson = "[]";

    public Guid Id { get; private set; }

    public Address Address { get; private set; } = null!;

    public long UserId { get; private set; }

    public decimal MinimumUsdValue { get; private set; }

    public string? Alias { get; private set; }

    public bool TrackIncomingTransfers { get; private set; }

    public bool TrackOutgoingTransfers { get; private set; }

    public bool TrackTokenTransfers { get; private set; }

    /// <summary>
    /// Chains where this wallet should emit notifications. An empty set means all supported chains.
    /// </summary>
    public IReadOnlyCollection<SupportedChain> EnabledChains => DeserializeEnabledChains(_enabledChainsJson);

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
        decimal minimumUsdValue,
        bool trackIncomingTransfers,
        bool trackOutgoingTransfers,
        bool trackTokenTransfers,
        string? alias,
        IReadOnlyCollection<SupportedChain> enabledChains,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Address = address;
        UserId = userId;
        MinimumUsdValue = minimumUsdValue;
        Alias = alias;
        TrackIncomingTransfers = trackIncomingTransfers;
        TrackOutgoingTransfers = trackOutgoingTransfers;
        TrackTokenTransfers = trackTokenTransfers;
        _enabledChainsJson = SerializeEnabledChains(enabledChains);
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// Creates a tracked wallet from a normalized address value object.
    /// </summary>
    public static Result<TrackedWallet> Create(
        Address? address,
        long userId,
        decimal minimumUsdValue = 0m,
        bool trackIncomingTransfers = true,
        bool trackOutgoingTransfers = true,
        bool trackTokenTransfers = true,
        string? alias = null,
        IEnumerable<SupportedChain>? enabledChains = null)
    {
        if (address is null)
        {
            return Result<TrackedWallet>.Failure(Errors.TrackedWallet.AddressRequired);
        }

        if (userId <= 0)
        {
            return Result<TrackedWallet>.Failure(Errors.TrackedWallet.UserIdMustBePositive);
        }

        if (minimumUsdValue < 0m)
        {
            return Result<TrackedWallet>.Failure(Errors.TrackedWallet.MinimumUsdValueMustBeNonNegative);
        }

        var aliasResult = NormalizeAlias(alias);
        if (aliasResult.IsFailure)
        {
            return Result<TrackedWallet>.Failure(aliasResult.Error);
        }

        var enabledChainsResult = NormalizeEnabledChains(enabledChains);

        return Result<TrackedWallet>.Success(new TrackedWallet(
            Guid.NewGuid(),
            address,
            userId,
            minimumUsdValue,
            trackIncomingTransfers,
            trackOutgoingTransfers,
            trackTokenTransfers,
            aliasResult.Value,
            enabledChainsResult,
            DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Creates a tracked wallet from raw address text.
    /// </summary>
    public static Result<TrackedWallet> Create(
        string address,
        long userId,
        decimal minimumUsdValue = 0m,
        bool trackIncomingTransfers = true,
        bool trackOutgoingTransfers = true,
        bool trackTokenTransfers = true,
        string? alias = null,
        IEnumerable<SupportedChain>? enabledChains = null)
    {
        if (userId <= 0)
        {
            return Result<TrackedWallet>.Failure(Errors.TrackedWallet.UserIdMustBePositive);
        }

        if (minimumUsdValue < 0m)
        {
            return Result<TrackedWallet>.Failure(Errors.TrackedWallet.MinimumUsdValueMustBeNonNegative);
        }

        var aliasResult = NormalizeAlias(alias);
        if (aliasResult.IsFailure)
        {
            return Result<TrackedWallet>.Failure(aliasResult.Error);
        }

        var addressResult = Address.Create(address);
        if (addressResult.IsFailure)
        {
            return Result<TrackedWallet>.Failure(addressResult.Error);
        }

        return Create(
            addressResult.Value,
            userId,
            minimumUsdValue,
            trackIncomingTransfers,
            trackOutgoingTransfers,
            trackTokenTransfers,
            aliasResult.Value,
            enabledChains);
    }

    /// <summary>
    /// Updates alert settings while preserving existing alias and chain restrictions when those values are omitted.
    /// </summary>
    public Result UpdateTrackingConfiguration(
        decimal minimumUsdValue,
        bool trackIncomingTransfers,
        bool trackOutgoingTransfers,
        bool trackTokenTransfers,
        string? alias = null,
        IEnumerable<SupportedChain>? enabledChains = null)
    {
        if (minimumUsdValue < 0m)
        {
            return Result.Failure(Errors.TrackedWallet.MinimumUsdValueMustBeNonNegative);
        }

        var aliasResult = NormalizeAlias(alias);
        if (aliasResult.IsFailure)
        {
            return Result.Failure(aliasResult.Error);
        }

        MinimumUsdValue = minimumUsdValue;
        TrackIncomingTransfers = trackIncomingTransfers;
        TrackOutgoingTransfers = trackOutgoingTransfers;
        TrackTokenTransfers = trackTokenTransfers;

        if (alias is not null)
        {
            Alias = aliasResult.Value;
        }

        if (enabledChains is not null)
        {
            _enabledChainsJson = SerializeEnabledChains(enabledChains);
        }

        UpdatedAtUtc = DateTimeOffset.UtcNow;

        return Result.Success();
    }

    private static Result<string?> NormalizeAlias(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return Result<string?>.Success(null);
        }

        var normalizedAlias = alias.Trim();
        if (normalizedAlias.Length > MaxAliasLength)
        {
            return Result<string?>.Failure(Errors.TrackedWallet.AliasTooLong);
        }

        return Result<string?>.Success(normalizedAlias);
    }

    private static List<SupportedChain> NormalizeEnabledChains(IEnumerable<SupportedChain>? enabledChains)
    {
        if (enabledChains is null)
        {
            return [];
        }

        return enabledChains.Distinct().ToList();
    }

    private static string SerializeEnabledChains(IEnumerable<SupportedChain>? enabledChains)
        => JsonSerializer.Serialize(NormalizeEnabledChains(enabledChains), (JsonSerializerOptions?)null);

    private static List<SupportedChain> DeserializeEnabledChains(string? enabledChainsJson)
    {
        if (string.IsNullOrWhiteSpace(enabledChainsJson))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<SupportedChain>>(enabledChainsJson, (JsonSerializerOptions?)null) ?? [];
    }
}
