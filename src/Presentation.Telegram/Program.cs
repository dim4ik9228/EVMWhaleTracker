using Application.Polymarket;
using Infrastructure.Polymarket;
using Application.Messaging;
using Application.Hyperliquid;
using Infrastructure.Hyperliquid;
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
    builder.Configuration.GetSection(HostConfiguration.BlockchainIndexer));

builder.Services.Configure<BlockchainNetworksOptions>(
    builder.Configuration.GetSection(HostConfiguration.BlockchainNetworks));

builder.Services.Configure<AssetPriceOptions>(
    builder.Configuration.GetSection(HostConfiguration.AssetPrices));

builder.Services.Configure<TelegramBotOptions>(
    builder.Configuration.GetSection(HostConfiguration.TelegramBot));

builder.Services.Configure<EnsReverseLookupOptions>(
    builder.Configuration.GetSection(HostConfiguration.EnsReverseLookup));

var connectionString = builder.Configuration[HostConfiguration.ConnectionString];
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(HostDiagnostics.ConnectionStringRequired);
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<ITrackedWalletRepository, TrackedWalletRepository>();
builder.Services.AddOptions<PolymarketOptions>().Bind(builder.Configuration.GetSection(HostConfiguration.Polymarket))
    .Validate(o => o.IsValidForStartup(), HostDiagnostics.InvalidPolymarketOptions).ValidateOnStart();
builder.Services.AddHttpClient(HostConfiguration.Polymarket, client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<IPolymarketProfileResolver>(sp => new PolymarketProfileResolver(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(HostConfiguration.Polymarket), sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PolymarketOptions>>()));
builder.Services.AddScoped<IPolymarketSettings, PolymarketSettings>();
builder.Services.AddOptions<PolymarketPolygonOptions>().Bind(builder.Configuration.GetSection(HostConfiguration.PolymarketPolygon))
    .PostConfigure(o => { if (string.IsNullOrWhiteSpace(o.RpcApiKey)) o.RpcApiKey = builder.Configuration[HostConfiguration.RpcApiKey]; })
    .Validate(o => o.IsValid(), HostDiagnostics.InvalidPolygonOptions).ValidateOnStart();
builder.Services.AddHttpClient(HostConfiguration.PolymarketPolygon, client => client.Timeout = Timeout.InfiniteTimeSpan)
    // Infura credentials are part of the URL path; factory request logs must never include it.
    .RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<IPolymarketPolygonRpc>(sp => new PolymarketPolygonRpc(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(HostConfiguration.PolymarketPolygon), sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PolymarketPolygonOptions>>()));
builder.Services.AddSingleton<IPolymarketMarketResolver>(sp => new PolymarketMarketResolver(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(HostConfiguration.Polymarket), sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PolymarketPolygonOptions>>()));
builder.Services.AddSingleton<PolymarketPolygonHealth>();
builder.Services.AddScoped<PolymarketPolygonStore>();
builder.Services.AddHostedService<PolymarketPolygonWorker>();
builder.Services.AddScoped<IAddressLabelRepository, AddressLabelRepository>();
builder.Services.AddOptions<HyperliquidOptions>().Bind(builder.Configuration.GetSection(HostConfiguration.Hyperliquid))
    .Validate(o => o.IsValidForStartup(), HostDiagnostics.InvalidHyperliquidOptions).ValidateOnStart();
builder.Services.AddSingleton<HyperliquidChangeSignal>();
builder.Services.AddScoped<IHyperliquidSettings, HyperliquidSettings>();
builder.Services.AddScoped<HyperliquidStore>();
builder.Services.AddSingleton<IHyperliquidSocketFactory, HyperliquidSocketFactory>();
builder.Services.AddHttpClient(HostConfiguration.Hyperliquid, client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<IHyperliquidInfoClient>(sp => new HyperliquidInfoClient(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(HostConfiguration.Hyperliquid), sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HyperliquidOptions>>()));
builder.Services.AddHostedService<HyperliquidWorker>();
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

    Console.WriteLine(HostDiagnostics.EnabledNetworks(string.Join(", ", enabledBlockchainNetworks.Select(network => network.Chain))));
}
else
{
    Console.WriteLine(HostDiagnostics.NoNetworks);
}

// Configure Telegram Bot
var telegramSection = builder.Configuration.GetSection(HostConfiguration.TelegramBot);
var botToken = telegramSection[HostConfiguration.BotToken];
if (!string.IsNullOrWhiteSpace(botToken))
{
    builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));
    builder.Services.AddSingleton<ITelegramBotUsernameProvider, TelegramBotUsernameProvider>();
    builder.Services.AddSingleton<TelegramCommandUpdateHandler>();
    builder.Services.AddHostedService<TelegramUpdateReceiverWorker>();
    builder.Services.AddHostedService<NotificationDispatcherWorker>();
    builder.Services.AddSingleton<PolymarketNotificationRenderer>();
    builder.Services.AddSingleton<IPolymarketNotificationSender, PolymarketNotificationSender>();
    builder.Services.AddHostedService<PolymarketDeliveryWorker>();
    builder.Services.AddSingleton<HyperliquidNotificationRenderer>();
    builder.Services.AddSingleton<IHyperliquidNotificationSender, HyperliquidNotificationSender>();
    builder.Services.AddHostedService<HyperliquidDeliveryWorker>();
}
else
{
    Console.WriteLine(HostDiagnostics.TelegramDisabled);
}

var app = builder.Build();


using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
}

if (args.Length > 0 && string.Equals(args[0], HostConfiguration.ImportAddressLabels, StringComparison.OrdinalIgnoreCase))
{
    await app.Services.RunAddressLabelImportAsync(args);
    return;
}

await app.RunAsync();
