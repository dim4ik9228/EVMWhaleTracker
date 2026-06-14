using Application.Messaging;
using Application.Abstractions;
using Infrastructure.Caching;
using Infrastructure.Blockchain;
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

builder.Services.Configure<AssetPriceOptions>(
    builder.Configuration.GetSection("AssetPrices"));

builder.Services.Configure<TelegramBotOptions>(
    builder.Configuration.GetSection("TelegramBot"));

var connectionString = builder.Configuration["ConnectionStrings:Default"];
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Default must be configured.");
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<ITrackedWalletRepository, TrackedWalletRepository>();
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<AssemblyReference>();
    cfg.AddOpenBehavior(typeof(RequestLoggingPipelineBehavior<,>));
});

builder.Services.AddMemoryCache();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<OnChainPipelineChannels>();
builder.Services.AddSingleton<InMemoryCacheService>();
builder.Services.AddSingleton<TelegramGlobalRateLimiter>();
builder.Services.AddSingleton<IChainMetadataProvider, StaticChainMetadataProvider>();
builder.Services.AddSingleton<Erc20TransferLogParser>();
builder.Services.AddSingleton<ITrackedAddressCacheService>(sp => sp.GetRequiredService<InMemoryCacheService>());
builder.Services.AddSingleton<ITrackedWalletCacheService>(sp => sp.GetRequiredService<InMemoryCacheService>());
builder.Services.AddSingleton<IPendingWalletAliasRequestStore, InMemoryPendingWalletAliasRequestStore>();
builder.Services.AddScoped<ITelegramCommandProcessor, TelegramCommandProcessor>();
builder.Services.AddSingleton<ITelegramNotificationRenderer, TelegramNotificationRenderer>();
builder.Services.AddHostedService<TrackedWalletCacheHydrationService>();

var blockchainIndexerSection = builder.Configuration.GetSection("BlockchainIndexer");
var httpRpcUrl = blockchainIndexerSection["HttpRpcUrl"];
var webSocketRpcUrl = blockchainIndexerSection["WebSocketRpcUrl"];
if (!string.IsNullOrWhiteSpace(httpRpcUrl) || !string.IsNullOrWhiteSpace(webSocketRpcUrl))
{
    builder.Services.AddSingleton<ITokenMetadataProvider, NethereumTokenMetadataProvider>();
    builder.Services.AddHttpClient<IAssetPriceProvider, HttpAssetPriceProvider>();
    if (!string.IsNullOrWhiteSpace(webSocketRpcUrl))
    {
        builder.Services.AddHostedService<TokenTransferLogSubscriberWorker>();
    }
    else
    {
        Console.WriteLine("BlockchainIndexer:WebSocketRpcUrl is not configured. Token transfer log subscriber is disabled.");
    }

    builder.Services.AddHostedService<WalletTrackerWorker>();
    builder.Services.AddHostedService<NotificationAggregationWorker>();
}
else
{
    Console.WriteLine("Neither BlockchainIndexer:HttpRpcUrl nor BlockchainIndexer:WebSocketRpcUrl is configured. Blockchain workers are disabled.");
}

var telegramSection = builder.Configuration.GetSection("TelegramBot");
var botToken = telegramSection["BotToken"];
if (!string.IsNullOrWhiteSpace(botToken))
{
    builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));
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

await app.RunAsync();
