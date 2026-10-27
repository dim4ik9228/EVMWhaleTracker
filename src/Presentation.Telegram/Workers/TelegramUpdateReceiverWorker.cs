using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Presentation.Telegram.Configuration;
using Presentation.Telegram.Handlers;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace Presentation.Telegram.Workers;

/// <summary>
/// Long-running Telegram polling worker for user commands.
/// </summary>
public sealed class TelegramUpdateReceiverWorker : BackgroundService
{
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly TelegramCommandUpdateHandler _updateHandler;
    private readonly ILogger<TelegramUpdateReceiverWorker> _logger;

    public TelegramUpdateReceiverWorker(
        ITelegramBotClient telegramBotClient,
        TelegramCommandUpdateHandler updateHandler,
        IOptions<TelegramBotOptions> options,
        ILogger<TelegramUpdateReceiverWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(telegramBotClient);
        ArgumentNullException.ThrowIfNull(updateHandler);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrWhiteSpace(options.Value.BotToken))
        {
            throw new InvalidOperationException(TelegramUpdateReceiverWorkerText.BotTokenRequired);
        }

        _telegramBotClient = telegramBotClient;
        _updateHandler = updateHandler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
        };

        _telegramBotClient.StartReceiving(_updateHandler, receiverOptions, stoppingToken);
        _logger.LogInformation(TelegramUpdateReceiverWorkerText.TelegramUpdateReceiverStarted);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
    }
}
