using Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Caching;

/// <summary>
/// Loads enabled address labels into memory during startup.
/// </summary>
public sealed class AddressLabelCacheHydrationService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<AddressLabelCacheHydrationService> logger)
    : IHostedService
{
    /// <summary>
    /// Loads enabled address labels from persistence into the in-memory resolver cache.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAddressLabelRepository>();
        var cache = scope.ServiceProvider.GetRequiredService<IAddressLabelCacheService>();

        var labels = await repository.GetEnabledAsync(cancellationToken);
        cache.ReplaceAll(labels);

        logger.LogInformation("Hydrated address label cache with {AddressLabelCount} labels.", labels.Count);
    }

    /// <summary>
    /// No-op stop hook; cache lifetime is managed by dependency injection.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
