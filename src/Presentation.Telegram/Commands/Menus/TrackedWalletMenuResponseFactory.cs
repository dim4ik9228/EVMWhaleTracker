using Application.Abstractions;
using Application.TrackedWallets;
using Infrastructure.Blockchain;
using Presentation.Telegram.Commands;
using Presentation.Telegram.Services;

namespace Presentation.Telegram.Commands.Menus;

/// <summary>
/// Resolves configured runtime chain metadata and maps rendered wallet menus to Telegram command responses.
/// </summary>
public sealed class TrackedWalletMenuResponseFactory(
    ITrackedWalletMenuRenderer menuRenderer,
    IChainMetadataProvider chainMetadataProvider,
    IReadOnlyList<BlockchainNetworkOptions> configuredNetworks)
    : ITrackedWalletMenuResponseFactory
{
    /// <inheritdoc />
    public TelegramCommandResponse CreateOverviewResponse(TrackedWalletDetails wallet, bool deleteSourceMessage = false)
    {
        var menu = menuRenderer.RenderOverview(wallet, GetConfiguredChainMetadata());
        return new TelegramCommandResponse(
            menu.Text,
            menu.ParseMode,
            menu.ReplyMarkup,
            deleteSourceMessage);
    }

    private IReadOnlyList<ChainMetadata> GetConfiguredChainMetadata()
        => configuredNetworks
            .Select(network => chainMetadataProvider.Get(network.Chain))
            .ToArray();
}
