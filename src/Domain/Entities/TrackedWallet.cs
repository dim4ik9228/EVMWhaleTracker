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
    /// Updates the minimum USD threshold required before a wallet activity can notify.
    /// </summary>
    public Result UpdateMinimumUsdValue(decimal minimumUsdValue)
    {
        if (minimumUsdValue < 0m)
        {
            return Result.Failure(Errors.TrackedWallet.MinimumUsdValueMustBeNonNegative);
        }

        MinimumUsdValue = minimumUsdValue;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Updates the user-facing wallet alias. Null or whitespace clears the alias.
    /// </summary>
    public Result UpdateAlias(string? alias)
    {
        var aliasResult = NormalizeAlias(alias);
        if (aliasResult.IsFailure)
        {
            return Result.Failure(aliasResult.Error);
        }

        Alias = aliasResult.Value;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Updates direction and token-transfer tracking flags.
    /// </summary>
    public Result UpdateTransferTracking(
        bool trackIncomingTransfers,
        bool trackOutgoingTransfers,
        bool trackTokenTransfers)
    {
        TrackIncomingTransfers = trackIncomingTransfers;
        TrackOutgoingTransfers = trackOutgoingTransfers;
        TrackTokenTransfers = trackTokenTransfers;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Replaces the explicit chain allow-list. An empty collection means all supported chains.
    /// </summary>
    public Result UpdateEnabledChains(IEnumerable<SupportedChain> enabledChains)
    {
        _enabledChainsJson = SerializeEnabledChains(enabledChains);
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Enables or disables a single chain while preserving the empty-list-as-all-chains storage convention.
    /// </summary>
    public Result SetChainTracking(SupportedChain chain, bool enabled)
    {
        var allSupportedChains = Enum.GetValues<SupportedChain>()
            .Where(candidate => candidate != SupportedChain.Arc)
            .ToHashSet();
        var enabledChains = EnabledChains.Count == 0
            ? allSupportedChains.ToHashSet()
            : EnabledChains.ToHashSet();

        if (enabled)
        {
            enabledChains.Add(chain);
        }
        else
        {
            enabledChains.Remove(chain);
        }

        if (enabledChains.Count == 0)
        {
            return Result.Failure(Errors.TrackedWallet.AtLeastOneEnabledChainRequired);
        }

        _enabledChainsJson = enabledChains.SetEquals(allSupportedChains)
            ? SerializeEnabledChains([])
            : SerializeEnabledChains(enabledChains);
        Touch();
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

        return enabledChains.Distinct().OrderBy(chain => (int)chain).ToList();
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

    private void Touch()
    {
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
