using Domain.ValueObjects;
using Infrastructure.Blockchain;

namespace UnitTests.Infrastructure;

public sealed class Erc20TransferLogFilterBuilderTests
{
    [Fact]
    public void Build_CreatesIncomingAndOutgoingSubscriptionFilters()
    {
        var trackedAddress = CreateAddress("0x52908400098527886E0F7030069857D2E4169EE7");

        var filters = Erc20TransferLogFilterBuilder.Build([trackedAddress]);

        Assert.Equal(2, filters.Count);
        Assert.All(filters, filter =>
        {
            Assert.Equal(Erc20TransferLogParser.TransferEventTopic, filter.Topics[0]);
        });

        Assert.Null(filters[0].Topics[1]);
        Assert.Equal([Erc20TransferLogFilterBuilder.ToAddressTopic(trackedAddress)], Assert.IsType<string[]>(filters[0].Topics[2]));

        Assert.Equal([Erc20TransferLogFilterBuilder.ToAddressTopic(trackedAddress)], Assert.IsType<string[]>(filters[1].Topics[1]));
        Assert.Null(filters[1].Topics[2]);
    }

    [Fact]
    public void ToAddressTopic_ZeroPadsAddressTopic()
    {
        var address = CreateAddress("0x1111111111111111111111111111111111111111");

        var topic = Erc20TransferLogFilterBuilder.ToAddressTopic(address);

        Assert.Equal("0x0000000000000000000000001111111111111111111111111111111111111111", topic);
    }

    [Fact]
    public void Build_BatchesTrackedAddresses()
    {
        var addresses = new[]
        {
            CreateAddress("0x1111111111111111111111111111111111111111"),
            CreateAddress("0x2222222222222222222222222222222222222222"),
            CreateAddress("0x3333333333333333333333333333333333333333")
        };

        var filters = Erc20TransferLogFilterBuilder.Build(addresses, addressBatchSize: 2);

        Assert.Equal(4, filters.Count);
        Assert.Equal(2, Assert.IsType<string[]>(filters[0].Topics[2]).Length);
        Assert.Single(Assert.IsType<string[]>(filters[2].Topics[2]));
    }

    private static Address CreateAddress(string value)
    {
        var result = Address.Create(value);
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
