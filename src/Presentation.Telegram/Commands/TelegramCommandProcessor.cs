using Presentation.Telegram.Commands.Parsing;
using Presentation.Telegram.Commands.PendingActions;
using Presentation.Telegram.Commands.StartPayloads;
using Presentation.Telegram.Commands.Wallets;

namespace Presentation.Telegram.Commands;

/// <summary>
/// Routes Telegram message text to pending action, deep-link, or wallet slash-command processors.
/// </summary>
public sealed class TelegramCommandProcessor(
    IPendingTelegramActionStore pendingActions,
    ITelegramPendingActionProcessor pendingActionProcessor,
    ITelegramStartPayloadProcessor startPayloadProcessor,
    ITelegramWalletCommandProcessor walletCommandProcessor)
    : ITelegramCommandProcessor
{
    /// <summary>
    /// Processes one Telegram text message and returns a response when the message belongs to the bot command flow.
    /// </summary>
    public async Task<TelegramCommandResponse?> ProcessAsync(
        TelegramCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.MessageText))
        {
            return null;
        }

        if (request.IsPrivateChat
            && !request.MessageText.TrimStart().StartsWith('/')
            && pendingActions.TryGet(request.TelegramUserId, out var pendingAction))
        {
            return await pendingActionProcessor.ProcessAsync(
                request.TelegramUserId,
                pendingAction,
                request.MessageText,
                cancellationToken);
        }

        if (!TelegramCommandParser.TryParse(request.MessageText, out var command))
        {
            return null;
        }

        if (!request.IsPrivateChat)
        {
            return new TelegramCommandResponse("Please open this bot in a private chat to manage tracked wallets.");
        }

        return command.Name switch
        {
            "start" => await startPayloadProcessor.ProcessAsync(
                request.TelegramUserId,
                command.Arguments,
                cancellationToken),
            "help" => new TelegramCommandResponse(TelegramCommandHelp.Text),
            "add" => await walletCommandProcessor.HandleAddAsync(
                request.TelegramUserId,
                command.Arguments,
                cancellationToken),
            "list" => await walletCommandProcessor.HandleListAsync(request.TelegramUserId, cancellationToken),
            "remove" => await walletCommandProcessor.HandleRemoveAsync(
                request.TelegramUserId,
                command.Arguments,
                cancellationToken),
            _ => new TelegramCommandResponse(TelegramCommandHelp.Text)
        };
    }
}
