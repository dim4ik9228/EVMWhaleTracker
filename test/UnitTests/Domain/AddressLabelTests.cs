using Domain.Common;
using Domain.Entities;
using Domain.ValueObjects;

namespace UnitTests.Domain;

public sealed class AddressLabelTests
{
    private const string AddressValue = "0x52908400098527886e0f7030069857d2e4169ee7";

    [Fact]
    public void Create_NormalizesAddressAndTrimsLabel()
    {
        var result = AddressLabel.Create(
            AddressValue,
            SupportedChain.Bsc,
            "  MEXC  ",
            AddressLabelKind.Exchange,
            AddressLabelSource.SeedFile,
            "  labels.addresses:bsc  ",
            priority: 90);

        Assert.True(result.IsSuccess);
        Assert.Equal("0x52908400098527886E0F7030069857D2E4169EE7", result.Value.Address.Value);
        Assert.Equal(SupportedChain.Bsc, result.Value.Chain);
        Assert.Equal("MEXC", result.Value.Label);
        Assert.Equal("labels.addresses:bsc", result.Value.SourceReference);
    }

    [Fact]
    public void Create_RejectsEmptyLabel()
    {
        var address = Address.Normalize(AddressValue).Value;

        var result = AddressLabel.Create(
            address,
            chain: null,
            label: " ",
            AddressLabelKind.Exchange,
            AddressLabelSource.Manual);

        Assert.True(result.IsFailure);
        Assert.Equal(Errors.AddressLabel.LabelRequired, result.Error);
    }

    [Fact]
    public void Update_RejectsLabelsOverLengthLimit()
    {
        var label = AddressLabel.Create(
            AddressValue,
            chain: null,
            label: "MEXC",
            AddressLabelKind.Exchange,
            AddressLabelSource.Manual).Value;

        var result = label.Update(
            new string('x', AddressLabel.MaxLabelLength + 1),
            AddressLabelKind.Exchange,
            AddressLabelSource.Manual,
            sourceReference: null,
            priority: 100,
            isEnabled: true);

        Assert.True(result.IsFailure);
        Assert.Equal(Errors.AddressLabel.LabelTooLong, result.Error);
    }
}
