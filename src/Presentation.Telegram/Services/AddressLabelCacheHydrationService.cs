using Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Presentation.Telegram.Services;

/// <summary>
/// Loads enabled address labels into memory during startup.
/// </summary>
public sealed class AddressLabelCacheHydrationService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<AddressLabelCacheHydrationService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAddressLabelRepository>();
        var cache = scope.ServiceProvider.GetRequiredService<IAddressLabelCacheService>();

        var labels = await repository.GetEnabledAsync(cancellationToken);
        cache.ReplaceAll(labels);

        logger.LogInformation("Hydrated address label cache with {AddressLabelCount} labels.", labels.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
