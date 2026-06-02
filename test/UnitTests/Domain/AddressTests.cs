using Domain.ValueObjects;

namespace UnitTests.Domain;

public sealed class AddressTests
{
    [Fact]
    public void Create_WithValidChecksumAddress_Succeeds()
    {
        const string address = "0x52908400098527886E0F7030069857D2E4169EE7";

        var result = Address.Create(address);

        Assert.True(result.IsSuccess);
        Assert.Equal(address, result.Value.Value);
    }

    [Fact]
    public void Create_WithNonChecksummedAddress_Fails()
    {
        const string address = "0x52908400098527886e0f7030069857d2e4169ee7";

        var result = Address.Create(address);

        Assert.True(result.IsFailure);
        Assert.Equal("Address.Checksum", result.Error.Code);
    }

    [Fact]
    public void Normalize_WithLowercaseAddress_ReturnsChecksummedValue()
    {
        const string lowercaseAddress = "0x52908400098527886e0f7030069857d2e4169ee7";

        var result = Address.Normalize(lowercaseAddress);

        Assert.True(result.IsSuccess);
        Assert.Equal("0x52908400098527886E0F7030069857D2E4169EE7", result.Value.Value);
    }
}
