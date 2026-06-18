using Application.Messaging;
using Application.Abstractions;
using Infrastructure.Caching;
using Infrastructure.AddressLabels;
using Infrastructure.Blockchain;
using Infrastructure.Ens;
using Infrastructure.Persistence;
using Infrastructure.Pricing;
using Infrastructure.Workers;
using Application.Behaviors;
using Application;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Presentation.Telegram.Configuration;
using Presentation.Telegram.Commands;
using Presentation.Telegram.Commands.Callbacks;
using Presentation.Telegram.Commands.Menus;
using Presentation.Telegram.Commands.PendingActions;
using Presentation.Telegram.Commands.StartPayloads;
using Presentation.Telegram.Commands.Wallets;
using Presentation.Telegram.Handlers;
using Presentation.Telegram.Services;
using Presentation.Telegram.Workers;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddUserSecrets<Program>(optional: true, reloadOnChange: true);

builder.Logging.ClearProviders();
builder.Services.AddSerilog((services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services);
});

builder.Services.Configure<BlockchainIndexerOptions>(
    builder.Configuration.GetSection("BlockchainIndexer"));

builder.Services.Configure<BlockchainNetworksOptions>(
    builder.Configuration.GetSection("BlockchainNetworks"));

builder.Services.Configure<AssetPriceOptions>(
    builder.Configuration.GetSection("AssetPrices"));

builder.Services.Configure<TelegramBotOptions>(
    builder.Configuration.GetSection("TelegramBot"));

builder.Services.Configure<EnsReverseLookupOptions>(
    builder.Configuration.GetSection("EnsReverseLookup"));

var connectionString = builder.Configuration["ConnectionStrings:Default"];
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Default must be configured.");
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<ITrackedWalletRepository, TrackedWalletRepository>();
builder.Services.AddScoped<IAddressLabelRepository, AddressLabelRepository>();
builder.Services.AddScoped<AddressLabelSeedImporter>();
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<AssemblyReference>();
    cfg.AddOpenBehavior(typeof(RequestLoggingPipelineBehavior<,>));
});

builder.Services.AddMemoryCache();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<OnChainPipelineChannels>();
builder.Services.AddSingleton<InMemoryCacheService>();
builder.Services.AddSingleton<InMemoryAddressLabelCacheService>();
builder.Services.AddSingleton<TelegramGlobalRateLimiter>();
builder.Services.AddSingleton<IChainMetadataProvider, StaticChainMetadataProvider>();
builder.Services.AddSingleton<Erc20TransferLogParser>();
builder.Services.AddSingleton<ITrackedAddressCacheService>(sp => sp.GetRequiredService<InMemoryCacheService>());
builder.Services.AddSingleton<ITrackedWalletCacheService>(sp => sp.GetRequiredService<InMemoryCacheService>());
builder.Services.AddSingleton<IAddressLabelCacheService>(sp => sp.GetRequiredService<InMemoryAddressLabelCacheService>());
builder.Services.AddSingleton<IAddressLabelResolver, AddressLabelResolver>();
builder.Services.AddHttpClient<IEnsReverseLookupProvider, EthereumEnsReverseLookupProvider>();
builder.Services.AddSingleton<IPendingTelegramActionStore, InMemoryPendingTelegramActionStore>();
builder.Services.AddScoped<ITelegramWalletCommandProcessor, TelegramWalletCommandProcessor>();
builder.Services.AddScoped<ITelegramPendingActionProcessor, TelegramPendingActionProcessor>();
builder.Services.AddScoped<ITelegramStartPayloadProcessor, TelegramStartPayloadProcessor>();
builder.Services.AddScoped<ITrackedWalletMenuResponseFactory, TrackedWalletMenuResponseFactory>();
builder.Services.AddScoped<ITelegramCommandProcessor, TelegramCommandProcessor>();
builder.Services.AddScoped<ITelegramCallbackProcessor, TelegramCallbackProcessor>();
builder.Services.AddSingleton<ITelegramNotificationEnricher, TelegramNotificationEnricher>();
builder.Services.AddSingleton<ITelegramNotificationRenderer, TelegramNotificationRenderer>();
builder.Services.AddSingleton<ITrackedWalletMenuRenderer, TrackedWalletMenuRenderer>();
builder.Services.AddHostedService<TrackedWalletCacheHydrationService>();
builder.Services.AddHostedService<AddressLabelCacheHydrationService>();

var enabledBlockchainNetworks = BlockchainNetworkOptionsResolver.Resolve(builder.Configuration);
builder.Services.AddSingleton<IReadOnlyList<BlockchainNetworkOptions>>(enabledBlockchainNetworks);

// Configure blockchain workers only
if (enabledBlockchainNetworks.Count > 0)
{
    builder.Services.AddSingleton<ITokenMetadataProvider, NethereumTokenMetadataProvider>();
    builder.Services.AddHttpClient<IAssetPriceProvider, HttpAssetPriceProvider>();

    foreach (var network in enabledBlockchainNetworks)
    {
        builder.Services.AddSingleton<IHostedService>(sp =>
            new TokenTransferLogSubscriberWorker(
                sp.GetRequiredService<OnChainPipelineChannels>(),
                sp.GetRequiredService<ITrackedAddressCacheService>(),
                network,
                sp.GetRequiredService<ILogger<TokenTransferLogSubscriberWorker>>()));
    }

    builder.Services.AddHostedService<WalletTrackerWorker>();
    builder.Services.AddHostedService<NotificationAggregationWorker>();

    Console.WriteLine($"Enabled blockchain networks: {string.Join(", ", enabledBlockchainNetworks.Select(network => network.Chain))}");
}
else
{
    Console.WriteLine("No blockchain networks are configured. Blockchain workers are disabled.");
}

// Configure Telegram Bot
var telegramSection = builder.Configuration.GetSection("TelegramBot");
var botToken = telegramSection["BotToken"];
if (!string.IsNullOrWhiteSpace(botToken))
{
    builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));
    builder.Services.AddSingleton<ITelegramBotUsernameProvider, TelegramBotUsernameProvider>();
    builder.Services.AddSingleton<TelegramCommandUpdateHandler>();
    builder.Services.AddHostedService<TelegramUpdateReceiverWorker>();
    builder.Services.AddHostedService<NotificationDispatcherWorker>();
}
else
{
    Console.WriteLine("TelegramBot:BotToken is not configured. Telegram workers are disabled.");
}

var app = builder.Build();


using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
}

if (args.Length > 0 && string.Equals(args[0], "import-address-labels", StringComparison.OrdinalIgnoreCase))
{
    await app.Services.RunAddressLabelImportAsync(args);
    return;
}

await app.RunAsync();
