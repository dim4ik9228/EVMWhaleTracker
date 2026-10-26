using Domain.Common;
using Domain.Entities;
using Domain.ValueObjects;

namespace UnitTests.Domain;

public sealed class TrackedWalletTests
{
    private const string LowercaseAddress = "0x52908400098527886e0f7030069857d2e4169ee7";

    private static Address ChecksummedAddress
    {
        get
        {
            var result = Address.Normalize(LowercaseAddress);
            Assert.True(result.IsSuccess);
            return result.Value;
        }
    }

    [Fact]
    public void Create_WithAddressValueObject_SucceedsAndStoresConfiguration()
    {
        var result = TrackedWallet.Create(
            ChecksummedAddress,
            userId: 12345,
            minimumUsdValue: 1.5m,
            trackIncomingTransfers: true,
            trackOutgoingTransfers: false,
            trackTokenTransfers: true,
            alias: "MEXC Hot Wallet",
            enabledChains: [SupportedChain.Bsc, SupportedChain.Base]);

        Assert.True(result.IsSuccess);
        Assert.Equal(ChecksummedAddress, result.Value.Address);
        Assert.Equal(12345, result.Value.UserId);
        Assert.Equal(1.5m, result.Value.MinimumUsdValue);
        Assert.Equal("MEXC Hot Wallet", result.Value.Alias);
        Assert.True(result.Value.TrackIncomingTransfers);
        Assert.False(result.Value.TrackOutgoingTransfers);
        Assert.True(result.Value.TrackTokenTransfers);
        Assert.Equal([SupportedChain.Bsc, SupportedChain.Base], result.Value.EnabledChains);
        Assert.NotEqual(default, result.Value.Id);
        Assert.NotEqual(default, result.Value.CreatedAtUtc);
        Assert.Null(result.Value.UpdatedAtUtc);
    }

    [Fact]
    public void Create_WithLowercaseAddress_FailsBecauseTrackedWalletRequiresChecksummedInput()
    {
        var result = TrackedWallet.Create(LowercaseAddress, userId: 12345);

        Assert.True(result.IsFailure);
        Assert.Equal(Errors.Address.Checksum, result.Error);
    }

    [Fact]
    public void Create_WithLowercaseAddressAfterNormalization_Succeeds()
    {
        var normalizedAddress = Address.Normalize(LowercaseAddress);
        Assert.True(normalizedAddress.IsSuccess);

        var result = TrackedWallet.Create(normalizedAddress.Value, userId: 12345);

        Assert.True(result.IsSuccess);
        Assert.Equal(normalizedAddress.Value, result.Value.Address);
    }

    [Fact]
    public void Create_WithInvalidUserId_Fails()
    {
        var result = TrackedWallet.Create(ChecksummedAddress, userId: 0);

        Assert.True(result.IsFailure);
        Assert.Equal(Errors.TrackedWallet.UserIdMustBePositive, result.Error);
    }

    [Fact]
    public void FocusedUpdateMethods_ChangeSettingsAndSetUpdatedTimestamp()
    {
        var createResult = TrackedWallet.Create(ChecksummedAddress, userId: 12345);
        Assert.True(createResult.IsSuccess);

        var wallet = createResult.Value;

        Assert.True(wallet.UpdateMinimumUsdValue(10m).IsSuccess);
        Assert.True(wallet.UpdateAlias("Treasury").IsSuccess);
        Assert.True(wallet.UpdateTransferTracking(
            trackIncomingTransfers: false,
            trackOutgoingTransfers: true,
            trackTokenTransfers: false).IsSuccess);
        Assert.True(wallet.UpdateEnabledChains([SupportedChain.Ethereum, SupportedChain.Base]).IsSuccess);

        Assert.Equal(10m, wallet.MinimumUsdValue);
        Assert.Equal("Treasury", wallet.Alias);
        Assert.False(wallet.TrackIncomingTransfers);
        Assert.True(wallet.TrackOutgoingTransfers);
        Assert.False(wallet.TrackTokenTransfers);
        Assert.Equal([SupportedChain.Ethereum, SupportedChain.Base], wallet.EnabledChains);
        Assert.NotNull(wallet.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateMinimumUsdValue_WithNegativeValue_FailsAndKeepsExistingSettings()
    {
        var createResult = TrackedWallet.Create(ChecksummedAddress, userId: 12345, minimumUsdValue: 2m);
        Assert.True(createResult.IsSuccess);

        var wallet = createResult.Value;
        var updateResult = wallet.UpdateMinimumUsdValue(-1m);

        Assert.True(updateResult.IsFailure);
        Assert.Equal(Errors.TrackedWallet.MinimumUsdValueMustBeNonNegative, updateResult.Error);
        Assert.Equal(2m, wallet.MinimumUsdValue);
        Assert.Null(wallet.UpdatedAtUtc);
    }

    [Fact]
    public void SetChainTracking_DisablesChainFromAllChainsByExpandingExplicitChains()
    {
        var createResult = TrackedWallet.Create(ChecksummedAddress, userId: 12345);
        Assert.True(createResult.IsSuccess);

        var wallet = createResult.Value;
        var updateResult = wallet.SetChainTracking(SupportedChain.Base, enabled: false);

        Assert.True(updateResult.IsSuccess);
        Assert.NotEmpty(wallet.EnabledChains);
        Assert.DoesNotContain(SupportedChain.Base, wallet.EnabledChains);
        Assert.Contains(SupportedChain.Bsc, wallet.EnabledChains);
        Assert.Contains(SupportedChain.Ethereum, wallet.EnabledChains);
        Assert.NotNull(wallet.UpdatedAtUtc);
    }

    [Fact]
    public void SetChainTracking_EnablesChainAndCollapsesToAllChains()
    {
        var createResult = TrackedWallet.Create(
            ChecksummedAddress,
            userId: 12345,
            enabledChains: Enum.GetValues<SupportedChain>()
                .Where(chain => chain != SupportedChain.Base && chain != SupportedChain.Arc));
        Assert.True(createResult.IsSuccess);

        var wallet = createResult.Value;
        var updateResult = wallet.SetChainTracking(SupportedChain.Base, enabled: true);

        Assert.True(updateResult.IsSuccess);
        Assert.Empty(wallet.EnabledChains);
        Assert.NotNull(wallet.UpdatedAtUtc);
    }

    [Fact]
    public void SetChainTracking_DisablingLastExplicitChain_Fails()
    {
        var createResult = TrackedWallet.Create(
            ChecksummedAddress,
            userId: 12345,
            enabledChains: [SupportedChain.Base]);
        Assert.True(createResult.IsSuccess);

        var wallet = createResult.Value;
        var updateResult = wallet.SetChainTracking(SupportedChain.Base, enabled: false);

        Assert.True(updateResult.IsFailure);
        Assert.Equal(Errors.TrackedWallet.AtLeastOneEnabledChainRequired, updateResult.Error);
        Assert.Equal([SupportedChain.Base], wallet.EnabledChains);
        Assert.Null(wallet.UpdatedAtUtc);
    }

    [Fact]
    public void Create_WithNullAddress_Fails()
    {
        var result = TrackedWallet.Create((Address?)null, userId: 12345);

        Assert.True(result.IsFailure);
        Assert.Equal(Errors.TrackedWallet.AddressRequired, result.Error);
    }
}
