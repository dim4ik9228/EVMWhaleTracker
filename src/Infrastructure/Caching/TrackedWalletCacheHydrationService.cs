using Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Caching;

/// <summary>
/// Loads persisted tracked wallets into the hot-path in-memory cache during startup.
/// </summary>
public sealed class TrackedWalletCacheHydrationService : IHostedService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<TrackedWalletCacheHydrationService> _logger;

    public TrackedWalletCacheHydrationService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<TrackedWalletCacheHydrationService> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Loads persisted tracked wallets into the in-memory lookup used by blockchain workers.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITrackedWalletRepository>();
        var cache = scope.ServiceProvider.GetRequiredService<ITrackedWalletCacheService>();

        var trackedWallets = await repository.ListAllAsync(cancellationToken);
        cache.ReplaceAll(trackedWallets);

        _logger.LogInformation(TrackedWalletCacheHydrationServiceDiagnostics.HydratedTrackedWalletCacheWithWallets, trackedWallets.Count);
    }

    /// <summary>
    /// No-op stop hook; cache lifetime is managed by dependency injection.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
