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
            minimumTransactionValue: 1.5m,
            trackIncomingTransfers: true,
            trackOutgoingTransfers: false,
            trackTokenTransfers: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(ChecksummedAddress, result.Value.Address);
        Assert.Equal(12345, result.Value.UserId);
        Assert.Equal(1.5m, result.Value.MinimumTransactionValue);
        Assert.True(result.Value.TrackIncomingTransfers);
        Assert.False(result.Value.TrackOutgoingTransfers);
        Assert.True(result.Value.TrackTokenTransfers);
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
    public void UpdateTrackingConfiguration_ChangesSettingsAndSetsUpdatedTimestamp()
    {
        var createResult = TrackedWallet.Create(ChecksummedAddress, userId: 12345);
        Assert.True(createResult.IsSuccess);

        var wallet = createResult.Value;
        var updateResult = wallet.UpdateTrackingConfiguration(
            minimumTransactionValue: 10m,
            trackIncomingTransfers: false,
            trackOutgoingTransfers: true,
            trackTokenTransfers: false);

        Assert.True(updateResult.IsSuccess);
        Assert.Equal(10m, wallet.MinimumTransactionValue);
        Assert.False(wallet.TrackIncomingTransfers);
        Assert.True(wallet.TrackOutgoingTransfers);
        Assert.False(wallet.TrackTokenTransfers);
        Assert.NotNull(wallet.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateTrackingConfiguration_WithNegativeValue_FailsAndKeepsExistingSettings()
    {
        var createResult = TrackedWallet.Create(ChecksummedAddress, userId: 12345, minimumTransactionValue: 2m);
        Assert.True(createResult.IsSuccess);

        var wallet = createResult.Value;
        var updateResult = wallet.UpdateTrackingConfiguration(
            minimumTransactionValue: -1m,
            trackIncomingTransfers: false,
            trackOutgoingTransfers: false,
            trackTokenTransfers: false);

        Assert.True(updateResult.IsFailure);
        Assert.Equal(Errors.TrackedWallet.MinimumTransactionValueMustBeNonNegative, updateResult.Error);
        Assert.Equal(2m, wallet.MinimumTransactionValue);
        Assert.True(wallet.TrackIncomingTransfers);
        Assert.True(wallet.TrackOutgoingTransfers);
        Assert.True(wallet.TrackTokenTransfers);
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
