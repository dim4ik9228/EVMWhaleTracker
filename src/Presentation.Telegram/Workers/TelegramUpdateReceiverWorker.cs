using Application.Billing;
using System.Threading.Channels;
using Telegram.Bot.Types;
using Domain.Entities;
using Infrastructure.Billing;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Presentation.Telegram.Billing;
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
    private readonly Channel<Update> _commands = Channel.CreateBounded<Update>(new BoundedChannelOptions(64) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly IServiceScopeFactory _scopes;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly TelegramCommandUpdateHandler _updateHandler;
    private readonly ILogger<TelegramUpdateReceiverWorker> _logger;

    public TelegramUpdateReceiverWorker(
        ITelegramBotClient telegramBotClient,
        TelegramCommandUpdateHandler updateHandler,
        IOptions<TelegramBotOptions> options,
        ILogger<TelegramUpdateReceiverWorker> logger, IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(telegramBotClient);
        ArgumentNullException.ThrowIfNull(updateHandler);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrWhiteSpace(options.Value.BotToken))
        {
            throw new InvalidOperationException(TelegramUpdateReceiverWorkerText.BotTokenRequired);
        }

        _scopes = scopes;
        _telegramBotClient = telegramBotClient;
        _updateHandler = updateHandler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var dispatch = DispatchCommandsAsync(stoppingToken);
        try
        {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.OpenConnectionAsync(stoppingToken);
                var connection = (NpgsqlConnection)db.Database.GetDbConnection();
                await using var owner = new NpgsqlCommand("SELECT pg_try_advisory_lock(74230002)", connection);
                if (!(bool)(await owner.ExecuteScalarAsync(stoppingToken))!) { await Task.Delay(3000, stoppingToken); continue; }
                try
                {
                    var checkpoint = await db.Set<BillingCheckpoint>().SingleOrDefaultAsync(x => x.Key == BillingStates.TelegramUpdates, stoppingToken);
                    if (checkpoint is null) { checkpoint = new() { Key = BillingStates.TelegramUpdates }; db.Add(checkpoint); await db.SaveChangesAsync(stoppingToken); }
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var updates = await _telegramBotClient.GetUpdates(offset: checked((int)checkpoint.Offset), limit: 100, timeout: 25,
                            allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery, UpdateType.PreCheckoutQuery], cancellationToken: stoppingToken);
                        // Pre-checkout has its own priority path and never waits behind command rendering or the alert limiter.
                        foreach (var pre in updates.Where(u => u.PreCheckoutQuery is not null))
                        { try { using var preScope = _scopes.CreateScope(); await preScope.ServiceProvider.GetRequiredService<TelegramBilling>().HandleAsync(pre, stoppingToken); } catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { _logger.LogWarning(ex, BillingDiagnostics.WorkerFailure); } }
                        foreach (var update in updates.OrderBy(u => u.Id))
                        {
                            if (update.PreCheckoutQuery is null)
                            {
                                var financial = update.Message?.SuccessfulPayment is not null || update.Message?.RefundedPayment is not null;
                                // Financial failure escapes: no higher-offset GetUpdates call can acknowledge an undurable payment.
                                if (financial)
                                {
                                    using var paymentScope = _scopes.CreateScope();
                                    await paymentScope.ServiceProvider.GetRequiredService<TelegramBilling>().HandleAsync(update, stoppingToken);
                                }
                                else _commands.Writer.TryWrite(update);
                            }
                            checkpoint.Offset = (long)update.Id + 1;
                            await db.SaveChangesAsync(stoppingToken);
                            db.ChangeTracker.Clear();
                            db.Attach(checkpoint);
                        }
                    }
                }
                finally { if (connection.State == System.Data.ConnectionState.Open) { await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(74230002)", connection); await unlock.ExecuteScalarAsync(CancellationToken.None); } }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, BillingDiagnostics.WorkerFailure); await Task.Delay(2000, stoppingToken); }
        }
        }
        finally { _commands.Writer.TryComplete(); try { await dispatch; } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { } }
    }
    private async Task DispatchCommandsAsync(CancellationToken ct)
    {
        await foreach (var update in _commands.Reader.ReadAllAsync(ct))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                if (!await scope.ServiceProvider.GetRequiredService<TelegramBilling>().HandleAsync(update, ct))
                    await _updateHandler.HandleUpdateAsync(_telegramBotClient, update, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested) { _logger.LogWarning(ex, BillingDiagnostics.WorkerFailure); }
        }
    }
}
